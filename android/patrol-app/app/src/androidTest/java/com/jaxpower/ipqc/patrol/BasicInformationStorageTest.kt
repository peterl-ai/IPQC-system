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
import java.io.IOException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonObject
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.ResponseBody.Companion.toResponseBody
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import retrofit2.Response

@RunWith(AndroidJUnit4::class)
class BasicInformationStorageTest {
    @Test fun realRoomCommitFaultsAndReopenPreserveOneExactEncryptedOperation() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val faults = CommitFault(fixture.device.rows)
            val app = fixture.workServices(rows = faults)
            val task = fixture.scope()
            fixture.respondBasicInformation(7)
            val grant = open(app, IDENTITY, login = true)
            app.online.readBasicInformation(grant)
            val local = app.online.offlineBasicInformation
            expectWork(WorkProblem.SourceUnavailable) { local.newEdit(task, IDENTITY, BasicInformationField.Customer, null) }
            activate(app, grant)
            val command = local.newEdit(task, IDENTITY, BasicInformationField.Customer, ENCRYPTED_TEST_VALUE)
            val rowsBefore = fixture.device.rows.count()
            faults.point = Fault.BeforeCommit
            expectWork(WorkProblem.StorageUnavailable) { local.save(command) }
            assertEquals(rowsBefore, fixture.device.rows.count())
            assertTrue(local.draft(task, IDENTITY).proposals.isEmpty())
            assertEquals(0L, local.draft(task, IDENTITY).localEditVersion)

            faults.point = Fault.AfterCommit
            expectWork(WorkProblem.StorageUnavailable) { local.save(command) }
            val committed = local.draft(task, IDENTITY)
            val original = committed.proposals.single().original
            assertEquals(command, original.command)
            assertEquals(1L, committed.localEditVersion)
            assertEquals(BasicInformationPresence.Unrecorded, committed.accepted?.field(BasicInformationField.Customer)?.presence)
            assertEquals(1, fixture.wire.headerReads.get())

