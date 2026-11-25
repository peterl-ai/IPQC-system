package com.jaxpower.ipqc.patrol

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.IDENTITY
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.LINE
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.NOW
import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.offline.CacheRows
import com.jaxpower.ipqc.patrol.offline.RoomCacheRows
import com.jaxpower.ipqc.patrol.offline.TaskCacheDatabase
import com.jaxpower.ipqc.patrol.work.*
import java.io.File
import java.io.IOException
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonObject
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class OfflineItemRemovalStorageTest {
    @Test fun realRoomAndKeystorePreserveAThroughOfflineRestartAndAuthorizedBHandoff() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val a = fixture.workServices()
            val grant = activate(a, IDENTITY)
            val scope = fixture.scope()
            val attempt = fixture.attempt(scope.taskId, 1)
            val local = a.online.offlineItemRemovals
            val intent = local.newRemoval(scope, IDENTITY, attempt, true, RemovalConfirmation.Production, SECRET_REASON)
            val removal = local.save(intent)
            val calls = fixture.wire.reads.get()
            val original = requireNotNull(grant.source)
            assertEquals(original, removal.source)
            assertEquals("1.25", local.draft(scope, IDENTITY).samples.single().value.inspectionValue)
            assertEquals(calls, fixture.wire.reads.get())

            fixture.device.database.close()
            val db = TaskCacheDatabase.open(fixture.device.context, fixture.device.databaseFile)
            try {
                val restarted = fixture.workServices(cache = fixture.device.openCache(db), rows = RoomCacheRows(db))
                val reopened = restarted.online.offlineItemRemovals
                assertEquals(removal, reopened.draft(scope, IDENTITY).history.single().removal)
                assertEquals(removal, reopened.save(intent))
                assertEquals(calls, fixture.wire.reads.get())
                restarted.sessions.logout(attemptRemote = false)
                assertNull(fixture.device.vault.read())
                assertFailure(Issue.UNAUTHENTICATED) { reopened.draft(scope, IDENTITY) }

                fixture.device.api.loginSubject = fixture.b
                val b = activate(restarted, fixture.b)
                assertTrue(reopened.draft(scope, fixture.b).history.isEmpty())
                assertWorkFailure(WorkProblem.SourceUnavailable) { reopened.save(intent) }
                val competing = reopened.newRemoval(scope, fixture.b, fixture.attempt(scope.taskId, 0),
                    true, RemovalConfirmation.Equipment)
                assertWorkFailure(WorkProblem.ItemRemovalReconciliationRequired) { reopened.save(competing) }
                assertTrue(reopened.draft(scope, fixture.b).items.none { it.provisionalRemoval != null })

                assertWorkFailure(WorkProblem.SourceUnavailable) {
                    restarted.store.prepareItemRemovalForReconciliation(b.access, b.draft.sampleBasis, benchId(81), removal.reference())
                }
                fixture.wire.explicitRemovalSource = original
                val authorized = restarted.online.preflight(b, intent.operationId)
                assertEquals(1, fixture.wire.removalPreflights.get())
                val batch = restarted.online.prepareItemRemovalForReconciliation(authorized, benchId(81), removal.reference())
                restarted.store.startItemRemovalAttempt(authorized.access, authorized.draft.sampleBasis, batch.batchId)
                restarted.store.markItemRemovalOutcomeUnknown(authorized.access, batch.batchId)
                val bAccess = fixture.b
                val outcome = ItemRemovalReceipt(scope.taskId, intent.operationId, batch.batchId, "RemoveItem", 1,
                    removal.contentHash, attempt.taskItemId, attempt.inspectionAttemptId, original.contextId,
                    IDENTITY.userId, bAccess.userId, bAccess.familyId, scope.environmentId, scope.lineId,
                    scope.workflowVersion, NOW, 8, ItemDisposition.Removed, true, intent.confirmation, intent.reason,
                    removal.minimumSampleCount, 1, false)
                val receipt = ItemRemovalBatchReceipt(scope.taskId, batch.batchId, batch.fingerprint, bAccess.userId,
                    bAccess.familyId, NOW, 7, 8, emptyList(), listOf(outcome), 2)
                fixture.wire.explicitRemovalReceipt = receipt
                assertEquals(ItemRemovalReceiptRecovery.BatchConfirmed(receipt), restarted.online.recoverItemRemovalReceipt(
                    authorized, ReceiptLookup(ReceiptKind.Batch, batch.batchId)))
                assertEquals(1, fixture.wire.removalReceiptReads.get())
                val history = reopened.draft(scope, bAccess).history
                val accepted = history.single { it.state == WorkState.Accepted }
                assertEquals(removal, accepted.removal)
                assertEquals(IDENTITY.userId, accepted.receipt?.contributorUserId)
                assertEquals(bAccess.userId, accepted.receipt?.uploadedByUserId)
                assertWorkFailure(WorkProblem.StaleGrant) { reopened.save(competing) }
                fixture.wire.observedRemoval = outcome
                restarted.online.open(fixture.task(), bAccess)
                assertWorkFailure(WorkProblem.StaleGrant) { reopened.save(competing) }
                val newCommand = reopened.newRemoval(scope, bAccess, competing.attempt, true, RemovalConfirmation.Equipment)
                assertWorkFailure(WorkProblem.OperationIdentityConflict) {
                    reopened.save(newCommand.copy(operationId = intent.operationId))
                }
                val next = reopened.save(newCommand)
                assertEquals(8L, next.command.expectedDraftVersion)
                assertEquals(bAccess.userId, next.source.contributorUserId)
                assertEquals(bAccess.familyId, next.source.originPersonalSessionId)
                assertNotEquals(original.contextId, next.source.contextId)
                assertEquals(0, fixture.wire.sends.get())
                assertEncrypted(fixture.device.databaseFile)
            } finally {
                db.close()
            }
        }
    }

    @Test fun roomRollbackAndLostAcknowledgementKeepOneCommandAndBatchAcrossReopen() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val faults = RemovalFaultRows(fixture.device.rows)
            val a = fixture.workServices(rows = faults)
            activate(a, IDENTITY)
            val task = fixture.scope()
            val local = a.online.offlineItemRemovals
            val command = local.newRemoval(task, IDENTITY, fixture.attempt(task.taskId, 0),
                true, RemovalConfirmation.Equipment, SECRET_REASON)
            val count = fixture.device.rows.count()
            faults.rollback.set(true)
            assertWorkFailure(WorkProblem.StorageUnavailable) { local.save(command) }
            assertEquals(count, fixture.device.rows.count())
            assertTrue(local.draft(task, IDENTITY).history.isEmpty())
            faults.dropAck.set(true)
            assertWorkFailure(WorkProblem.StorageUnavailable) { local.save(command) }
            assertEquals(1L, local.draft(task, IDENTITY).localEditVersion)
            val saved = local.save(command)
            val batchId = benchId(83)
            faults.rollback.set(true)
            assertWorkFailure(WorkProblem.StorageUnavailable) { local.prepareBatch(task, IDENTITY, batchId, saved.reference()) }
            assertNull(local.draft(task, IDENTITY).history.single().batch)
            faults.dropAck.set(true)
            assertWorkFailure(WorkProblem.StorageUnavailable) { local.prepareBatch(task, IDENTITY, batchId, saved.reference()) }
            fixture.device.database.close()
            val db = TaskCacheDatabase.open(fixture.device.context, fixture.device.databaseFile)
            try {
                val fresh = fixture.workServices(cache = fixture.device.openCache(db), rows = RoomCacheRows(db)).online.offlineItemRemovals
                assertEquals(saved, fresh.save(command))
                val sealed = fresh.prepareBatch(task, IDENTITY, batchId, saved.reference())
                assertEquals(sealed, fresh.draft(task, IDENTITY).history.single().batch)
                assertEquals(1L, fresh.draft(task, IDENTITY).localEditVersion)
                assertEquals(0, fixture.wire.sends.get())
                assertEncrypted(fixture.device.databaseFile)
            } finally {
                db.close()
            }
        }
    }

    @Test fun provisionalRemovalFencesAllSampleEditsAndPresealedUploadAtEveryUnresolvedState() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.workServices()
            val grant = activate(app, IDENTITY)
            val scope = fixture.scope()
            val target = fixture.attempt(scope.taskId, 1)
            val other = fixture.attempt(scope.taskId, 0)
            val samples = app.online.offlineSamples
            val removals = app.online.offlineItemRemovals
            val input = SampleInput.Quantitative("1.50")
            val capture = samples.newCapture(target, input)
            val measured = samples.save(scope, capture)
            val opId = measured.envelope.operation.operationId
            val extra = samples.save(scope, samples.newCapture(target, input)).envelope.operation.operationId
            val sealed = samples.prepareBatch(scope, benchId(84), listOf(opId))
            val source = requireNotNull(grant.source)
            val access = WorkAccess(grant.access.session, scope, 7, setOf(target, other),
                setOf(ValidatedWorkSource(source.contextId, source.contributorUserId, source.captureUntilUtc)))
            val candidate = app.store.inspectUpload(access, sealed.batchId)

            val removal = removals.save(removals.newRemoval(scope, IDENTITY, target, true, RemovalConfirmation.Production))
            val competing = removals.newRemoval(scope, IDENTITY, other, true, RemovalConfirmation.Equipment)
            val removalBatch = removals.prepareBatch(scope, IDENTITY, benchId(85), removal.reference())
            suspend fun assertBlocked(state: WorkState, batchProblem: WorkProblem) {
                val projection = removals.draft(scope, IDENTITY)
                assertEquals(state, projection.history.single().state)
                assertEquals(listOf(removal.reference()), projection.items.mapNotNull { it.provisionalRemoval })
                assertFalse(projection.provisionallyAllItemsRemoved)
                assertEquals("1.25", projection.samples.single().value.inspectionValue)
                val attempts = listOf(samples.newCapture(target, input),
                    OfflineSampleEdit.Correct(capture.key, input, measured.envelope.reference()),
                    OfflineSampleEdit.Remove(capture.key, measured.envelope.reference()))
                for (edit in attempts) {
                    assertWorkFailure(WorkProblem.ItemDispositionConflict) { samples.save(scope, edit) }
                    assertWorkFailure(WorkProblem.ItemDispositionConflict) {
                        app.store.enqueue(access, edit.operation(source.contextId, 7))
                    }
                }
                assertWorkFailure(WorkProblem.ItemRemovalReconciliationRequired) { removals.save(competing) }
                val replayedSample = samples.save(scope, capture).envelope
                assertEquals(measured.envelope.reference(), replayedSample.reference())
                assertEquals(measured.envelope.canonicalPayload, replayedSample.canonicalPayload)
                assertEquals(measured.envelope.originLocalEditVersion, replayedSample.originLocalEditVersion)
                assertEquals(removal, removals.save(removal.command))
                assertWorkFailure(batchProblem) { samples.prepareBatch(scope, benchId(86), listOf(extra)) }
                assertWorkFailure(batchProblem) { app.store.prepareBatch(access, benchId(86), listOf(extra)) }
                assertWorkFailure(batchProblem) { app.store.startAttempt(access, sealed.batchId) }
                assertWorkFailure(batchProblem) { app.store.inspectUpload(access, sealed.batchId) }
                assertWorkFailure(batchProblem) { app.store.beginUpload(access, candidate) }
                val unrelated = samples.newCapture(other, SampleInput.Qualitative(QualitativeJudgment.OK))
                assertEquals(other, samples.save(scope, unrelated).envelope.operation.attempt)
            }

            assertBlocked(WorkState.Queued, WorkProblem.ItemDispositionConflict)
            app.store.startItemRemovalAttempt(access, grant.draft.sampleBasis, removalBatch.batchId)
            assertBlocked(WorkState.InFlight, WorkProblem.InvalidState)
            app.store.markItemRemovalOutcomeUnknown(access, removalBatch.batchId)
            assertBlocked(WorkState.OutcomeUnknown, WorkProblem.InvalidState)
            assertEquals(0, fixture.wire.sends.get())
        }
    }

    @Test fun legacyParallelRowsKeepTheirBytesAndOnlyTheFirstCanAdvanceTheVersion() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.workServices()
            activate(app, IDENTITY)
            val scope = fixture.scope()
            val local = app.online.offlineItemRemovals
            val first = local.save(local.newRemoval(scope, IDENTITY, fixture.attempt(scope.taskId, 0),
                true, RemovalConfirmation.Production))
            val secondCommand = local.newRemoval(scope, IDENTITY, fixture.attempt(scope.taskId, 1),
                true, RemovalConfirmation.Equipment)
            val secondSave = runCatching { local.save(secondCommand) }
            assertTrue("Two same-version saves produced ${local.draft(scope, IDENTITY).items.count { it.provisionalRemoval != null }} provisional removals",
                secondSave.isFailure)
            assertEquals(WorkProblem.ItemRemovalReconciliationRequired, (secondSave.exceptionOrNull() as? WorkFailure)?.problem)
            val prior = requireNotNull(app.rows.transaction(scope) {
                val catalog = catalog()
                val sequence = catalog.localEditVersion + 1
                val bytes = ItemRemovalCanonical.operation(secondCommand, sequence)
                val legacy = first.copy(command = secondCommand, localEditVersion = sequence,
                    minimumSampleCount = 50, canonicalPayload = bytes, contentHash = workHash(bytes))
                reserveItemRemovalId(secondCommand.operationId)
                saveItemRemoval(ItemRemovalEntry(legacy))
                save(catalog.copy(localEditVersion = sequence, itemRemovalIds = catalog.itemRemovalIds + secondCommand.operationId))
                legacy
            })
            val projected = local.draft(scope, IDENTITY)
            assertFalse(projected.provisionallyAllItemsRemoved)
            assertEquals(listOf(first.reference()), projected.items.mapNotNull { it.provisionalRemoval })
            assertEquals(listOf(WorkState.Queued, WorkState.Conflict), projected.history.map { it.state })
            assertWorkFailure(WorkProblem.ItemRemovalReconciliationRequired) {
                local.prepareBatch(scope, IDENTITY, benchId(87), prior.reference())
            }
            assertEquals(prior, local.save(secondCommand))
            assertEquals(first.reference(), local.prepareBatch(scope, IDENTITY, benchId(88), first.reference()).operation)
            assertEquals(0, fixture.wire.sends.get())
        }
    }

    @Test fun realProtectedClockRejectsExpiredReceiptsAndRollbackWithoutErasingTheItem() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val a = fixture.workServices()
            activate(a, IDENTITY)
            val task = fixture.scope()
            val local = a.online.offlineItemRemovals
            val saved = local.save(local.newRemoval(task, IDENTITY, fixture.attempt(task.taskId, 0),
                true, RemovalConfirmation.Production))
            val pending = local.newRemoval(task, IDENTITY, fixture.attempt(task.taskId, 1),
                true, RemovalConfirmation.Equipment)
            val first = fixture.device.sample
            fixture.device.sample = first.copy(elapsedMs = first.elapsedMs + 60_000, utcMs = first.utcMs + 60_000)
            assertWorkFailure(WorkProblem.CaptureWindowClosed) { local.save(pending) }
            assertEquals(saved, local.save(saved.command))
            fixture.device.sample = first.copy(elapsedMs = first.elapsedMs + 60_001, utcMs = first.utcMs - 180_000)
            assertFailure(Issue.OFFLINE_CLOCK) { local.draft(task, IDENTITY) }
            assertEquals(saved, a.rows.transaction(task) { itemRemoval(saved.command.operationId)?.removal })
        }
        WorkbenchDeviceFixture().use { fixture ->
            val a = fixture.workServices()
            activate(a, IDENTITY)
            val task = fixture.scope()
            val local = a.online.offlineItemRemovals
            val saved = local.save(local.newRemoval(task, IDENTITY, fixture.attempt(task.taskId, 0),
                true, RemovalConfirmation.Production))
            val first = fixture.device.sample
            val expires = 72 * 3600 * 1000L
            fixture.device.sample = first.copy(elapsedMs = first.elapsedMs + expires, utcMs = first.utcMs + expires)
            assertFailure(Issue.OFFLINE_EXPIRED) { local.save(saved.command) }
            assertEquals(saved, a.rows.transaction(task) { itemRemoval(saved.command.operationId)?.removal })
        }
    }

    @Test fun acceptedRemovalFencesAllNewSampleEditsUntilVerifiedRefresh() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val a = fixture.workServices()
            val grant = activate(a, IDENTITY)
            val scope = fixture.scope()
            val removals = a.online.offlineItemRemovals
            val samples = a.online.offlineSamples
            val removedAttempt = fixture.attempt(scope.taskId, 0)
            val sampleAttempt = fixture.attempt(scope.taskId, 1)
            val input = SampleInput.Quantitative("1.50")
            val oldCapture = samples.newCapture(sampleAttempt, input)
            val original = samples.save(scope, oldCapture).envelope
            val removal = removals.save(removals.newRemoval(scope, IDENTITY, removedAttempt, true, RemovalConfirmation.Production))
            val batch = removals.prepareBatch(scope, IDENTITY, benchId(91), removal.reference())
            val priorForCorrection = samples.save(scope, samples.newCapture(sampleAttempt, input)).envelope
            assertEquals(7L, priorForCorrection.operation.baseDraftVersion)
            assertNull(samples.captureProblem(scope, IDENTITY))

            a.store.startItemRemovalAttempt(approved(grant, removal.source), grant.draft.sampleBasis, batch.batchId)
            val receipt = acceptance(removal, batch, IDENTITY)
            fixture.wire.explicitRemovalReceipt = receipt
            assertEquals(ItemRemovalReceiptRecovery.BatchConfirmed(receipt), a.online.recoverItemRemovalReceipt(
                grant, ReceiptLookup(ReceiptKind.Batch, batch.batchId)))
            val before = requireNotNull(a.rows.transaction(scope) { catalog() })
            val rowCount = fixture.device.rows.count()
            val edits = listOf(samples.newCapture(sampleAttempt, input),
                OfflineSampleEdit.Correct(LocalSampleKey(sampleAttempt, priorForCorrection.operation.clientSampleId),
                    SampleInput.Quantitative("1.75"), priorForCorrection.reference()),
                OfflineSampleEdit.Remove(oldCapture.key, original.reference()))
            val offlineProblems = edits.map { edit ->
                runCatching { samples.save(scope, edit) }.exceptionOrNull().let { (it as? WorkFailure)?.problem }
            }
            val onlineProblems = edits.map { edit ->
                val newId = when (edit) {
                    is OfflineSampleEdit.Capture -> edit.copy(operationId = benchId(92), key = edit.key.copy(clientSampleId = benchId(93)))
                    is OfflineSampleEdit.Correct -> edit.copy(operationId = benchId(94))
                    is OfflineSampleEdit.Remove -> edit.copy(operationId = benchId(95))
                }
                runCatching { a.store.enqueue(grant.access, newId.operation(removal.source.contextId, 7)) }
                    .exceptionOrNull().let { (it as? WorkFailure)?.problem }
            }
            assertEquals("Offline and online admission at version 7",
                List(6) { WorkProblem.StaleGrant }, offlineProblems + onlineProblems)
            assertEquals(rowCount, fixture.device.rows.count())
            assertEquals(before, a.rows.transaction(scope) { catalog() })
            val stale = samples.draft(scope, IDENTITY)
            assertEquals(7L, stale.observedServerDraftVersion)
            assertEquals(WorkProblem.StaleGrant, stale.captureProblem)
            assertEquals(WorkProblem.StaleGrant, samples.captureProblem(scope, IDENTITY))
            assertEquals(listOf(original.reference(), priorForCorrection.reference()), stale.history.map { it.operation.envelope.reference() })
            assertEquals("1.25", stale.confirmed.single().value.inspectionValue)
            val items = removals.draft(scope, IDENTITY)
            assertEquals(WorkState.Accepted, items.history.single().state)
            assertEquals(receipt.itemOperations.single(), items.history.single().receipt)
            assertNull(items.items.single { it.attempt == removedAttempt }.provisionalRemoval)
            assertEquals(original.canonicalPayload, samples.save(scope, oldCapture).envelope.canonicalPayload)
            assertEquals(original.reference(), a.store.enqueue(grant.access, original.operation).envelope.reference())
            assertEquals(removal, removals.save(removal.command))

            fixture.wire.observedRemoval = receipt.itemOperations.single()
            val refreshed = a.online.issueOrReuse(a.online.open(fixture.task(), IDENTITY))
            assertEquals(8L, refreshed.draft.version)
            assertNull(samples.captureProblem(scope, IDENTITY))
            val reopenedHistory = samples.draft(scope, IDENTITY).history
            assertEquals(listOf(original.reference(), priorForCorrection.reference()),
                reopenedHistory.map { it.operation.envelope.reference() })
            assertTrue(reopenedHistory.none { it.matchesObservedDraftVersion })
            val newCapture = samples.newCapture(sampleAttempt, input)
            val newOffline = samples.save(scope, newCapture).envelope
            val onlineCapture = samples.newCapture(sampleAttempt, input)
            val newOnline = a.store.enqueue(refreshed.access, onlineCapture.operation(removal.source.contextId, 8)).envelope
            assertEquals(8L, newOffline.operation.baseDraftVersion)
            assertEquals(8L, newOnline.operation.baseDraftVersion)
            assertNotEquals(original.reference(), newOffline.reference())
            val savedAgain = samples.save(scope, oldCapture).envelope
            assertEquals(original.reference(), savedAgain.reference())
            assertEquals(original.canonicalPayload, savedAgain.canonicalPayload)
            assertEquals(original.source, savedAgain.source)
            assertEquals(7L, savedAgain.operation.baseDraftVersion)
            assertEquals(0, fixture.wire.sends.get())
        }
    }

    @Test fun individualRemovalReceiptFencesBThroughRoomReopenWithoutSettlingUnknownBatch() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val a = fixture.workServices()
            activate(a, IDENTITY)
            val scope = fixture.scope()
            val aLocal = a.online.offlineItemRemovals
            val removal = aLocal.save(aLocal.newRemoval(scope, IDENTITY, fixture.attempt(scope.taskId, 0),
                true, RemovalConfirmation.Production))
            a.sessions.logout(attemptRemote = false)
            fixture.device.api.loginSubject = fixture.b
            val b = activate(a, fixture.b)
            val samples = a.online.offlineSamples
            val sampleAttempt = fixture.attempt(scope.taskId, 1)
            val input = SampleInput.Quantitative("1.50")
            val pending = samples.newCapture(sampleAttempt, input)
            val prior = samples.save(scope, pending).envelope
            fixture.wire.explicitRemovalSource = removal.source
            val approved = a.online.preflight(b, removal.command.operationId)
            val batch = a.online.prepareItemRemovalForReconciliation(approved, benchId(96), removal.reference())
            a.store.startItemRemovalAttempt(approved.access, approved.draft.sampleBasis, batch.batchId)
            a.store.markItemRemovalOutcomeUnknown(approved.access, batch.batchId)
            val receipt = acceptance(removal, batch, fixture.b).itemOperations.single()
            fixture.wire.explicitIndividualRemovalReceipt = receipt
            assertEquals(ItemRemovalReceiptRecovery.OperationObserved(receipt), a.online.recoverItemRemovalReceipt(
                approved, ReceiptLookup(ReceiptKind.Operation, removal.command.operationId)))
            val bSource = requireNotNull(b.source)
            val oldCaptureAccess = WorkAccess(approved.access.session, scope, 7, approved.draft.applicableAttempts,
                requiresCaptureGrant = true, captureGrant = requireNotNull(b.access.captureGrant))
            assertWorkFailure(WorkProblem.StaleGrant) {
                a.store.enqueue(oldCaptureAccess, samples.newCapture(sampleAttempt, input).operation(bSource.contextId, 7))
            }

            fixture.device.database.close()
            val db = TaskCacheDatabase.open(fixture.device.context, fixture.device.databaseFile)
            try {
                val restarted = fixture.workServices(cache = fixture.device.openCache(db), rows = RoomCacheRows(db))
                val offline = restarted.online.offlineSamples
                assertEquals(WorkProblem.StaleGrant, offline.captureProblem(scope, fixture.b))
                assertWorkFailure(WorkProblem.StaleGrant) { offline.save(scope, offline.newCapture(sampleAttempt, input)) }
                assertEquals(prior.reference(), offline.save(scope, pending).envelope.reference())
                val savedRemoval = restarted.rows.transaction(scope) { itemRemoval(removal.command.operationId) }!!
                assertEquals(WorkState.OutcomeUnknown, savedRemoval.state)
                assertEquals(receipt, savedRemoval.individualReceipt)
                assertEquals(IDENTITY.userId, savedRemoval.removal.source.contributorUserId)
                assertEquals(fixture.b.userId, receipt.uploadedByUserId)
                fixture.wire.observedRemoval = receipt
                val fresh = activate(restarted, fixture.b)
                assertEquals(8L, fresh.draft.version)
                val freshSample = offline.save(scope, offline.newCapture(sampleAttempt, input)).envelope
                assertEquals(8L, freshSample.operation.baseDraftVersion)
                assertWorkFailure(WorkProblem.InvalidState) {
                    offline.prepareBatch(scope, benchId(97), listOf(freshSample.operation.operationId))
                }
                assertEquals(0, fixture.wire.sends.get())
            } finally {
                db.close()
            }
        }
    }

    @Test fun authenticatedRemoteRemovalObservationFencesAnotherOperatorsOldProfile() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.workServices()
            activate(app, IDENTITY)
            val scope = fixture.scope()
            val remote = fixture.attempt(scope.taskId, 0)
            val receipt = ItemRemovalReceipt(scope.taskId, benchId(101), benchId(102), "RemoveItem", 1,
                "A".repeat(64), remote.taskItemId, remote.inspectionAttemptId, benchId(103),
                fixture.b.userId, fixture.b.userId, fixture.b.familyId, scope.environmentId, scope.lineId,
                scope.workflowVersion, NOW, 8, ItemDisposition.Removed, true, RemovalConfirmation.Equipment, null, 1, 1, false)
            app.sessions.logout(attemptRemote = false)
            fixture.device.api.loginSubject = fixture.b
            app.sessions.login("synthetic-reader", "test-only")
            fixture.wire.observedRemoval = receipt
            assertEquals(8L, app.online.open(fixture.task(), fixture.b).draft.version)
            app.sessions.logout(attemptRemote = false)
            fixture.device.api.loginSubject = IDENTITY
            app.sessions.login("synthetic-inspector", "test-only")

            val offline = app.online.offlineSamples
            val draft = offline.draft(scope, IDENTITY)
            assertEquals(7L, draft.observedServerDraftVersion)
            assertEquals(WorkProblem.StaleGrant, draft.captureProblem)
            assertWorkFailure(WorkProblem.StaleGrant) {
                offline.save(scope, offline.newCapture(fixture.attempt(scope.taskId, 1), SampleInput.Quantitative("1.75")))
            }
            assertTrue(app.rows.transaction(scope) { catalog().operationIds.isEmpty() && catalog().itemRemovalIds.isEmpty() } == true)
        }
    }


    @Test fun roomAdmissionFailureOrLostAcknowledgementNeverStartsRemovalPost() = runBlocking<Unit>(Dispatchers.IO) {
        for (rollback in listOf(true, false)) WorkbenchDeviceFixture().use { fixture ->
            val faults = RemovalFaultRows(fixture.device.rows)
            val app = fixture.workServices(rows = faults)
            val grant = activate(app, IDENTITY)
            val scope = fixture.scope()
            val local = app.online.offlineItemRemovals
            val command = local.newRemoval(scope, IDENTITY, fixture.attempt(scope.taskId, 1),
                true, RemovalConfirmation.Equipment, SECRET_REASON)
            val saved = local.save(command)
            val batch = local.prepareBatch(scope, IDENTITY, benchId(90), saved.reference())
            fixture.wire.explicitRemovalSource = saved.source
            fixture.wire.removalUploadTestEnabled = true
            fixture.wire.beforeRemovalAdmission = {
                if (rollback) faults.rollback.set(true) else faults.dropAck.set(true)
            }
            assertWorkFailure(WorkProblem.StorageUnavailable) { app.online.uploadItemRemovalBatch(grant, batch.batchId) }
            assertEquals(0, fixture.wire.removalPosts.get())
            val expected = if (rollback) WorkState.Queued else WorkState.OutcomeUnknown
            assertEquals(expected, local.draft(scope, IDENTITY).history.single().state)
            fixture.device.database.close()
            val db = TaskCacheDatabase.open(fixture.device.context, fixture.device.databaseFile)
            try {
                val reopened = fixture.workServices(cache = fixture.device.openCache(db), rows = RoomCacheRows(db))
                assertEquals(saved, reopened.online.offlineItemRemovals.save(command))
                val history = reopened.online.offlineItemRemovals.draft(scope, IDENTITY).history.single()
                assertEquals(expected, history.state)
                assertEquals(batch, history.batch)
                assertEquals(0, fixture.wire.removalPosts.get())
            } finally {
                db.close()
            }
        }
    }

    @Test fun exactPostAndReceiptRecoveryPreserveTheOriginalThroughRoomReopen() = runBlocking<Unit>(Dispatchers.IO) {
        for (lostSettlementAck in listOf(true, false)) WorkbenchDeviceFixture().use { fixture ->
            val faults = RemovalFaultRows(fixture.device.rows)
            val app = fixture.workServices(rows = faults)
            activate(app, IDENTITY)
            val scope = fixture.scope()
            val local = app.online.offlineItemRemovals
            val command = local.newRemoval(scope, IDENTITY, fixture.attempt(scope.taskId, 1),
                true, RemovalConfirmation.Production, SECRET_REASON)
            val saved = local.save(command)
            val batch = local.prepareBatch(scope, IDENTITY, benchId(91), saved.reference())
            app.sessions.logout(attemptRemote = false)
            fixture.device.api.loginSubject = fixture.b
            val grant = activate(app, fixture.b)
            val accepted = acceptance(saved, batch, fixture.b)
            val receipt = accepted.itemOperations.single()
            fixture.wire.explicitRemovalSource = saved.source
            fixture.wire.removalUploadTestEnabled = true
            fixture.wire.removalResponse = { request ->
                assertEquals(WorkState.InFlight, app.rows.transaction(scope) { itemRemoval(command.operationId)?.state })
                assertEquals(JsonPrimitive(batch.batchId), request["batchId"])
                val operation = request.getValue("operation").jsonObject
                assertEquals(JsonPrimitive(command.owner.userId), operation["contributorUserIdAssertion"])
                assertEquals(JsonPrimitive(command.operationId), operation["operationId"])
                assertEquals(JsonPrimitive(command.reason), operation["reason"])
                if (!lostSettlementAck) throw IOException("Synthetic disconnect after request")
                faults.dropAck.set(true)
                """{"batchReceipt":${Json.encodeToString(accepted)},"operation":${Json.encodeToString(receipt)},
                    "replayOnly":false,"canExecute":false,"acceptsFormalInspection":false}"""
            }
            if (lostSettlementAck) {
                assertWorkFailure(WorkProblem.StorageUnavailable) { app.online.uploadItemRemovalBatch(grant, batch.batchId) }
            } else {
                assertEquals(ItemRemovalUploadResult.OutcomeUnknown(Issue.CONNECTION),
                    app.online.uploadItemRemovalBatch(grant, batch.batchId))
            }
            assertEquals(1, fixture.wire.removalPosts.get())
            fixture.device.database.close()
            val db = TaskCacheDatabase.open(fixture.device.context, fixture.device.databaseFile)
            try {
                val fresh = fixture.workServices(cache = fixture.device.openCache(db), rows = RoomCacheRows(db))
                val current = activate(fresh, fixture.b)
                val lookup = ReceiptLookup(ReceiptKind.Batch, batch.batchId)
                val id = command.operationId
                val pendingState = if (lostSettlementAck) WorkState.Accepted else WorkState.OutcomeUnknown
                assertEquals(pendingState, fresh.rows.transaction(scope) { itemRemoval(id)?.state })
                assertWorkFailure(WorkProblem.InvalidState) { fresh.online.uploadItemRemovalBatch(current, batch.batchId) }
                val recovery = fresh.online.open(fixture.task(), fixture.b)
                if (!lostSettlementAck) {
                    fixture.wire.missingRemovalReceipt = true
                    assertEquals(ItemRemovalReceiptRecovery.Unresolved(ReceiptUncertainty.NotObserved),
                        fresh.online.recoverItemRemovalReceipt(recovery, lookup))
                    fixture.wire.missingRemovalReceipt = false
                    fixture.wire.explicitIndividualRemovalReceipt = receipt
                    assertEquals(ItemRemovalReceiptRecovery.OperationObserved(receipt), fresh.online.recoverItemRemovalReceipt(
                        recovery, ReceiptLookup(ReceiptKind.Operation, id)))
                    assertEquals(WorkState.OutcomeUnknown, fresh.rows.transaction(scope) { itemRemoval(id)?.state })
                    assertWorkFailure(WorkProblem.StaleGrant) {
                        val sample = fresh.online.offlineSamples.newCapture(fixture.attempt(scope.taskId, 0),
                            SampleInput.Qualitative(QualitativeJudgment.OK))
                        fresh.online.offlineSamples.save(scope, sample)
                    }
                }
                fixture.wire.explicitRemovalReceipt = accepted
                assertEquals(ItemRemovalReceiptRecovery.BatchConfirmed(accepted),
                    fresh.online.recoverItemRemovalReceipt(recovery, lookup))
                val item = fresh.rows.transaction(scope) { requireNotNull(itemRemoval(id)) }
                assertEquals(saved, item?.removal)
                assertEquals(receipt, item?.serverEvidence)
                assertEquals(WorkState.Accepted, item?.state)
                assertEquals("1.25", fresh.online.offlineItemRemovals.draft(scope, fixture.b).samples.single().value.inspectionValue)
                assertEquals(1, fixture.wire.removalPosts.get())
                assertEncrypted(fixture.device.databaseFile)
            } finally {
                db.close()
            }
        }
    }

    private fun approved(grant: OnlineWorkCoordinator.Grant, source: WorkSource) =
        WorkAccess(grant.access.session, source.task, grant.draft.version, grant.draft.applicableAttempts,
            setOf(ValidatedWorkSource(source.contextId, source.contributorUserId, source.captureUntilUtc)))

    private fun acceptance(removal: ItemRemovalEnvelope, batch: ItemRemovalBatch, uploader: SessionIdentity): ItemRemovalBatchReceipt {
        val command = removal.command
        val scope = command.task
        val operation = ItemRemovalReceipt(scope.taskId, command.operationId, batch.batchId, "RemoveItem", 1,
            removal.contentHash, command.attempt.taskItemId, command.attempt.inspectionAttemptId, command.contextId,
            command.owner.userId, uploader.userId, uploader.familyId, scope.environmentId, scope.lineId,
            scope.workflowVersion, NOW, 8, ItemDisposition.Removed, true, command.confirmation, command.reason,
            removal.minimumSampleCount, 1, false)
        return ItemRemovalBatchReceipt(scope.taskId, batch.batchId, batch.fingerprint, uploader.userId, uploader.familyId,
            NOW, 7, 8, emptyList(), listOf(operation), 2)
    }

    private suspend fun activate(app: WorkbenchWorkServices, owner: SessionIdentity):
        OnlineWorkCoordinator.Grant {
        app.sessions.login("synthetic-inspector", "test-only")
        val line = app.patrol.lines().single { it.id == LINE.id }
        val selected = app.patrol.tasks(line, 1).items.single()
        val grant = app.online.issueOrReuse(app.online.open(selected, owner))
        app.online.armOfflineSamples(grant)
        return grant
    }

    private suspend fun assertWorkFailure(problem: WorkProblem, run: suspend () -> Any?) {
        val failure = runCatching { run() }.exceptionOrNull()
        assertEquals(problem, (failure as? WorkFailure)?.problem)
    }

    private suspend fun assertFailure(issue: Issue, run: suspend () -> Any?) {
        val failure = runCatching { run() }.exceptionOrNull()
        assertEquals(issue, (failure as? PatrolFailure)?.issue)
    }

    private fun assertEncrypted(database: File) {
        database.parentFile!!.listFiles().orEmpty().filter { it.name.startsWith(database.name) }.forEach {
            assertFalse(it.readBytes().toString(Charsets.ISO_8859_1).contains(SECRET_REASON))
        }
    }

    private class RemovalFaultRows(private val base: CacheRows) : CacheRows by base {
        val rollback = AtomicBoolean()
        val dropAck = AtomicBoolean()
        private val writes = AtomicInteger()

        override suspend fun replace(slot: String, sealed: ByteArray) {
            base.replace(slot, sealed)
            writes.incrementAndGet()
            if (slot.startsWith("work-v1:") && rollback.compareAndSet(true, false)) error("Synthetic rollback")
        }

        override suspend fun <T> transaction(block: suspend () -> T): T {
            val before = writes.get()
            val result = base.transaction(block)
            if (writes.get() != before && dropAck.compareAndSet(true, false)) error("Synthetic lost acknowledgement")
            return result
        }
    }

    private companion object { const val SECRET_REASON = "Synthetic private disposition remark" }
}
