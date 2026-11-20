package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.work.*
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test

class SharedWorkStoreTest {
    @Test fun captureRequiresTimeStrictlyBeforeDeadlineButExactReplaySurvivesIt() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = SharedWorkTestFixture(server)
            val access = f.preparedSource()
            f.offline.advance(59_999)
            val saved = f.store.enqueue(access, f.capture()).envelope
            f.offline.advance(1)
            expectWork(WorkProblem.CaptureWindowClosed) { f.store.enqueue(access, f.capture(11)) }
            f.offline.advance(1)
            expectWork(WorkProblem.CaptureWindowClosed) { f.store.enqueue(access, f.capture(12)) }
            val replay = f.reopen().enqueue(access, f.capture()).envelope
            assertEquals(saved.canonicalPayload, replay.canonicalPayload)
            assertEquals(saved.contentHash, replay.contentHash)
            assertEquals(saved.originLocalEditVersion, replay.originLocalEditVersion)
            expectWork(WorkProblem.OperationIdentityConflict) { f.store.enqueue(access, f.capture(sample = testId(42))) }
            assertEquals(1, f.store.operations(access).size)
        }
    }

    @Test fun refreshedPersonalAuthorityDoesNotExtendSourceButNewContextCanCapture() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = SharedWorkTestFixture(server)
            val before = f.preparedSource()
            f.offline.advance(300_000)
            f.rotate(restart = false)
            val after = f.access()
            assertNotSame(before.session.authorityId, after.session.authorityId)
            expectIssue(Issue.UNAUTHENTICATED) { f.store.enqueue(before, f.capture()) }
            expectWork(WorkProblem.CaptureWindowClosed) { f.store.enqueue(after, f.capture()) }
            val fresh = f.sourceFor(f.a, testId(8))
            f.store.storeSource(after, fresh, f.proof)
            val saved = f.store.enqueue(after, f.capture(context = fresh)).envelope
            assertEquals(fresh.contextId, saved.source.contextId)
            assertEquals(1L, saved.originLocalEditVersion)
        }
    }

    @Test fun repositoryReopenNewAccessAndLineSwitchCannotRenewAContextAcrossReboot() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = SharedWorkTestFixture(server)
            val original = f.preparedSource()
            val secondTask = f.task.copy(taskId = testId(41), lineId = Fixture.SECOND_LINE)
            val secondContext = f.sourceFor(f.a, testId(8), secondTask)
            f.store.storeSource(f.access(secondTask), secondContext, f.proof)
            val saved = f.store.enqueue(original, f.capture()).envelope
            f.offline.advance(60_000)
            f.offline.rebootAt(60_000)
            expectWork(WorkProblem.CaptureWindowClosed) { f.reopen().enqueue(f.access(), f.capture(11)) }
            expectWork(WorkProblem.CaptureWindowClosed) {
                f.reopen().enqueue(f.access(secondTask), f.capture(11, secondContext))
            }

            f.rotate(restart = true)
            val restored = f.access()
            expectWork(WorkProblem.CaptureWindowClosed) { f.store.enqueue(restored, f.capture(11)) }
            assertEquals(saved.contentHash, f.store.enqueue(restored, f.capture()).envelope.contentHash)
        }
    }

    @Test fun rollbackMissingAndCorruptClockEvidenceFailClosedForNewCapture() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = SharedWorkTestFixture(server)
            val access = f.preparedSource()
            f.offline.advance(1_000)
            val saved = f.store.enqueue(access, f.capture()).envelope
            f.offline.rollbackWallClock(1)
            expectWork(WorkProblem.CaptureTimeUnavailable) { f.store.enqueue(access, f.capture(11)) }
            expectWork(WorkProblem.CaptureTimeUnavailable) { f.reopen().enqueue(f.access(), f.capture(11)) }
            assertEquals(saved.contentHash, f.store.enqueue(access, f.capture()).envelope.contentHash)
        }

        TestServer().use { server ->
            val f = SharedWorkTestFixture(server)
            val access = f.preparedSource()
            f.offline.receipt.replace("invalid protected clock record".encodeToByteArray())
            expectWork(WorkProblem.CaptureTimeUnavailable) { f.store.enqueue(access, f.capture()) }
            assertNull(f.store.operation(access, f.capture().operationId))
        }
    }

    @Test fun knownSecurityRevocationBlocksCaptureAndTransportWithoutErasingReplay() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = SharedWorkTestFixture(server)
            val access = f.preparedSource()
            val saved = f.store.enqueue(access, f.capture())
            val batch = f.store.prepareBatch(access, testId(20), listOf(saved.envelope.operation.operationId))
            val revoked = f.access(revoked = setOf(f.source.contextId))
            expectWork(WorkProblem.SourceUnavailable) { f.store.enqueue(revoked, f.capture(11)) }
            expectWork(WorkProblem.SourceUnavailable) { f.store.startAttempt(revoked, batch.batchId) }
            assertEquals(WorkState.Queued, f.store.enqueue(revoked, f.capture()).state)
        }
    }

    @Test fun operationIdIsExclusiveAcrossTasksLinesAndSourceContexts() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = SharedWorkTestFixture(server)
            val firstTaskAccess = f.preparedSource()
            val saved = f.store.enqueue(firstTaskAccess, f.capture()).envelope
            val secondContext = f.sourceFor(f.a, testId(8))
            f.store.storeSource(firstTaskAccess, secondContext, f.proof)
            expectWork(WorkProblem.OperationIdentityConflict) {
                f.store.enqueue(firstTaskAccess, f.capture(context = secondContext))
            }

            val sameLine = f.task.copy(taskId = testId(42))
            val otherLine = f.task.copy(taskId = testId(43), lineId = Fixture.SECOND_LINE)
            for (scope in listOf(sameLine, otherLine)) {
                val access = f.access(scope)
                val source = f.sourceFor(f.a, testId(9), scope)
                val reopened = f.reopen()
                reopened.storeSource(access, source, f.proof)
                expectWork(WorkProblem.OperationIdentityConflict) { reopened.enqueue(access, f.capture(context = source)) }
                assertTrue(reopened.operations(access).isEmpty())
            }
            assertEquals(saved.contentHash, f.reopen().enqueue(firstTaskAccess, f.capture()).envelope.contentHash)
        }
    }

    @Test fun operationIdAlsoConflictsAcrossEnvironmentsWithinTheInstallation() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = SharedWorkTestFixture(server)
            val first = f.preparedSource()
            val original = f.store.enqueue(first, f.capture()).envelope
            f.sessions.logout(attemptRemote = false)
            val identity = f.a.copy(environment = "synthetic-second-env")
            f.login(identity)
            val scope = f.task.copy(environmentId = identity.environment)
            val access = f.access(scope)
            val context = f.sourceFor(identity, testId(8), scope)
            f.store.storeSource(access, context, f.proof)
            expectWork(WorkProblem.OperationIdentityConflict) { f.store.enqueue(access, f.capture(context = context)) }
            assertTrue(f.store.operations(access).isEmpty())
            val other = f.store.enqueue(access, f.capture(11, context)).envelope
            assertEquals(original.workspaceId, other.workspaceId)
        }
    }

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
            expectIssue(Issue.UNAUTHENTICATED) { f.sessions.bindWorkSession() }
            val a = f.preparedSource()
            val original = f.store.enqueue(a, f.capture()).envelope
            f.offline.advance(60_001)
            f.sessions.logout(attemptRemote = false)
            assertNull(server.vault.saved)
            f.login(f.b)
            expectIssue(Issue.UNAUTHENTICATED) { f.reopen().operation(a, original.operation.operationId) }
            val bBeforePreflight = f.access(checked = emptyList())
            assertTrue(f.reopen().operations(bBeforePreflight).isEmpty())
            expectWork(WorkProblem.SourceUnavailable) { f.store.enqueue(bBeforePreflight, f.capture(11)) }
            expectWork(WorkProblem.SourceUnavailable) { f.store.storeSource(bBeforePreflight, f.source, f.proof) }
            val b = f.access()
            f.reopen().withSourceForPreflight(bBeforePreflight, original.operation.operationId) {
                assertEquals(original.source, it.source)
                assertEquals(f.proof, it.proof)
            }
            val historicalBatch = f.reopen().prepareBatch(b, testId(27), listOf(original.operation.operationId))
            f.reopen().startAttempt(b, historicalBatch.batchId)
            assertEquals(WorkState.InFlight, f.reopen().batch(b, historicalBatch.batchId)?.state)
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
