package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.work.*
import java.time.Instant
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import org.junit.Assert.*
import org.junit.Test

class OfflineItemRemovalCoordinatorTest {
    @Test fun canonicalV1KeepsExactScopeAndConfirmationAndUsesSeparateBatchDomain() {
        val owner = SessionIdentity(testId(1), testId(2), "test-env")
        val task = WorkTask(owner.environment, testId(3), testId(4), 2)
        val command = ItemRemovalCommand(task, WorkAttempt(testId(5), testId(6)), testId(7), owner, testId(8),
            9, true, RemovalConfirmation.Equipment, "Absent <設備> 😀")
        val bytes = ItemRemovalCanonical.operation(command, 10)
        assertEquals(
            """{"domain":"item-removal","v":1,"environment":"test-env","task":"${testId(3)}","line":"${testId(4)}","workflow":2,"operation":"${testId(7)}","kind":"RemoveItem","item":"${testId(5)}","attempt":"${testId(6)}","source":"${testId(8)}","contributorAssertion":"${testId(1)}","base":9,"local":10,"confirmedAbsentOrNotApplicable":true,"confirmationSource":"Equipment","reason":"Absent \u003C\u8A2D\u5099\u003E \uD83D\uDE00"}""",
            bytes,
        )
        val source = WorkSource(task, command.contextId, owner.userId, owner.familyId, Fixture.NOW,
            "2026-11-19T14:01:00Z", Fixture.FAMILY_EXPIRY, 1)
        val envelope = ItemRemovalEnvelope(testId(11), command, source, 10, 2, 1, bytes,
            workHash(bytes), OfflineCaptureEvidence(source.issuedAtUtc, source.captureUntilUtc))
        val batch = ItemRemovalCanonical.batch(envelope, testId(12))
        // Independently emitted by the checked-out backend DraftCanonical.cs.
        assertEquals("3DFADD7AC7E4A9FFC04B9EFACE971CB3C87701F3F72BE23499FF33905232A9C6", envelope.contentHash)
        assertEquals("9FA15D163EFF40BAA6C5EA4E0819E95292AC76B63B205CD1121F6F79EFD74644", batch.fingerprint)
        assertEquals(envelope.reference(), batch.operation)
        assertTrue(ItemRemovalCanonical.operation(command.copy(reason = null), 10).endsWith("\"reason\":null}"))
        assertTrue(ItemRemovalCanonical.operation(command.copy(reason = ""), 10).endsWith("\"reason\":\"\"}"))
        assertFalse(bytes.contains("clientSample"))
        assertFalse(bytes.contains(owner.familyId))
    }

    @Test fun itemAndSampleRemovalRemainDistinctAndMeasuredHistoryStaysIntact() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = RemovalFixture(server)
            e.arm()
            val a = e.f.work.a
            val calls = server.web.requestCount
            val sample = e.coordinator.offlineSamples
            val capture = sample.newCapture(e.f.attempt, SampleInput.Quantitative("2.5"))
            val measured = sample.save(e.f.task, capture)
            val removedSample = sample.save(e.f.task, OfflineSampleEdit.Remove(capture.key, measured.envelope.reference()))
            assertEquals(SampleKind.RemoveSample, removedSample.envelope.operation.kind)
            assertEquals(ItemDisposition.Applicable, e.local.draft(e.f.task, a).items.single().server)
            assertFalse(e.local.draft(e.f.task, a).provisionallyAllItemsRemoved)

