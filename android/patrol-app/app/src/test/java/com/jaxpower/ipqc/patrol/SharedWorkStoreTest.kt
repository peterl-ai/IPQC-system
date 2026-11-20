package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.work.*
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test

class SharedWorkStoreTest {
    @Test fun operationIdCanonicalBytesAndLocalEditSurviveRetry() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = SharedWorkTestFixture(server)
            val access = f.preparedSource()
            val requested = f.capture()
            val saved = f.store.enqueue(access, requested).envelope
            val reopened = f.reopen()
            val equivalent = requested.copy(data = requested.data!!.copy(inspectionValue = "1.23"))
            val same = reopened.enqueue(access, equivalent).envelope
            assertEquals(saved.workspaceId, same.workspaceId)
            assertEquals(saved.canonicalPayload, same.canonicalPayload)
            assertEquals(saved.contentHash, same.contentHash)
            assertEquals(1L, same.originLocalEditVersion)
            assertEquals(7L, same.operation.baseDraftVersion)
            expectWork(WorkProblem.OperationIdentityConflict) {
                reopened.enqueue(access, equivalent.copy(data = equivalent.data!!.copy(barcode = "changed")))
            }
            val next = reopened.enqueue(access, f.capture(11, sample = testId(12))).envelope
            assertEquals(2L, next.originLocalEditVersion)
            assertEquals(7L, next.operation.baseDraftVersion)
            assertEquals(2, server.web.requestCount)
        }
    }

    @Test fun immutableBatchAndDependenciesCannotBeRebound() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = SharedWorkTestFixture(server)
            val access = f.preparedSource()
            val capture = f.store.enqueue(access, f.capture()).envelope
            val correction = f.capture(11).copy(kind = SampleKind.CorrectSample, dependencies = listOf(capture.reference()))
            val queued = f.store.enqueue(access, correction).envelope
            assertEquals(listOf(capture.reference()), queued.operation.dependencies)
            val batchId = testId(20)
            expectWork(WorkProblem.DependencyMismatch) {
                f.store.prepareBatch(access, batchId, listOf(correction.operationId))
            }
            val ids = listOf(correction.operationId, capture.operation.operationId)
            val batch = f.store.prepareBatch(access, batchId, ids)
            assertEquals(batch, f.reopen().prepareBatch(access, batchId, ids.reversed()))
            expectWork(WorkProblem.BatchIdentityConflict) {
                f.store.prepareBatch(access, batchId, ids.takeLast(1))
            }
            expectWork(WorkProblem.BatchIdentityConflict) {
                f.reopen().prepareBatch(access, testId(21), ids)
            }
            assertEquals(batchId, f.reopen().operation(access, correction.operationId)?.batchId)
            assertEquals(WorkState.Queued, f.reopen().batch(access, batchId)?.state)
        }
    }

    @Test fun reopenAndPointInTimeNotFoundNeverRequeueUnknownAttempt() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = SharedWorkTestFixture(server)
            val access = f.preparedSource()
            val op = f.store.enqueue(access, f.capture())
            val id = testId(21)
            val batch = f.store.prepareBatch(access, id, listOf(op.envelope.operation.operationId))
            f.store.startAttempt(access, id)
            val reopened = f.reopen()
            reopened.recoverInterrupted(access)
            reopened.recordOutcomeUnknown(access, id)
            assertEquals(WorkState.OutcomeUnknown, reopened.batch(access, id)?.state)
            assertEquals(batch.fingerprint, reopened.batch(access, id)?.fingerprint)
            expectWork(WorkProblem.InvalidState) { reopened.startAttempt(access, id) }
            expectWork(WorkProblem.InvalidState) {
                reopened.recordRejection(access, id, WorkConflict.SourceSecurityRevoked)
            }
            assertEquals(WorkState.OutcomeUnknown, reopened.batch(access, id)?.state)
            val other = reopened.enqueue(access, f.capture(22, sample = testId(23)))
            reopened.prepareBatch(access, testId(24), listOf(other.envelope.operation.operationId))
            expectWork(WorkProblem.InvalidState) { reopened.startAttempt(access, testId(24)) }
            assertEquals(2, server.web.requestCount)
        }
    }

    @Test fun onlyMatchingBatchReceiptAcceptsItsOwnOperationsAndPreservesUploader() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = SharedWorkTestFixture(server)
            val access = f.preparedSource()
            val op = f.store.enqueue(access, f.capture())
            val id = testId(20)
            val batch = f.store.prepareBatch(access, id, listOf(op.envelope.operation.operationId))
            val receipt = f.receipt(batch, listOf(op))
            expectWork(WorkProblem.InvalidState) { f.store.recordReceipt(access, receipt) }
            f.store.startAttempt(access, id)
            f.store.recordOutcomeUnknown(access, id)
            val later = f.store.enqueue(access, f.capture(31, sample = testId(32)))
            for (bad in listOf(
                receipt.copy(requestFingerprint = "F".repeat(64)),
                receipt.copy(operations = emptyList()),
                receipt.copy(operations = listOf(receipt.operations.single().copy(contributorUserId = f.b.userId))),
                receipt.copy(operations = listOf(receipt.operations.single().copy(inspectionAttemptId = testId(100)))),
                receipt.copy(operations = listOf(receipt.operations.single().copy(value = ServerSampleValue("Quantitative", "99", "OK", "fixture barcode")))),
                receipt.copy(draftVersion = 9),
            )) expectWork(WorkProblem.ReceiptMismatch) { f.store.recordReceipt(access, bad) }
            assertEquals(WorkState.OutcomeUnknown, f.store.batch(access, id)?.state)

            f.store.recordReceipt(access, receipt)
            f.reopen().recordReceipt(access, receipt)
            val accepted = requireNotNull(f.reopen().batch(access, id))
            assertEquals(WorkState.Accepted, accepted.state)
            assertEquals(f.b.familyId, accepted.receipt?.uploadedPersonalSessionId)
            assertEquals(f.a.userId, accepted.receipt?.operations?.single()?.contributorUserId)
            assertEquals(2L, later.envelope.originLocalEditVersion)
            assertEquals(WorkState.Queued, f.reopen().operation(access, later.envelope.operation.operationId)?.state)
            assertEquals(7L, later.envelope.operation.baseDraftVersion)
        }
    }

    @Test fun acceptedPredecessorIsBoundByHashAndItsOriginalReceipt() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = SharedWorkTestFixture(server)
            val access = f.preparedSource()
            val first = f.store.enqueue(access, f.capture())
            val initial = f.store.prepareBatch(access, testId(20), listOf(first.envelope.operation.operationId))
            f.store.startAttempt(access, initial.batchId)
            val prior = f.receipt(initial, listOf(first))
            f.store.recordReceipt(access, prior)
            val newer = f.access(version = 8)
            val correct = f.capture(11).copy(baseDraftVersion = 8, kind = SampleKind.CorrectSample,
                dependencies = listOf(first.envelope.reference()))
            expectWork(WorkProblem.DependencyMismatch) {
                f.store.enqueue(newer, correct.copy(dependencies = listOf(first.envelope.reference().copy(contentHash = "0".repeat(64)))))
            }
            val queued = f.store.enqueue(newer, correct)
            val batch = f.store.prepareBatch(newer, testId(21), listOf(correct.operationId))
            assertEquals(listOf(first.envelope.reference()), batch.acceptedReferences)
            f.store.startAttempt(newer, batch.batchId)
            expectWork(WorkProblem.ReceiptMismatch) {
                f.store.recordReceipt(newer, f.receipt(batch, listOf(queued)))
            }
            val corrected = f.receipt(batch, listOf(queued), references = prior.operations)
            f.store.recordReceipt(newer, corrected)
            assertEquals(WorkState.Accepted, f.store.batch(newer, batch.batchId)?.state)

            val current = f.access(version = 9)
            val remove = f.capture(12).copy(baseDraftVersion = 9, kind = SampleKind.RemoveSample,
                data = null, dependencies = listOf(queued.envelope.reference()))
            val removed = f.store.enqueue(current, remove)
            val removalBatch = f.store.prepareBatch(current, testId(22), listOf(remove.operationId))
            f.store.startAttempt(current, removalBatch.batchId)
            val previous = corrected.operations.single { it.operationId == correct.operationId }
            val raw = f.receipt(removalBatch, listOf(removed))
            val result = raw.operations.single().copy(value = null, isRemoved = true, valueOperationId = previous.valueOperationId)
            expectWork(WorkProblem.ReceiptMismatch) {
                f.store.recordReceipt(current, raw.copy(operations = listOf(previous, result.copy(valueOperationId = remove.operationId))))
            }
            f.store.recordReceipt(current, raw.copy(operations = listOf(previous, result)))
            assertTrue(f.store.batch(current, removalBatch.batchId)?.receipt?.operations
                ?.single { it.operationId == remove.operationId }?.isRemoved == true)
        }
    }

    @Test fun sessionBoundTaskAndSourceAuthorizationPreserveAUnderB() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = SharedWorkTestFixture(server)
            expectIssue(Issue.UNAUTHENTICATED) { server.session.bindWorkSession() }
            val a = f.preparedSource()
            val original = f.store.enqueue(a, f.capture()).envelope
            server.session.logout(attemptRemote = false)
            assertNull(server.vault.saved)
            f.login(f.b)
            expectIssue(Issue.UNAUTHENTICATED) { f.reopen().operation(a, original.operation.operationId) }
            val bBeforePreflight = f.access(checked = emptyList())
            assertTrue(f.reopen().operations(bBeforePreflight).isEmpty())
            expectWork(WorkProblem.SourceUnavailable) { f.store.enqueue(bBeforePreflight, f.capture(11)) }
            expectWork(WorkProblem.SourceUnavailable) { f.store.storeSource(bBeforePreflight, f.source, f.proof) }
            val b = f.access()
            assertThrows(IllegalArgumentException::class.java) { f.task.copy(workflowVersion = 1) }
            expectWork(WorkProblem.ScopeMismatch) { f.access(identity = f.task.copy(environmentId = "other-env")) }
            expectIssue(Issue.UNAUTHENTICATED) {
                f.reopen("https://wrong.example.invalid/").operation(b, original.operation.operationId)
            }
            for (wrong in listOf(f.task.copy(taskId = testId(98)), f.task.copy(lineId = Fixture.SECOND_LINE))) {
                assertNull(f.reopen().operation(f.access(identity = wrong), original.operation.operationId))
            }
            val retained = requireNotNull(f.reopen().operation(b, original.operation.operationId)).envelope
            assertEquals(original.contentHash, retained.contentHash)
            assertEquals(f.a.userId, retained.source.contributorUserId)
            assertEquals(f.a.familyId, retained.source.originPersonalSessionId)
            val bSource = f.sourceFor(f.b, testId(8))
            f.store.storeSource(b, bSource, f.proof)
            expectWork(WorkProblem.SourceUnavailable) {
                f.store.enqueue(bBeforePreflight, f.capture(11, bSource).copy(kind = SampleKind.CorrectSample,
                    dependencies = listOf(original.reference())))
            }
            expectWork(WorkProblem.ScopeMismatch) {
                f.store.enqueue(b, f.capture(11, bSource).copy(attempt = f.attempt.copy(inspectionAttemptId = testId(39))))
            }
            val new = f.store.enqueue(b, f.capture(11, bSource, testId(18))).envelope
            assertEquals(f.b.userId, new.source.contributorUserId)
            assertEquals(4, server.web.requestCount)
        }
    }
}