            fixture.device.database.close()
            val database = TaskCacheDatabase.open(fixture.device.context, fixture.device.databaseFile)
            try {
                val reopened = fixture.workServices(fixture.device.openCache(database), RoomCacheRows(database))
                    .online.offlineBasicInformation
                assertEquals(original, reopened.save(command))
                assertEquals(original, reopened.draft(task, IDENTITY).proposals.single().original)
                assertEquals(1L, reopened.draft(task, IDENTITY).localEditVersion)
                expectWork(WorkProblem.OperationIdentityConflict) {
                    reopened.save(command.copy(operation = command.operation.copy(value = null)))
                }
                assertEquals(1, fixture.wire.headerReads.get())
                assertEquals(0, fixture.wire.removalPosts.get())
                assertEquals(0, fixture.wire.sends.get())
            } finally {
                database.close()
            }
            val file = fixture.device.databaseFile
            file.parentFile!!.listFiles().orEmpty().filter { it.name.startsWith(file.name) }.forEach {
                assertFalse(it.readBytes().toString(Charsets.ISO_8859_1).contains(ENCRYPTED_TEST_VALUE))
            }
        }
    }

    @Test fun correctionRequiresCurrentAcceptedFieldAndKeepsUnchangedProvenanceAndDomainIds() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.workServices()
            val task = fixture.scope()
            val customer = accepted(task, BasicInformationField.Customer, "Prior Customer", 5, 91)
            val production = accepted(task, BasicInformationField.Production, " \u00A0", 6, 92)
            fixture.respondBasicInformation(7, customer, production)
            val grant = activate(app, open(app, IDENTITY, login = true))
            val header = app.online.readBasicInformation(grant)
            val local = app.online.offlineBasicInformation

            expectWork(WorkProblem.DependencyMismatch) { local.newEdit(task, IDENTITY, BasicInformationField.Customer, null) }
            expectWork(WorkProblem.DependencyMismatch) {
                local.newEdit(task, IDENTITY, BasicInformationField.Customer, null, production.reference())
            }
            expectWork(WorkProblem.DependencyMismatch) {
                local.newEdit(task, IDENTITY, BasicInformationField.Customer, null,
                    OperationReference(customer.operation.operationId, "A".repeat(64)))
            }

            val command = local.newEdit(task, IDENTITY, BasicInformationField.Customer, null, customer.reference())
            val other = local.newEdit(task, IDENTITY, BasicInformationField.ProductMo, "")
            val attempt = fixture.attempt(task.taskId, 0)
            val sample = app.online.offlineSamples
            val capture = sample.newCapture(attempt, SampleInput.Qualitative(QualitativeJudgment.OK))
            val existing = sample.save(task, capture, IDENTITY)
            expectWork(WorkProblem.OperationIdentityConflict) {
                local.save(command.withId(existing.envelope.operation.operationId))
            }
            val removal = app.online.offlineItemRemovals
            val removalCommand = removal.newRemoval(task, IDENTITY, fixture.attempt(task.taskId, 1), true,
                RemovalConfirmation.Production)
            val savedRemoval = removal.save(removalCommand)
            expectWork(WorkProblem.OperationIdentityConflict) { local.save(command.withId(savedRemoval.command.operationId)) }
            val corrected = local.save(command)
            val captured = local.save(other)
            expectWork(WorkProblem.OperationIdentityConflict) {
                sample.save(task, capture.copy(operationId = captured.command.operation.operationId,
                    key = capture.key.copy(clientSampleId = benchId(95))), IDENTITY)
            }
            expectWork(WorkProblem.InvalidState) { local.newEdit(task, IDENTITY, BasicInformationField.ProductMo, "new") }
            assertEquals(header.basis, local.draft(task, IDENTITY).accepted)
            assertEquals(listOf(corrected, captured), local.draft(task, IDENTITY).proposals.map { it.original })
            assertNull(corrected.command.operation.value)
            assertEquals("", captured.command.operation.value)
            assertEquals(4L, local.draft(task, IDENTITY).localEditVersion)
        }
    }

    @Test fun pinnedConflictAndHigherHeaderObservationFenceWholeTaskWithoutRebasing() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.workServices()
            val task = fixture.scope()
            fixture.respondBasicInformation(7)
            val grant = activate(app, open(app, IDENTITY, login = true))
            app.online.readBasicInformation(grant)
            val local = app.online.offlineBasicInformation
            val old = local.save(local.newEdit(task, IDENTITY, BasicInformationField.Customer, "Retain original"))
            val later = local.newEdit(task, IDENTITY, BasicInformationField.Production, "Old basis")
            val attempt = fixture.attempt(task.taskId, 0)
            val removal = app.online.offlineItemRemovals
            val toRemove = removal.newRemoval(task, IDENTITY, attempt, true, RemovalConfirmation.Equipment)
            val sample = app.online.offlineSamples
            val toSave = sample.newCapture(attempt, SampleInput.Qualitative(QualitativeJudgment.OK))
            fixture.wire.headerReply = { _, _, _ ->
                Response.error(409, """{"status":409,"code":"BasicInformationVersionConflict"}""".toResponseBody())
            }
            val conflict = runCatching { app.online.readBasicInformation(grant) }.exceptionOrNull()
            assertEquals(Issue.CONFLICT, (conflict as? PatrolFailure)?.issue)
            assertEquals(2, fixture.wire.headerReads.get())
            assertEquals(7L, local.draft(task, IDENTITY).accepted?.draftVersion)
            expectWork(WorkProblem.StaleGrant) { local.save(later) }
            expectWork(WorkProblem.StaleGrant) { sample.save(task, toSave, IDENTITY) }
            expectWork(WorkProblem.StaleGrant) { removal.save(toRemove) }
            assertEquals(old, local.save(old.command))

            val confirmed = accepted(task, BasicInformationField.Production, "New observation", 7, 93)
            fixture.respondBasicInformation(8, confirmed)
            val staleGrant = open(app, IDENTITY)
            app.online.readBasicInformation(staleGrant, pinToGrant = false)
            assertNull(fixture.wire.lastHeaderPin)
            assertEquals(8L, local.draft(task, IDENTITY).accepted?.draftVersion)
            assertEquals(WorkProblem.StaleGrant, local.draft(task, IDENTITY).captureProblem)
            assertFalse(local.draft(task, IDENTITY).proposals.single().atCurrentDraftVersion)
            expectWork(WorkProblem.StaleGrant) { sample.save(task, toSave, IDENTITY) }
            expectWork(WorkProblem.StaleGrant) { removal.save(toRemove) }

            fixture.wire.sharedDraftVersionOverride = 8
            val matched = open(app, IDENTITY)
            app.online.readBasicInformation(matched)
            assertEquals(8L, fixture.wire.lastHeaderPin)
            val fresh = local.save(local.newEdit(task, IDENTITY, BasicInformationField.ProductMo, "  "))
            assertEquals(8L, fresh.command.operation.baseDraftVersion)
            assertEquals(confirmed, local.draft(task, IDENTITY).accepted?.field(BasicInformationField.Production)?.accepted)
            assertEquals(old, local.save(old.command))

            val contradictory = accepted(task, BasicInformationField.Customer, "Backdated fact", 5, 95)
            fixture.respondBasicInformation(9, confirmed, contradictory)
            fixture.wire.sharedDraftVersionOverride = 9
            val advanced = open(app, IDENTITY)
            expectWork(WorkProblem.ReceiptMismatch) { app.online.readBasicInformation(advanced) }
            assertEquals(BasicInformationPresence.Unrecorded, local.draft(task, IDENTITY).accepted
                ?.field(BasicInformationField.Customer)?.presence)
            assertEquals(8L, local.draft(task, IDENTITY).accepted?.draftVersion)
            assertEquals(WorkProblem.StaleGrant, local.draft(task, IDENTITY).captureProblem)
        }
    }

    @Test fun otherDomainObservationsAndUnresolvedAttemptsProtectHeaderAdmission() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.workServices()
            val task = fixture.scope()
            fixture.respondBasicInformation(7)
            val grant = activate(app, open(app, IDENTITY, login = true))
            app.online.readBasicInformation(grant)
            val local = app.online.offlineBasicInformation
            val cmd = local.newEdit(task, IDENTITY, BasicInformationField.ProductMo, null)
            val attempt = fixture.attempt(task.taskId, 0)
            val removal = app.online.offlineItemRemovals
            val op = removal.save(removal.newRemoval(task, IDENTITY, attempt, true, RemovalConfirmation.Equipment))
            val batch = removal.prepareBatch(task, IDENTITY, benchId(94), op.reference())
            val source = requireNotNull(grant.source)
            val access = WorkAccess(grant.access.session, task, 7, grant.draft.attempts,
                setOf(ValidatedWorkSource(source.contextId, source.contributorUserId, source.captureUntilUtc)))
            app.store.startItemRemovalAttempt(access, grant.draft.sampleBasis, batch.batchId)
            expectWork(WorkProblem.InvalidState) { local.save(cmd) }
            app.store.markItemRemovalOutcomeUnknown(access, batch.batchId)
            expectWork(WorkProblem.InvalidState) { local.save(cmd) }

            val unrelated = app.online.offlineSamples.newCapture(fixture.attempt(task.taskId, 1), SampleInput.Quantitative("1.1"))
            assertNotNull(app.online.offlineSamples.save(task, unrelated, IDENTITY))
            fixture.wire.sharedDraftVersionOverride = 8
            open(app, IDENTITY)
            expectWork(WorkProblem.StaleGrant) { local.save(cmd) }
            assertEquals(7L, local.draft(task, IDENTITY).accepted?.draftVersion)
            assertTrue(local.draft(task, IDENTITY).proposals.isEmpty())
        }
    }

    @Test fun queuedSampleAllowsEditingButUnresolvedSampleBatchFencesHeader() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.workServices()
            val task = fixture.scope()
            fixture.respondBasicInformation(7)
            val grant = activate(app, open(app, IDENTITY, login = true))
            app.online.readBasicInformation(grant)
            val sample = app.online.offlineSamples
            val edit = sample.newCapture(fixture.attempt(task.taskId, 0), SampleInput.Qualitative(QualitativeJudgment.OK))
            val saved = sample.save(task, edit, IDENTITY)
            val batch = sample.prepareBatch(task, benchId(96), listOf(saved.envelope.operation.operationId))
            val local = app.online.offlineBasicInformation
            val cmd = local.newEdit(task, IDENTITY, BasicInformationField.Customer, "Independent optional field")
            assertNull(local.draft(task, IDENTITY).captureProblem)

            val source = requireNotNull(grant.source)
            val access = WorkAccess(grant.access.session, task, 7, grant.draft.attempts,
                setOf(ValidatedWorkSource(source.contextId, source.contributorUserId, source.captureUntilUtc)))
            app.store.startAttempt(access, batch.batchId)
            expectWork(WorkProblem.InvalidState) { local.save(cmd) }
            assertEquals(WorkProblem.InvalidState, local.draft(task, IDENTITY).captureProblem)
            app.store.recoverInterrupted(access)
            expectWork(WorkProblem.InvalidState) { local.save(cmd) }
            assertEquals(1L, local.draft(task, IDENTITY).localEditVersion)
            assertTrue(local.draft(task, IDENTITY).proposals.isEmpty())
        }
    }

    @Test fun logoutExpiryAndStickyRevocationPreserveOriginalsAndIsolateFamilies() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.workServices()
            val task = fixture.scope()
            val existing = accepted(task, BasicInformationField.Production, null, 6, 92)
            fixture.respondBasicInformation(7, existing)
            val grant = activate(app, open(app, IDENTITY, login = true))
            app.online.readBasicInformation(grant)
            val local = app.online.offlineBasicInformation
            val original = local.save(local.newEdit(task, IDENTITY, BasicInformationField.Customer, "A only"))
            val later = local.newEdit(task, IDENTITY, BasicInformationField.ProductMo, "A original source")
            val time = fixture.device.sample
            fixture.device.sample = time.copy(elapsedMs = time.elapsedMs + 61_000, utcMs = time.utcMs + 61_000)
            expectWork(WorkProblem.CaptureWindowClosed) { local.save(later) }
            assertFalse(local.draft(task, IDENTITY).proposals.single().sourceRevoked)
            assertEquals(original, local.save(original.command))

            app.store.recordSourceRevoked(grant.access, original.source.contextId)
            expectWork(WorkProblem.SourceUnavailable) { local.save(later) }
            val retained = local.draft(task, IDENTITY).proposals.single()
            assertTrue(retained.sourceRevoked)
            assertEquals(original, retained.original)
            assertEquals(original, local.save(original.command))
            app.sessions.logout(attemptRemote = false)
            val signedOut = runCatching { local.draft(task, IDENTITY) }.exceptionOrNull()
            assertEquals(Issue.UNAUTHENTICATED, (signedOut as? PatrolFailure)?.issue)
            fixture.device.sample = time.copy(boot = time.boot + 1)
            fixture.device.api.loginSubject = fixture.b
            val b = activate(app, open(app, fixture.b, login = true))
            app.online.readBasicInformation(b)
            assertTrue(local.draft(task, fixture.b).proposals.isEmpty())
            assertEquals(existing, local.draft(task, fixture.b).accepted?.field(BasicInformationField.Production)?.accepted)
            expectWork(WorkProblem.SourceUnavailable) { local.save(original.command) }
            val bSaved = local.save(local.newEdit(task, fixture.b, BasicInformationField.Customer, "B own"))
            assertEquals(fixture.b, bSaved.command.owner)
            assertNotEquals(original.source, bSaved.source)
        }
    }

    @Test fun acceptsAByBAndLeavesTheOtherVersionSevenFieldsImmutable() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.workServices()
            val task = fixture.scope()
            fixture.respondBasicInformation(7)
            val a = activate(app, open(app, IDENTITY, login = true))
            app.online.readBasicInformation(a)
            val local = app.online.offlineBasicInformation
            val originals = listOf(BasicInformationField.Customer to "Synthetic A", BasicInformationField.Production to "",
                BasicInformationField.ProductMo to "  ").map { (field, value) -> local.save(local.newEdit(task, IDENTITY, field, value)) }
            assertEquals(0, fixture.wire.headerPosts.get())

            app.sessions.logout(attemptRemote = false)
            fixture.device.api.loginSubject = fixture.b
            val b = open(app, fixture.b, login = true)
            val selected = app.online.basicInformationWorkbench(b).proposals.first { it.field == BasicInformationField.Customer }
            val original = originals.first()
            fixture.wire.headerSource = original.source
            fixture.wire.headerPost = { body ->
                val record = stored(fixture, app, original)
                assertEquals(WorkState.InFlight, record.state)
                val wire = body.getValue("operation").jsonObject
                assertEquals(JsonPrimitive(IDENTITY.userId), wire["contributorUserIdAssertion"])
                assertEquals(JsonPrimitive(original.source.contextId), wire["sourceWorkContextId"])
                assertEquals(JsonPrimitive(7), wire["baseDraftVersion"])
                val receipt = headerAccepted(original, record.batch, fixture.b)
                fixture.wire.sharedDraftVersionOverride = 8
                fixture.respondBasicInformation(8, receipt.operation)
                Response.success(headerUploadJson(receipt).jsonResponse())
            }
            val accepted = app.online.sendWorkbenchBasicInformation(b, selected) as BasicInformationUploadResult.Accepted
            assertEquals(fixture.b.familyId, accepted.receipt.operation.actorPersonalSessionId)
            assertEquals(IDENTITY.userId, accepted.receipt.operation.operation.contributorUserId)
            val next = open(app, fixture.b)
            val remaining = app.online.basicInformationWorkbench(next).proposals.filter { it.field != selected.field }
            assertEquals(2, remaining.size)
            assertTrue(remaining.all { it.stale && it.baseDraftVersion == 7L && !it.canUpload })
            assertEquals(listOf("", "  "), remaining.map { it.value })
            expectWork(WorkProblem.StaleGrant) { app.online.sendWorkbenchBasicInformation(next, remaining.first()) }
            assertEquals(1, fixture.wire.headerPosts.get())
            for (stored in originals) {
                assertEquals(stored, app.rows.transaction(task) { checkedBasicInformation(task, stored.reference().operationId) })
            }

            app.sessions.logout(attemptRemote = false)
            fixture.device.api.loginSubject = IDENTITY
            val aAgain = activate(app, open(app, IDENTITY, login = true))
            app.online.readBasicInformation(aAgain)
            assertEquals(originals[1], local.save(originals[1].command))
            val replacement = local.save(local.newEdit(task, IDENTITY, BasicInformationField.Production, "Explicit v8 proposal"))
            assertEquals(8L, replacement.command.operation.baseDraftVersion)
            assertNotEquals(originals[1].reference(), replacement.reference())
            assertEquals(1, fixture.wire.headerPosts.get())
        }
    }

    @Test fun admissionCommitFailureAndLostAcknowledgementBothPreventAnyBusinessPost() = runBlocking<Unit>(Dispatchers.IO) {
        for (point in listOf(Fault.BeforeCommit, Fault.AfterCommit)) {
            WorkbenchDeviceFixture().use { fixture ->
                val faults = CommitFault(fixture.device.rows)
                val app = fixture.workServices(rows = faults)
                val task = fixture.scope()
                fixture.respondBasicInformation(7)
                val grant = activate(app, open(app, IDENTITY, login = true))
                val local = app.online.offlineBasicInformation
                app.online.readBasicInformation(grant)
                val original = local.save(local.newEdit(task, IDENTITY, BasicInformationField.Customer, null))
                val row = app.online.basicInformationWorkbench(grant).proposals.single()
                fixture.wire.headerSource = original.source
                fixture.wire.beforeHeaderAdmission = { faults.point = point }
                expectWork(WorkProblem.StorageUnavailable) { app.online.sendWorkbenchBasicInformation(grant, row) }
                val record = stored(fixture, app, original)
                assertEquals(if (point == Fault.BeforeCommit) WorkState.Queued else WorkState.OutcomeUnknown, record.state)
                assertEquals(0, fixture.wire.headerPosts.get())
                assertEquals(original, local.save(original.command))
                if (point == Fault.AfterCommit) {
                    val reopened = open(app, IDENTITY)
                    expectWork(WorkProblem.InvalidState) { app.online.sendWorkbenchBasicInformation(reopened, row) }
                    assertEquals(0, fixture.wire.headerPosts.get())
                }
            }
        }
    }

    @Test fun unknownAndIndividualReceiptFenceOtherDomainsUntilExactV6SettlementAcrossRoomReopen() =
        runBlocking<Unit>(Dispatchers.IO) {
            WorkbenchDeviceFixture().use { fixture ->
                val faults = CommitFault(fixture.device.rows)
                val app = fixture.workServices(rows = faults)
                val task = fixture.scope()
                fixture.respondBasicInformation(7)
                val grant = activate(app, open(app, IDENTITY, login = true))
                app.online.readBasicInformation(grant)
                val local = app.online.offlineBasicInformation
                val original = local.save(local.newEdit(task, IDENTITY, BasicInformationField.Customer, "Durable synthetic"))
                val sample = app.online.offlineSamples
                val edit = sample.newCapture(fixture.attempt(task.taskId, 0), SampleInput.Qualitative(QualitativeJudgment.OK))
                val pendingSample = sample.save(task, edit, IDENTITY)
                val sampleBatch = sample.prepareBatch(task, benchId(98), listOf(pendingSample.envelope.operation.operationId))
                val removal = app.online.offlineItemRemovals
                val removed = removal.save(removal.newRemoval(task, IDENTITY, fixture.attempt(task.taskId, 1),
                    true, RemovalConfirmation.Production))
                val removalBatch = removal.prepareBatch(task, IDENTITY, benchId(99), removed.reference())
                val basis = app.online.readBasicInformation(grant).basis
                for (collision in listOf(sampleBatch.batchId, removalBatch.batchId)) {
                    expectWork(WorkProblem.BatchIdentityConflict) {
                        app.store.prepareBasicInformationUpload(grant.access, basis, collision, original.reference())
                    }
                }
                val row = app.online.basicInformationWorkbench(grant).proposals.single()
                fixture.wire.headerSource = original.source
                fixture.wire.headerPost = { throw IOException("Synthetic response loss after one POST") }
                assertTrue(app.online.sendWorkbenchBasicInformation(grant, row) is BasicInformationUploadResult.OutcomeUnknown)
                val record = stored(fixture, app, original)
                val full = headerAccepted(original, record.batch, IDENTITY)
                assertEquals(WorkState.OutcomeUnknown, record.state)
                expectWork(WorkProblem.InvalidState) { app.store.startAttempt(grant.access, sampleBatch.batchId) }
                expectWork(WorkProblem.InvalidState) {
                    app.store.startItemRemovalAttempt(grant.access, grant.draft.sampleBasis, removalBatch.batchId)
                }

                val batchLookup = ReceiptLookup(ReceiptKind.Batch, record.batch.batchId)
                val opLookup = ReceiptLookup(ReceiptKind.Operation, original.reference().operationId)
                fixture.wire.headerBatchLookup = { id ->
                    assertEquals(batchLookup.id, id)
                    Response.error(404, "{}".toResponseBody())
                }
                fixture.wire.headerOperationLookup = { id ->
                    assertEquals(opLookup.id, id)
                    Response.success(wireBasicInformationReceipt(full.operation).jsonResponse())
                }
                assertEquals(BasicInformationReceiptRecovery.Unresolved(ReceiptUncertainty.NotObserved),
                    app.online.recoverBasicInformationReceipt(grant, batchLookup))
                assertEquals(BasicInformationReceiptRecovery.OperationObserved(full.operation),
                    app.online.recoverBasicInformationReceipt(grant, opLookup))
                assertEquals(WorkState.OutcomeUnknown, stored(fixture, app, original).state)
                val newSample = sample.newCapture(fixture.attempt(task.taskId, 0), SampleInput.Qualitative(QualitativeJudgment.NG))
                expectWork(WorkProblem.StaleGrant) { sample.save(task, newSample, IDENTITY) }
                expectWork(WorkProblem.InvalidState) { app.store.startAttempt(grant.access, sampleBatch.batchId) }
                fixture.wire.headerBatchLookup = { id ->
                    assertEquals(batchLookup.id, id)
                    faults.point = Fault.AfterCommit
                    Response.success(headerBatchJson(full).jsonResponse())
                }
                expectWork(WorkProblem.StorageUnavailable) { app.online.recoverBasicInformationReceipt(grant, batchLookup) }
                assertEquals(1, fixture.wire.headerPosts.get())
                fixture.device.database.close()
                val database = TaskCacheDatabase.open(fixture.device.context, fixture.device.databaseFile)
                try {
                    val reopened = fixture.workServices(fixture.device.openCache(database), RoomCacheRows(database))
                    val previous = stored(fixture, reopened, original)
                    assertEquals(WorkState.Accepted, previous.state)
                    assertEquals(full, previous.batchReceipt)
                    assertEquals(full.operation, previous.individualReceipt)
                    assertEquals(8L, reopened.rows.transaction(task) { catalog().observedTaskDraftVersion })
                    assertEquals(original, reopened.online.offlineBasicInformation.save(original.command))
                } finally {
                    database.close()
                }
            }
        }

    private suspend fun stored(fixture: WorkbenchDeviceFixture, app: WorkbenchWorkServices, original: BasicInformationEnvelope) =
        requireNotNull(app.rows.transaction(fixture.scope()) { checkedBasicInformationTransport(original) })

    @Test fun validReplayPreservesOriginalActorAndOperationOnlyCannotClaimExactBatch() = runBlocking<Unit>(Dispatchers.IO) {
        for (mode in listOf("wrong fresh actor", "exact replay", "cross-batch replay")) {
            WorkbenchDeviceFixture().use { fixture ->
                val app = fixture.workServices()
                val task = fixture.scope()
                fixture.respondBasicInformation(7)
                val grant = activate(app, open(app, IDENTITY, login = true))
                app.online.readBasicInformation(grant)
                val local = app.online.offlineBasicInformation
                val original = local.save(local.newEdit(task, IDENTITY, BasicInformationField.Customer, "Original"))
                val row = app.online.basicInformationWorkbench(grant).proposals.single()
                fixture.wire.headerSource = original.source
                fixture.wire.headerPost = {
                    val record = stored(fixture, app, original)
                    val differentBatch = mode == "cross-batch replay"
                    val batch = if (differentBatch) BasicInformationBatch.seal(original, benchId(71)) else record.batch
                    val historical = headerAccepted(original, batch, fixture.b)
                    Response.success(headerUploadJson(historical, replay = mode != "wrong fresh actor",
                        batch = !differentBatch).jsonResponse())
                }
                val result = app.online.sendWorkbenchBasicInformation(grant, row)
                val record = stored(fixture, app, original)
                when (mode) {
                    "wrong fresh actor" -> {
                        assertTrue(result is BasicInformationUploadResult.OutcomeUnknown)
                        assertEquals(WorkState.OutcomeUnknown, record.state)
                        assertNull(record.confirmed)
                    }
                    "exact replay" -> {
                        assertTrue((result as BasicInformationUploadResult.Accepted).replayOnly)
                        assertEquals(WorkState.Accepted, record.state)
                        assertEquals(fixture.b.familyId, record.confirmed?.actorPersonalSessionId)
                    }
                    else -> {
                        assertTrue(result is BasicInformationUploadResult.NeedsBatchReceipt)
                        assertEquals(WorkState.OutcomeUnknown, record.state)
                        assertEquals(benchId(71), record.confirmed?.batchId)
                        assertNull(record.batchReceipt)
                    }
                }
                assertEquals(1, fixture.wire.headerPosts.get())
            }
        }
    }

    @Test fun sharedLeaseAndSourceAuthorityBlockCompetingOrObsoletePosts() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.workServices()
            val task = fixture.scope()
            fixture.respondBasicInformation(7)
            val grant = activate(app, open(app, IDENTITY, login = true))
            app.online.readBasicInformation(grant)
            val local = app.online.offlineBasicInformation
            val original = local.save(local.newEdit(task, IDENTITY, BasicInformationField.Customer, null))
            val second = OnlineWorkCoordinator(OfflineDeviceFixture.ORIGIN, app.patrol, fixture.wire, app.sessions, app.store)
            val otherGrant = second.open(grant.selectedTask, IDENTITY)
            val row = app.online.basicInformationWorkbench(grant).proposals.single()
            fixture.wire.headerSource = original.source
            val sent = CompletableDeferred<Unit>()
            val release = CompletableDeferred<Unit>()
            fixture.wire.headerPost = {
                sent.complete(Unit)
                release.await()
                throw IOException("Synthetic lost POST acknowledgement")
            }
            coroutineScope {
                val inFlight = async { app.online.sendWorkbenchBasicInformation(grant, row) }
                try {
                    withTimeout(5_000) { sent.await() }
                    expectWork(WorkProblem.InvalidState) { second.uploadBatch(otherGrant, benchId(76)) }
                    assertEquals(WorkState.InFlight, stored(fixture, app, original).state)
                } finally {
                    release.complete(Unit)
                }
                assertTrue(inFlight.await() is BasicInformationUploadResult.OutcomeUnknown)
            }
            val next = open(app, IDENTITY)
            val noSend = app.online.basicInformationWorkbench(next).proposals.single()
            assertFalse(noSend.canUpload)
            assertEquals(1, fixture.wire.headerPosts.get())
        }

        for (scenario in listOf("revoked", "selection", "logout", "rotate", "replace operator", "missing proof")) {
            WorkbenchDeviceFixture().use { fixture ->
                val app = fixture.workServices()
                val task = fixture.scope()
                fixture.respondBasicInformation(7)
                val grant = activate(app, open(app, IDENTITY, login = true))
                app.online.readBasicInformation(grant)
                val local = app.online.offlineBasicInformation
                val op = local.save(local.newEdit(task, IDENTITY, BasicInformationField.ProductMo, null))
                val row = app.online.basicInformationWorkbench(grant).proposals.single()
                fixture.wire.headerSource = op.source
                when (scenario) {
                    "revoked" -> {
                        fixture.wire.headerPreflight = Response.error(409, """{"status":409,"title":"SourceSecurityRevoked",
                            "code":"SourceSecurityRevoked","sourceContextIds":["${op.source.contextId}"]}"""
                            .toResponseBody("application/problem+json".toMediaType()))
                        assertEquals(BasicInformationUploadResult.Rejected(WorkConflict.SourceSecurityRevoked, submitted = false),
                            app.online.sendWorkbenchBasicInformation(grant, row))
                        assertEquals(WorkState.Quarantined, stored(fixture, app, op).state)
                    }
                    "selection" -> {
                        fixture.wire.beforeHeaderAdmission = { app.online.clearSelection() }
                        expectWork(WorkProblem.StaleGrant) { app.online.sendWorkbenchBasicInformation(grant, row) }
                        assertEquals(WorkState.Queued, stored(fixture, app, op).state)
                    }
                    "logout", "rotate", "replace operator" -> {
                        fixture.wire.beforeHeaderAdmission = {
                            if (scenario != "rotate") app.sessions.logout(attemptRemote = false)
                            if (scenario == "replace operator") fixture.device.api.loginSubject = fixture.b
                            if (scenario != "logout") app.sessions.login("synthetic-header-inspector", "test-only")
                        }
                        val failed = runCatching { app.online.sendWorkbenchBasicInformation(grant, row) }.exceptionOrNull()
                        assertEquals(Issue.UNAUTHENTICATED, (failed as? PatrolFailure)?.issue)
                        assertEquals(WorkState.Queued, stored(fixture, app, op).state)
                    }
                    else -> {
                        app.rows.transaction(task) { save(StoredWorkSource(op.source, "")) }
                        expectWork(WorkProblem.SourceUnavailable) { app.online.sendWorkbenchBasicInformation(grant, row) }
                    }
                }
                assertEquals(0, fixture.wire.headerPosts.get())
            }
        }
    }

    @Test fun previouslySelectedHeaderCannotPostOverAnUnresolvedSampleOrRemoval() = runBlocking<Unit>(Dispatchers.IO) {
        for (remove in listOf(false, true)) {
            WorkbenchDeviceFixture().use { fixture ->
                fixture.respondBasicInformation(7)
                val app = fixture.workServices()
                val task = fixture.scope()
                val grant = activate(app, open(app, IDENTITY, login = true))
                app.online.readBasicInformation(grant)
                val local = app.online.offlineBasicInformation
                val original = local.save(local.newEdit(task, IDENTITY, BasicInformationField.Customer, "Retain header"))
                val row = app.online.basicInformationWorkbench(grant).proposals.single()
                assertTrue(row.canUpload)
                fixture.wire.headerSource = original.source
                val source = original.source
                val access = WorkAccess(grant.access.session, task, 7, grant.draft.attempts,
                    setOf(ValidatedWorkSource(source.contextId, source.contributorUserId, source.captureUntilUtc)))
                if (remove) {
                    val coordinator = app.online.offlineItemRemovals
                    val pending = coordinator.save(coordinator.newRemoval(task, IDENTITY, fixture.attempt(task.taskId, 0),
                        true, RemovalConfirmation.Production))
                    val sealed = coordinator.prepareBatch(task, IDENTITY, benchId(99), pending.reference())
                    app.store.startItemRemovalAttempt(access, grant.draft.sampleBasis, sealed.batchId)
                } else {
                    val coordinator = app.online.offlineSamples
                    val command = coordinator.newCapture(fixture.attempt(task.taskId, 0), SampleInput.Qualitative(QualitativeJudgment.OK))
                    val pending = coordinator.save(task, command, IDENTITY)
                    val sealed = coordinator.prepareBatch(task, benchId(98), listOf(pending.envelope.operation.operationId))
                    app.store.startAttempt(access, sealed.batchId)
                }
                expectWork(WorkProblem.InvalidState) { app.online.sendWorkbenchBasicInformation(grant, row) }
                assertEquals(original, local.save(original.command))
                assertNull(app.rows.transaction(task) { checkedBasicInformationTransport(original) })
                assertEquals(0, fixture.wire.headerPosts.get())
            }
        }
    }

    private fun BasicInformationCommand.withId(id: String) = copy(operation = operation.copy(operationId = id))

    private suspend fun open(app: WorkbenchWorkServices, owner: SessionIdentity, login: Boolean = false): OnlineWorkCoordinator.Grant {
        if (login) app.sessions.login("synthetic-header-inspector", "test-only")
        val line = app.patrol.lines().single { it.id == LINE.id }
        return app.online.open(app.patrol.tasks(line, 1).items.single(), owner)
    }

    private suspend fun activate(app: WorkbenchWorkServices, grant: OnlineWorkCoordinator.Grant): OnlineWorkCoordinator.Grant {
        val sourced = app.online.issueOrReuse(grant)
        app.online.armOfflineSamples(sourced)
        return sourced
    }

    private suspend fun expectWork(problem: WorkProblem, action: suspend () -> Any?) {
        val failure = runCatching { action() }.exceptionOrNull()
        assertEquals(problem, (failure as? WorkFailure)?.problem)
    }

    private fun accepted(task: WorkTask, field: BasicInformationField, value: String?,
                         base: Long, id: Int): BasicInformationReceipt {
        val op = BasicInformationOperation(benchId(id), BasicInformationKind.CaptureBasicInformationField, field, value,
            benchId(id + 10), IDENTITY.userId, base, null)
        return BasicInformationReceipt(task, op, benchId(id + 20), workHash(BasicInformationCanonical.operation(task, op, 1)),
            benchId(71), benchId(72), 1, NOW, base + 1)
    }

    private enum class Fault { None, BeforeCommit, AfterCommit }

    private class CommitFault(private val rows: CacheRows) : CacheRows by rows {
        var point = Fault.None
        private var writes = 0

        override suspend fun replace(slot: String, sealed: ByteArray) {
            rows.replace(slot, sealed)
            writes++
            if (point == Fault.BeforeCommit) {
                point = Fault.None
                throw IOException("Synthetic rollback during protected write")
            }
        }

        override suspend fun <T> transaction(block: suspend () -> T): T {
            val before = writes
            val result = rows.transaction(block)
            if (point == Fault.AfterCommit && before != writes) {
                point = Fault.None
                throw IOException("Synthetic lost protected acknowledgement")
            }
            return result
        }
    }

    private companion object { const val ENCRYPTED_TEST_VALUE = "Synthetic exact sealed Customer \u00A0 " }
}