            val command = e.command()
            val saved = e.local.save(command)
            assertEquals(saved, e.local.save(command))
            expectWork(WorkProblem.OperationIdentityConflict) { e.local.save(command.copy(reason = "changed")) }
            expectWork(WorkProblem.OperationIdentityConflict) {
                sample.save(e.f.task, capture.copy(operationId = command.operationId, key = capture.key.copy(clientSampleId = testId(60))))
            }
            val sealed = e.local.prepareBatch(e.f.task, a, testId(70), saved.reference())
            assertEquals(sealed, e.local.prepareBatch(e.f.task, a, testId(70), saved.reference()))
            expectWork(WorkProblem.BatchIdentityConflict) {
                e.local.prepareBatch(e.f.task, a, testId(71), saved.reference())
            }
            val draft = e.local.draft(e.f.task, a)
            assertEquals(7L, draft.serverDraftVersion)
            assertEquals(3L, draft.localEditVersion)
            assertEquals(listOf(e.f.attempt), draft.items.map { it.attempt })
            assertEquals(2, draft.items.single().serverSampleRequirement)
            assertNull(draft.items.single().localSampleRequirement)
            assertEquals(saved.reference(), draft.items.single().provisionalRemoval)
            assertTrue(draft.provisionallyAllItemsRemoved)
            assertFalse(draft.serverAllItemsRemoved)
            assertEquals(WorkState.Queued, draft.history.single().state)
            assertEquals(a.userId, draft.history.single().removal.source.contributorUserId)
            assertEquals("1.23", draft.samples.single().value.inspectionValue)
            assertEquals(a.userId, draft.samples.single().valueContributorId)
            assertEquals(listOf(SampleKind.CaptureSample, SampleKind.RemoveSample),
                sample.draft(e.f.task, a).history.map { it.operation.envelope.operation.kind })
            assertEquals(calls, server.web.requestCount)
        }
    }

    @Test fun conflictingConcurrentCommandsAndInvalidScopeCannotReplaceTheOriginal() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = RemovalFixture(server)
            val grant = e.arm()
            val command = e.command()
            expectWork(WorkProblem.ScopeMismatch) { e.local.save(command.copy(attempt = WorkAttempt(e.f.attempt.taskItemId, testId(77)))) }
            expectWork(WorkProblem.SourceUnavailable) { e.local.save(command.copy(contextId = testId(78))) }
            expectWork(WorkProblem.SourceUnavailable) { e.local.save(command.copy(task = e.f.task.copy(lineId = testId(79)))) }
            expectWork(WorkProblem.SourceUnavailable) { e.local.save(command.copy(task = e.f.task.copy(taskId = testId(76)))) }
            expectWork(WorkProblem.ScopeMismatch) { e.local.save(command.copy(task = e.f.task.copy(environmentId = "other"))) }
            expectWork(WorkProblem.SourceUnavailable) {
                e.local.save(command.copy(owner = command.owner.copy(familyId = testId(74))))
            }
            expectWork(WorkProblem.ItemRemovalInputMismatch) { e.local.save(command.copy(confirmedAbsentOrNotApplicable = false)) }
            expectWork(WorkProblem.ItemRemovalInputMismatch) { e.local.save(command.copy(reason = "x".repeat(501))) }
            expectWork(WorkProblem.ItemRemovalInputMismatch) { e.local.save(command.copy(reason = "line\nfeed")) }

            val outcomes = coroutineScope {
                listOf(command, e.command()).map { fresh -> async { runCatching { e.local.save(fresh) } } }.awaitAll()
            }
            val saved = outcomes.single { it.isSuccess }.getOrThrow()
            assertEquals(WorkProblem.ItemDispositionConflict, (outcomes.single { it.isFailure }.exceptionOrNull() as WorkFailure).problem)
            assertEquals(saved, e.local.save(saved.command))
            e.f.work.store.recordSourceRevoked(grant.access, saved.source.contextId)
            val history = e.local.draft(e.f.task, command.owner)
            assertEquals(WorkState.Quarantined, history.history.single().state)
            assertNull(history.items.single().provisionalRemoval)
            expectWork(WorkProblem.SourceUnavailable) { e.local.prepareBatch(e.f.task, command.owner, testId(70), saved.reference()) }
            expectWork(WorkProblem.SourceUnavailable) { e.command() }
            assertEquals(saved, e.local.save(saved.command))
        }
    }

    @Test fun observedVersionAndExistingDispositionFenceOldCommandsAndBatchPreparation() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = RemovalFixture(server)
            e.arm()
            val cmd = e.command()
            val saved = e.local.save(cmd)
            val neverSaved = e.command()
            e.open(version = 8, disposition = "UnableToMeasureNG")
            expectWork(WorkProblem.StaleGrant) { e.local.save(neverSaved) }
            expectWork(WorkProblem.StaleGrant) { e.local.prepareBatch(e.f.task, cmd.owner, testId(70), saved.reference()) }
            val draft = e.local.draft(e.f.task, cmd.owner)
            assertEquals(ItemDisposition.UnableToMeasureNG, draft.items.single().server)
            assertEquals(WorkState.Conflict, draft.history.single().state)
            assertNull(draft.items.single().provisionalRemoval)
            // A non-editable basis cannot be rearmed just because an unsupported current item is shown.
            expectWork(WorkProblem.ScopeMismatch) { e.command() }
            assertEquals(saved, e.local.save(cmd))
        }
    }

    @Test fun olderProtectedProfileStillSupportsSamplesAndRequiresTypedItemObservation() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = RemovalFixture(server)
            e.arm()
            val owner = e.f.work.a
            e.f.work.rewriteFixture {
                val current = requireNotNull(offlineProfile(owner.userId))
                val legacy = current.basis.copy(attempts = current.basis.attempts.map {
                    it.copy(serverDisposition = null, serverRemoval = null)
                })
                saveOfflineProfile(current.copy(basis = legacy))
            }
            assertEquals(1, e.coordinator.offlineSamples.draft(e.f.task, owner).progress.size)
            expectWork(WorkProblem.ScopeMismatch) { e.command() }
            e.open()
            assertEquals(7L, e.local.save(e.command()).command.expectedDraftVersion)
        }
    }

    @Test fun restartPreservesSavedCommandAndProtectsBothClockDeadlines() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = RemovalFixture(server)
            e.arm()
            val saved = e.local.save(e.command())
            val fresh = e.command()
            e.f.work.restartOffline()
            val local = e.f.coordinator().offlineItemRemovals
            e.f.work.offline.rebootAt(4_000)
            assertEquals(saved, local.draft(e.f.task, saved.command.owner).history.single().removal)
            e.f.work.offline.advance(56_000)
            expectWork(WorkProblem.CaptureWindowClosed) { local.save(fresh) }
            assertEquals(saved, local.save(saved.command))
            e.f.work.offline.advance(72L * 3600 * 1000)
            expectIssue(Issue.OFFLINE_EXPIRED) { local.save(saved.command) }
            e.f.work.rewriteFixture {
                assertEquals(saved.contentHash, itemRemoval(saved.command.operationId)?.removal?.contentHash)
            }
        }
        TestServer().use { server ->
            val e = RemovalFixture(server)
            e.arm()
            val command = e.command()
            e.f.work.offline.advance(5_000)
            e.local.save(command)
            e.f.work.offline.rollbackWallClock(120_000)
            expectIssue(Issue.OFFLINE_CLOCK) { e.local.draft(e.f.task, command.owner) }
        }
    }

    @Test fun authenticatedRemovedReceiptKeepsSamplesAndSurvivesAChangedCurrentAttemptAsHistory() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = RemovalFixture(server)
            e.arm()
            val fresh = e.command()
            val saved = e.local.save(fresh)
            val batch = e.local.prepareBatch(e.f.task, fresh.owner, testId(85), saved.reference())
            val receipt = testItemReceipt(saved, batch, fresh.owner)
            e.open(version = 8, disposition = "Removed", removal = receipt)
            val observed = e.local.draft(e.f.task, fresh.owner)
            assertTrue(observed.serverAllItemsRemoved)
            assertFalse(observed.provisionallyAllItemsRemoved)
            assertNull(observed.items.single().serverSampleRequirement)
            assertEquals(receipt, observed.items.single().serverRemoval)
            assertEquals("1.23", observed.samples.single().value.inspectionValue)
            assertFalse(observed.samples.single().current)
            assertEquals(receipt, observed.history.single().receipt)
            assertEquals(WorkState.Queued, observed.history.single().state)

            val newAttempt = e.f.attempt.copy(inspectionAttemptId = testId(86))
            e.open(version = 9, attempt = newAttempt)
            val later = e.local.draft(e.f.task, fresh.owner)
            assertEquals(newAttempt, later.items.single().attempt)
            assertFalse(later.history.single().currentAttempt)
            assertEquals(listOf(receipt), later.historicalServerRemovals)
            assertEquals(e.f.attempt, later.samples.single().key.attempt)
            assertFalse(later.samples.single().current)
            assertEquals(fresh.reason, later.historicalServerRemovals.single().reason)
        }
    }

    @Test fun unknownStateSurvives404MismatchedReceiptAndSingleOperationEvidence() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = RemovalFixture(server)
            val initial = e.arm()
            val command = e.command()
            val samples = e.coordinator.offlineSamples
            val sample = samples.save(e.f.task, samples.newCapture(e.f.attempt, SampleInput.Quantitative("2.5")))
            val sampleBatch = samples.prepareBatch(e.f.task, testId(81), listOf(sample.envelope.operation.operationId))
            // An older sample attempt must settle before the removal can seal.
            val source = requireNotNull(initial.source)
            val checked = e.f.work.access(checked = listOf(source))
            e.f.work.store.startAttempt(checked, sampleBatch.batchId)
            val saved = e.local.save(command)
            expectWork(WorkProblem.InvalidState) {
                e.local.prepareBatch(e.f.task, command.owner, testId(82), saved.reference())
            }
            e.f.work.store.recordRejection(checked, sampleBatch.batchId, WorkConflict.DraftVersionConflict)
            val batch = e.local.prepareBatch(e.f.task, command.owner, testId(82), saved.reference())
            e.f.work.store.startItemRemovalAttempt(checked, initial.draft.sampleBasis, batch.batchId)
            assertEquals(WorkState.InFlight, e.local.draft(e.f.task, command.owner).history.single().state)
            expectWork(WorkProblem.InvalidState) { e.f.work.store.inspectUpload(checked, sampleBatch.batchId) }
            e.f.work.store.markItemRemovalOutcomeUnknown(checked, batch.batchId)

            e.f.work.sessions.logout(attemptRemote = false)
            val b = e.f.work.b
            val bearer = "synthetic-review-operator-b"
            e.f.login(b, bearer)
            val bGrant = e.open(b, bearer)
            val batchLookup = ReceiptLookup(ReceiptKind.Batch, batch.batchId)
            val opLookup = ReceiptLookup(ReceiptKind.Operation, command.operationId)
            val batchPath = "${e.f.path}/operation-batches/${batch.batchId}/receipt"
            val opPath = "${e.f.path}/operations/${command.operationId}/receipt"
            server.enqueue("{}", 404)
            assertEquals(ItemRemovalReceiptRecovery.Unresolved(ReceiptUncertainty.NotObserved),
                e.coordinator.recoverItemRemovalReceipt(bGrant, batchLookup))
            server.request(batchPath, "GET", bearer)
            val receipt = testItemReceipt(saved, batch, b)
            server.enqueue(Json.encodeToString(testItemBatchReceipt(batch, receipt.copy(reason = "altered"))))
            assertEquals(ItemRemovalReceiptRecovery.Conflict(ReceiptConflictReason.ResponseMismatch),
                e.coordinator.recoverItemRemovalReceipt(bGrant, batchLookup))
            server.request(batchPath, "GET", bearer)
            server.enqueue(Json.encodeToString(receipt))
            assertEquals(ItemRemovalReceiptRecovery.OperationObserved(receipt),
                e.coordinator.recoverItemRemovalReceipt(bGrant, opLookup))
            server.request(opPath, "GET", bearer)
            e.f.work.rewriteFixture { assertEquals(WorkState.OutcomeUnknown, itemRemoval(command.operationId)?.state) }
            server.enqueue(Json.encodeToString(testItemBatchReceipt(batch, receipt)))
            assertEquals(ItemRemovalReceiptRecovery.BatchConfirmed(testItemBatchReceipt(batch, receipt)),
                e.coordinator.recoverItemRemovalReceipt(bGrant, batchLookup))
            server.request(batchPath, "GET", bearer)
            e.f.work.rewriteFixture {
                assertEquals(WorkState.Accepted, itemRemoval(command.operationId)?.state)
                assertEquals(e.f.work.a.userId, itemRemoval(command.operationId)?.removal?.source?.contributorUserId)
            }
        }
    }

    private class RemovalFixture(val server: TestServer) {
        val f = OnlineWorkFixture(server, canContribute = true)
        val coordinator = f.coordinator()
        val local = coordinator.offlineItemRemovals

        private fun queue(owner: SessionIdentity, version: Long, disposition: String, attempt: WorkAttempt = f.attempt,
                          removal: ItemRemovalReceipt? = null) {
            val pairs = f.pair.replace(f.attempt.inspectionAttemptId, attempt.inspectionAttemptId)
            val pair = Json.parseToJsonElement(pairs).jsonArray.single().jsonObject.toString().dropLast(1)
            val item = pair + ""","disposition":"$disposition","removal":${Json.encodeToString(removal)}}"""
            val samples = """
                [{"sampleId":"${testId(51)}","taskItemId":"${f.attempt.taskItemId}","inspectionAttemptId":"${f.attempt.inspectionAttemptId}",
                  "clientSampleId":"${testId(52)}","sequenceNo":1,"value":{"inspectionType":"Quantitative","inspectionValue":1.23,
                  "judgment":"OK","barcode":null},"isRemoved":false,"valueOperationId":"${testId(53)}",
                  "valueContributorUserId":"${f.work.a.userId}","lastOperationId":"${testId(53)}",
                  "isCurrentApplicable":${disposition == "Applicable" && attempt == f.attempt}}]
            """.trimIndent()
            val draft = f.draft(version = version, pairs = pairs, samples = samples)
                .replace("\"allItemsRemoved\":false", "\"allItemsRemoved\":${disposition == "Removed"}")
            f.enqueueRead(owner, draft.dropLast(1) + ""","items":[$item]}""")
        }

        suspend fun open(owner: SessionIdentity = f.work.a, bearer: String = Fixture.ACCESS, version: Long = 7,
                         disposition: String = "Applicable", attempt: WorkAttempt = f.attempt,
                         removal: ItemRemovalReceipt? = null): OnlineWorkCoordinator.Grant {
            queue(owner, version, disposition, attempt, removal)
            return coordinator.open(f.selected(), owner).also { f.readRequests(bearer) }
        }

        suspend fun arm(): OnlineWorkCoordinator.Grant {
            f.login()
            val start = open()
            server.enqueue(f.issued(), 201)
            val grant = coordinator.issueOrReuse(start)
            f.issueRequest()
            queue(f.work.a, 7, "Applicable")
            coordinator.armOfflineSamples(grant)
            f.readRequests()
            return grant
        }

        suspend fun command() = local.newRemoval(f.task, f.work.a, f.attempt, true, RemovalConfirmation.Production)
    }
}

private fun testItemReceipt(saved: ItemRemovalEnvelope, batch: ItemRemovalBatch, uploader: SessionIdentity): ItemRemovalReceipt {
    val cmd = saved.command
    val task = cmd.task
    return ItemRemovalReceipt(task.taskId, cmd.operationId, batch.batchId, "RemoveItem", 1, saved.contentHash,
        cmd.attempt.taskItemId, cmd.attempt.inspectionAttemptId, cmd.contextId, cmd.owner.userId, uploader.userId,
        uploader.familyId, task.environmentId, task.lineId, task.workflowVersion, Fixture.NOW, batch.expectedDraftVersion + 1,
        ItemDisposition.Removed, true, cmd.confirmation, cmd.reason, saved.minimumSampleCount, 0, true)
}

private fun testItemBatchReceipt(batch: ItemRemovalBatch, receipt: ItemRemovalReceipt) = ItemRemovalBatchReceipt(
    receipt.taskId, batch.batchId, batch.fingerprint, receipt.uploadedByUserId, receipt.uploadedPersonalSessionId,
    receipt.acceptedAtUtc, batch.expectedDraftVersion, receipt.draftVersion, emptyList(), listOf(receipt), 2,
)
