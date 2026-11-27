package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.work.*
import java.time.Instant
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.SocketPolicy
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


    @Test fun foregroundUploadUsesExactSealedBytesWithCurrentBearerAndOriginalContributor() = runBlocking<Unit> {
        for (reason in listOf(null, "")) TestServer().use { server ->
            val e = RemovalFixture(server)
            val prepared = e.prepare(reason)
            e.f.work.sessions.logout(attemptRemote = false)
            e.f.work.offline.advance(61_000)
            val b = e.f.work.b
            val token = "synthetic-removal-uploader-b"
            e.f.login(b, token)
            val grant = e.open(b, token)
            val receipt = testItemReceipt(prepared.saved, prepared.batch, b)
            e.queueAttempt(prepared, b)
            server.enqueue(removalUploadResponse(testItemBatchReceipt(prepared.batch, receipt), receipt))

            assertEquals(ItemRemovalUploadResult.Accepted(testItemBatchReceipt(prepared.batch, receipt), false),
                e.coordinator.uploadItemRemovalBatch(grant, prepared.batch.batchId))
            val body = e.consumeAttempt(prepared, token)
            assertEquals(setOf("batchId", "canonicalizationVersion", "expectedTaskLineId",
                "expectedWorkflowVersion", "expectedEnvironmentId", "expectedDraftVersion", "operation", "sourceProof"), body.keys)
            val cmd = prepared.saved.command
            assertEquals(JsonPrimitive(prepared.batch.batchId), body["batchId"])
            assertEquals(JsonPrimitive(1), body["canonicalizationVersion"])
            assertEquals(JsonPrimitive(cmd.task.lineId), body["expectedTaskLineId"])
            assertEquals(JsonPrimitive(cmd.task.environmentId), body["expectedEnvironmentId"])
            assertEquals(JsonPrimitive(cmd.task.workflowVersion), body["expectedWorkflowVersion"])
            assertEquals(JsonPrimitive(cmd.expectedDraftVersion), body["expectedDraftVersion"])
            val op = body.getValue("operation").jsonObject
            assertEquals(setOf("operationId", "canonicalizationVersion", "kind", "taskItemId", "inspectionAttemptId",
                "sourceWorkContextId", "contributorUserIdAssertion", "baseDraftVersion", "originLocalEditVersion",
                "confirmedAbsentOrNotApplicable", "confirmationSource", "reason"), op.keys)
            assertEquals(JsonPrimitive(cmd.operationId), op["operationId"])
            assertEquals(JsonPrimitive(1), op["canonicalizationVersion"])
            assertEquals(JsonPrimitive("RemoveItem"), op["kind"])
            assertEquals(JsonPrimitive(cmd.attempt.taskItemId), op["taskItemId"])
            assertEquals(JsonPrimitive(cmd.attempt.inspectionAttemptId), op["inspectionAttemptId"])
            assertEquals(JsonPrimitive(cmd.contextId), op["sourceWorkContextId"])
            assertEquals(JsonPrimitive(cmd.owner.userId), op["contributorUserIdAssertion"])
            assertEquals(JsonPrimitive(cmd.expectedDraftVersion), op["baseDraftVersion"])
            assertEquals(JsonPrimitive(prepared.saved.localEditVersion), op["originLocalEditVersion"])
            assertEquals(JsonPrimitive(true), op["confirmedAbsentOrNotApplicable"])
            assertEquals(JsonPrimitive("Production"), op["confirmationSource"])
            assertEquals(if (reason == null) JsonNull else JsonPrimitive(reason), op["reason"])
            assertEquals(mapOf("workContextId" to JsonPrimitive(cmd.contextId),
                "proofSecret" to JsonPrimitive(e.f.proof)), body.getValue("sourceProof").jsonObject)
            assertEquals(prepared.saved, e.entry(prepared).removal)
            assertEquals(receipt, e.entry(prepared).serverEvidence)
            assertEquals(WorkState.Accepted, e.entry(prepared).state)
        }
    }

    @Test fun workbenchCannotSendAnObsoleteOrDifferentItemRow() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = RemovalFixture(server)
            val prepared = e.prepare()
            val row = e.coordinator.itemRemovalWorkbench(prepared.grant).rows.single()
            val other = row.attempt.copy(inspectionAttemptId = testId(93))
            expectWork(WorkProblem.ItemRemovalReconciliationRequired) {
                e.coordinator.sendWorkbenchItemRemoval(prepared.grant, row.copy(attempt = other))
            }
            expectWork(WorkProblem.ItemRemovalReconciliationRequired) {
                e.coordinator.sendWorkbenchItemRemoval(prepared.grant, row.copy(batchId = testId(94)))
            }
            assertEquals(WorkState.Queued, e.entry(prepared).state)
            assertEquals(prepared.batch, e.entry(prepared).batch)
            assertNull(server.web.takeRequest(100, TimeUnit.MILLISECONDS))
        }
    }

    @Test fun replayPreservesServerActorAndIndividualProofNeverSettlesTheLocalBatch() = runBlocking<Unit> {
        for (complete in listOf(true, false)) TestServer().use { server ->
            val e = RemovalFixture(server)
            val prepared = e.prepare()
            val historical = e.f.work.b
            val base = testItemReceipt(prepared.saved, prepared.batch, historical)
            val operation = if (complete) base else base.copy(batchId = testId(99))
            val full = if (complete) testItemBatchReceipt(prepared.batch, operation) else null
            e.queueAttempt(prepared)
            server.enqueue(removalUploadResponse(full, operation, replay = true))
            val resolved = e.coordinator.uploadItemRemovalBatch(prepared.grant, prepared.batch.batchId)
            e.consumeAttempt(prepared)
            if (full != null) assertEquals(ItemRemovalUploadResult.Accepted(full, true), resolved)
            else assertEquals(ItemRemovalUploadResult.NeedsBatchReceipt(operation), resolved)
            assertEquals(if (complete) WorkState.Accepted else WorkState.OutcomeUnknown, e.entry(prepared).state)
            assertEquals(operation, e.entry(prepared).serverEvidence)
            assertEquals(prepared.saved, e.entry(prepared).removal)
            if (!complete) {
                server.enqueue("{}", 404)
                val lookup = ReceiptLookup(ReceiptKind.Batch, prepared.batch.batchId)
                assertEquals(ItemRemovalReceiptRecovery.Unresolved(ReceiptUncertainty.NotObserved),
                    e.coordinator.recoverItemRemovalReceipt(prepared.grant, lookup))
                server.request("${e.f.path}/operation-batches/${prepared.batch.batchId}/receipt", "GET", Fixture.ACCESS)
                assertEquals(WorkState.OutcomeUnknown, e.entry(prepared).state)
            }
        }
    }

    @Test fun malformedAndInconsistentPostReceiptsNeverSettleTheAttempt() = runBlocking<Unit> {
        val badOperation: List<(ItemRemovalReceipt) -> ItemRemovalReceipt> = listOf(
            { it.copy(operationId = testId(92)) }, { it.copy(batchId = testId(93)) },
            { it.copy(lineId = testId(94)) }, { it.copy(inspectionAttemptId = testId(95)) },
            { it.copy(contentHash = "A".repeat(64)) }, { it.copy(draftVersion = it.draftVersion + 1) },
            { it.copy(uploadedByUserId = testId(96)) }, { it.copy(uploadedPersonalSessionId = testId(97)) },
            { it.copy(remainingApplicableItemCount = 23, warnsLastApplicableItemRemoval = false) },
            { it.copy(minimumSampleCount = 10) },
        )
        val transforms: List<(ItemRemovalBatchReceipt, ItemRemovalReceipt) -> String> =
            badOperation.map { change -> { full: ItemRemovalBatchReceipt, op: ItemRemovalReceipt ->
                val altered = change(op)
                removalUploadResponse(full.copy(itemOperations = listOf(altered), draftVersion = altered.draftVersion,
                    uploadedByUserId = altered.uploadedByUserId, uploadedPersonalSessionId = altered.uploadedPersonalSessionId), altered)
            } } + listOf(
                { full, op -> removalUploadResponse(full.copy(requestFingerprint = "A".repeat(64)), op) },
                { full, op -> removalUploadResponse(full.copy(receiptVersion = 3), op) },
                { full, op -> removalUploadResponse(full, op.copy(reason = "different")) },
                { full, op -> removalUploadResponse(full, op).replace("\"replayOnly\":false", "\"replayOnly\":false,\"replayOnly\":true") },
                { _, op -> removalUploadResponse(null, op) },
                { full, op -> removalUploadResponse(full, op) + " ".repeat(512 * 1024) },
                { _, _ -> "{" },
            )
        for ((index, transform) in transforms.withIndex()) TestServer().use { server ->
            val e = RemovalFixture(server)
            val p = e.prepare()
            val op = testItemReceipt(p.saved, p.batch, e.f.work.a)
            e.queueAttempt(p)
            server.enqueue(transform(testItemBatchReceipt(p.batch, op), op))
            assertEquals("Invalid receipt $index", ItemRemovalUploadResult.OutcomeUnknown(Issue.RESPONSE),
                e.coordinator.uploadItemRemovalBatch(p.grant, p.batch.batchId))
            e.consumeAttempt(p)
            assertEquals(WorkState.OutcomeUnknown, e.entry(p).state)
            assertNull(e.entry(p).serverEvidence)
        }
    }

    @Test fun onlyValidatedTerminalErrorsChangeTheLedgerToConflictOrQuarantine() = runBlocking<Unit> {
        for (mode in listOf("version", "scope", "revoke", "wrong-revoke", "unknown", "throttle", "server", "redirect",
            "disconnect", "unauthorized", "forbidden")) TestServer().use { server ->
            val e = RemovalFixture(server)
            val p = e.prepare()
            e.queueAttempt(p)
            val operation = p.saved.command
            val revocation = ""","preserveOriginals":true,"quarantineRequired":true,
                "sourceContextIds":["${operation.contextId}"],"operationIds":["${operation.operationId}"]"""
            val expected = when (mode) {
                "version" -> {
                    enqueueRemovalProblem(server, 409, "DraftVersionConflict")
                    ItemRemovalUploadResult.Rejected(WorkConflict.DraftVersionConflict)
                }
                "scope" -> {
                    enqueueRemovalProblem(server, 409, "DraftItemAlreadyRemoved")
                    ItemRemovalUploadResult.Rejected(WorkConflict.TaskScopeConflict)
                }
                "revoke" -> {
                    enqueueRemovalProblem(server, 409, "SourceSecurityRevoked", revocation)
                    ItemRemovalUploadResult.Rejected(WorkConflict.SourceSecurityRevoked)
                }
                "wrong-revoke" -> {
                    enqueueRemovalProblem(server, 409, "SourceSecurityRevoked", revocation.replace(operation.operationId, testId(89)))
                    ItemRemovalUploadResult.OutcomeUnknown(Issue.RESPONSE)
                }
                "unknown" -> {
                    server.enqueue("{}", 409)
                    ItemRemovalUploadResult.OutcomeUnknown(Issue.CONFLICT)
                }
                "throttle" -> {
                    server.enqueue("{}", 429)
                    ItemRemovalUploadResult.OutcomeUnknown(Issue.RATE_LIMITED)
                }
                "server" -> {
                    server.web.enqueue(MockResponse().setResponseCode(503).setHeader("Retry-After", "0"))
                    ItemRemovalUploadResult.OutcomeUnknown(Issue.UNAVAILABLE)
                }
                "redirect" -> {
                    server.web.enqueue(MockResponse().setResponseCode(307).setHeader("Location", "/must-not-follow"))
                    ItemRemovalUploadResult.OutcomeUnknown(Issue.RESPONSE)
                }
                "disconnect" -> {
                    server.web.enqueue(MockResponse().setSocketPolicy(SocketPolicy.DISCONNECT_AFTER_REQUEST))
                    ItemRemovalUploadResult.OutcomeUnknown(Issue.CONNECTION)
                }
                else -> { server.enqueue("{}", if (mode == "unauthorized") 401 else 403); null }
            }
            if (expected == null) expectIssue(if (mode == "unauthorized") Issue.UNAUTHENTICATED else Issue.FORBIDDEN) {
                e.coordinator.uploadItemRemovalBatch(p.grant, p.batch.batchId)
            } else assertEquals(mode, expected, e.coordinator.uploadItemRemovalBatch(p.grant, p.batch.batchId))
            e.consumeAttempt(p)
            assertEquals(when (mode) {
                "version", "scope" -> WorkState.Conflict
                "revoke" -> WorkState.Quarantined
                else -> WorkState.OutcomeUnknown
            }, e.entry(p).state)
            assertEquals(p.saved, e.entry(p).removal)
            assertNull(server.web.takeRequest(100, TimeUnit.MILLISECONDS))
        }
    }

    @Test fun badOrRevokedSourcePreflightCannotAdmitABusinessPost() = runBlocking<Unit> {
        for (revoked in listOf(true, false)) TestServer().use { server ->
            val e = RemovalFixture(server)
            val p = e.prepare()
            e.queue(e.f.work.a, 7, "Applicable")
            if (revoked) enqueueRemovalProblem(server, 409, "SourceSecurityRevoked",
                ""","sourceContextIds":["${p.saved.command.contextId}"]""")
            else server.enqueue(e.f.preflight(p.saved.source.copy(contributorUserId = e.f.work.b.userId)))
            if (revoked) assertEquals(ItemRemovalUploadResult.Rejected(WorkConflict.SourceSecurityRevoked),
                e.coordinator.uploadItemRemovalBatch(p.grant, p.batch.batchId))
            else expectIssue(Issue.RESPONSE) { e.coordinator.uploadItemRemovalBatch(p.grant, p.batch.batchId) }
            e.f.readRequests()
            e.f.preflightRequest(Fixture.ACCESS)
            assertEquals(if (revoked) WorkState.Quarantined else WorkState.Queued, e.entry(p).state)
            assertNull(server.web.takeRequest(100, TimeUnit.MILLISECONDS))
        }
    }

    @Test fun sharedSendLeaseAndAuthorityRacesPreserveTheExactAttempt() = runBlocking<Unit> {
        for (race in listOf("cancel", "logout", "selection", "rotated", "replacement")) TestServer().use { server ->
            val e = RemovalFixture(server)
            val p = e.prepare()
            val other = e.f.coordinator()
            e.queue(e.f.work.a, 7, "Applicable")
            val otherGrant = other.open(e.f.selected())
            e.f.readRequests()
            e.queueAttempt(p)
            if (race == "rotated") e.f.work.stageRotation(server.clock.instant().plusSeconds(300))
            val replacement = e.f.work.b
            val replacementBearer = "synthetic-new-removal-session"
            if (race == "replacement") server.admit(Fixture.auth(user = replacement.userId,
                family = replacement.familyId, access = replacementBearer), Fixture.receipt(replacement.userId, replacement.familyId))
            val operation = testItemReceipt(p.saved, p.batch, e.f.work.a)
            val finalResponse = MockResponse().setHeader("Content-Type", "application/json")
                .setBody(removalUploadResponse(testItemBatchReceipt(p.batch, operation), operation))
            withHeldResponse(server, e.uploadPath, finalResponse) { release ->
                val pending = async(Dispatchers.IO) { runCatching { e.coordinator.uploadItemRemovalBatch(p.grant, p.batch.batchId) } }
                e.consumeAttempt(p)
                // A separate coordinator cannot enter the sample send path while removal holds the lease.
                expectWork(WorkProblem.InvalidState) { other.uploadBatch(otherGrant, testId(77)) }
                assertEquals(WorkState.InFlight, e.entry(p).state)
                when (race) {
                    "cancel" -> pending.cancelAndJoin()
                    "logout" -> e.f.work.sessions.logout(attemptRemote = false)
                    "selection" -> e.coordinator.clearSelection()
                    "rotated" -> {
                        e.f.work.offline.advance(300_000)
                        e.f.work.sessions.read { _, _ -> Unit }
                        server.request("/api/auth/refresh")
                        server.request("/api/line-patrol/access/verify", bearer = "synthetic-access-rotated")
                    }
                    "replacement" -> {
                        e.f.work.sessions.logout(attemptRemote = false)
                        e.f.work.sessions.login("synthetic-replacement", "test-only")
                        server.request("/api/auth/login")
                        server.request("/api/line-patrol/access/verify", bearer = replacementBearer)
                    }
                }
                release()
                if (race != "cancel") assertTrue(pending.await().isFailure)
            }
            assertEquals(WorkState.OutcomeUnknown, e.entry(p).state)
            assertEquals(p.saved, e.entry(p).removal)
            assertNull(e.entry(p).serverEvidence)
        }
    }


    @Test fun missingOriginalProofOrStickyRevocationStopsBeforePreflight() = runBlocking<Unit> {
        for (revoked in listOf(true, false)) TestServer().use { server ->
            val e = RemovalFixture(server)
            val p = e.prepare()
            e.f.work.rewriteFixture {
                if (revoked) rememberSourceRevoked(p.saved.source.contextId)
                else save(StoredWorkSource(p.saved.source, ""))
            }
            e.queue(e.f.work.a, 7, "Applicable")
            expectWork(WorkProblem.SourceUnavailable) { e.coordinator.uploadItemRemovalBatch(p.grant, p.batch.batchId) }
            e.f.readRequests()
            assertEquals(p.saved, e.entry(p).removal)
            assertNull(server.web.takeRequest(100, TimeUnit.MILLISECONDS))
        }
    }

    @Test fun lastNetworkHopFencesRotationAndSelectionAfterDurableRemovalAdmission() = runBlocking<Unit> {
        for (rotate in listOf(true, false)) {
            val entered = CountDownLatch(1)
            val release = CountDownLatch(1)
            TestServer { request ->
                if (request.url.encodedPath.endsWith("/item-removal-batches")) {
                    entered.countDown()
                    check(release.await(10, TimeUnit.SECONDS))
                }
            }.use { server ->
                val e = RemovalFixture(server)
                val p = e.prepare()
                e.queueAttempt(p)
                if (rotate) e.f.work.stageRotation(server.clock.instant().plusSeconds(300))
                val pending = async(Dispatchers.IO) { runCatching { e.coordinator.uploadItemRemovalBatch(p.grant, p.batch.batchId) } }
                try {
                    assertTrue(entered.await(5, TimeUnit.SECONDS))
                    e.f.readRequests()
                    e.f.preflightRequest(Fixture.ACCESS)
                    assertEquals(WorkState.InFlight, e.entry(p).state)
                    if (rotate) {
                        e.f.work.offline.advance(300_000)
                        e.f.work.sessions.read { _, _ -> Unit }
                        server.request("/api/auth/refresh")
                        server.request("/api/line-patrol/access/verify", bearer = "synthetic-access-rotated")
                    } else e.coordinator.clearSelection()
                } finally {
                    release.countDown()
                }
                assertTrue(pending.await().isFailure)
                assertEquals(WorkState.OutcomeUnknown, e.entry(p).state)
                assertNull(server.web.takeRequest(100, TimeUnit.MILLISECONDS))
            }
        }
    }


    @Test fun selectingAnotherTaskOrLineDuringPreflightLeavesThePreparedRemovalQueued() = runBlocking<Unit> {
        for (changeLine in listOf(false, true)) TestServer().use { server ->
            val e = RemovalFixture(server)
            val p = e.prepare()
            e.queue(e.f.work.a, 7, "Applicable")
            val alternate = e.f.task.copy(taskId = testId(98),
                lineId = if (changeLine) Fixture.SECOND_LINE else e.f.task.lineId)
            e.f.enqueueRead(body = e.f.draft(alternate), scope = alternate)
            val response = MockResponse().setHeader("Content-Type", "application/json")
                .setBody(e.f.preflight(p.saved.source))
            withHeldResponse(server, "${e.f.path}/work-contexts/validate-sources", response) { release ->
                val upload = async(Dispatchers.IO) { runCatching {
                    e.coordinator.uploadItemRemovalBatch(p.grant, p.batch.batchId)
                } }
                e.f.readRequests()
                e.f.preflightRequest(Fixture.ACCESS)
                // open changes selection before acquiring the coordinator mutex held by the upload.
                val selected = async(start = CoroutineStart.UNDISPATCHED) {
                    e.coordinator.open(e.f.selected(alternate))
                }
                release()
                assertEquals(WorkProblem.StaleGrant, (upload.await().exceptionOrNull() as? WorkFailure)?.problem)
                assertEquals(alternate, selected.await().draft.task)
                e.f.readRequests(scope = alternate)
            }
            assertEquals(WorkState.Queued, e.entry(p).state)
            assertNull(server.web.takeRequest(100, TimeUnit.MILLISECONDS))
        }
    }

    private data class PreparedRemoval(val grant: OnlineWorkCoordinator.Grant, val saved: ItemRemovalEnvelope,
                                       val batch: ItemRemovalBatch)

    private class RemovalFixture(val server: TestServer) {
        val f = OnlineWorkFixture(server, canContribute = true)
        val coordinator = f.coordinator()
        val local = coordinator.offlineItemRemovals


        val uploadPath = "${f.path}/item-removal-batches"

        suspend fun prepare(reason: String? = null): PreparedRemoval {
            val grant = arm()
            val saved = local.save(command().copy(reason = reason))
            val batch = local.prepareBatch(f.task, saved.command.owner, testId(82), saved.reference())
            return PreparedRemoval(grant, saved, batch)
        }

        fun queueAttempt(removal: PreparedRemoval, actor: SessionIdentity = f.work.a) {
            queue(actor, 7, "Applicable")
            server.enqueue(f.preflight(removal.saved.source))
        }

        fun consumeAttempt(removal: PreparedRemoval, token: String = Fixture.ACCESS): JsonObject {
            f.readRequests(token)
            val source = f.preflightRequest(token).getValue("sources").jsonArray.single().jsonObject
            assertEquals(JsonPrimitive(removal.saved.command.contextId), source["workContextId"])
            assertEquals(JsonPrimitive(removal.saved.command.owner.userId), source["contributorUserIdAssertion"])
            return Json.parseToJsonElement(server.request(uploadPath, "POST", token).body.readUtf8()).jsonObject
        }

        suspend fun entry(removal: PreparedRemoval): ItemRemovalEntry {
            lateinit var stored: ItemRemovalEntry
            f.work.rewriteFixture { stored = requireNotNull(itemRemoval(removal.saved.command.operationId)) }
            return stored
        }

        fun queue(owner: SessionIdentity, version: Long, disposition: String, attempt: WorkAttempt = f.attempt,
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


private fun removalUploadResponse(batch: ItemRemovalBatchReceipt?, operation: ItemRemovalReceipt, replay: Boolean = false) =
    """{"batchReceipt":${Json.encodeToString(batch)},"operation":${Json.encodeToString(operation)},
        "replayOnly":$replay,"canExecute":false,"acceptsFormalInspection":false}"""

private fun enqueueRemovalProblem(server: TestServer, status: Int, code: String, extra: String = "") {
    server.web.enqueue(MockResponse().setResponseCode(status).setHeader("Content-Type", "application/problem+json")
        .setBody("""{"status":$status,"title":"$code","code":"$code"$extra}"""))
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
