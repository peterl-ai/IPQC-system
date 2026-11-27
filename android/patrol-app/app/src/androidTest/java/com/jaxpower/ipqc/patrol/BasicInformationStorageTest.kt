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
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
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
            fixture.respond(7)
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
            fixture.respond(7, customer, production)
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
            fixture.respond(7)
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
            fixture.respond(8, confirmed)
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
            fixture.respond(9, confirmed, contradictory)
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
            fixture.respond(7)
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
            fixture.respond(7)
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
            fixture.respond(7, existing)
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

    private fun WorkbenchDeviceFixture.respond(version: Long, vararg accepted: BasicInformationReceipt) {
        val task = scope()
        val facts = BasicInformationField.entries.map { name -> BasicInformationFact(name, accepted.singleOrNull { it.operation.field == name }) }
        val raw = buildJsonObject {
            put("taskId", task.taskId); put("lineId", task.lineId); put("workflowVersion", task.workflowVersion)
            put("draftVersion", version); put("availability", "DraftFoundation")
            put("values", buildJsonObject { facts.forEach { put(it.field.name.replaceFirstChar(Char::lowercaseChar), it.value) } })
            put("fields", JsonArray(facts.map {
                buildJsonObject {
                    put("field", it.field.name); put("value", it.value); put("valueState", it.presence.name)
                    put("hasAcceptedFact", it.accepted != null); put("lastOperation", it.accepted?.let(::wireReceipt) ?: JsonNull)
                    put("isRequired", false); put("blocksSubmit", false)
                }
            }))
            put("acceptedFieldCount", accepted.size)
            put("nonblankFieldCount", facts.count { it.presence == BasicInformationPresence.Provided })
            put("history", JsonArray(accepted.sortedByDescending { it.draftVersion }.map(::wireReceipt)))
            put("historyBeforeDraftVersion", JsonNull)
            put("canExecute", false); put("canSubmit", false); put("acceptsFormalInspection", false)
            put("completeness", "OptionalHeaderFactsOnly"); put("observation", "ReadOnlyPointInTime")
        }.toString()
        wire.headerReply = { expected, before, limit ->
            check((expected == null || expected == version) && before == null && limit == 20)
            Response.success(raw.toResponseBody())
        }
    }

    private fun wireReceipt(r: BasicInformationReceipt) = buildJsonObject {
        val op = r.operation
        put("taskId", r.task.taskId); put("operationId", op.operationId); put("batchId", r.batchId)
        put("canonicalizationVersion", 1); put("contentHash", r.contentHash); put("kind", op.kind.name)
        put("field", op.field.name); put("value", op.value); put("sourceWorkContextId", op.sourceWorkContextId)
        put("contributorUserId", op.contributorUserId); put("actorUserId", r.actorUserId)
        put("actorPersonalSessionId", r.actorPersonalSessionId); put("environmentId", r.task.environmentId)
        put("lineId", r.task.lineId); put("workflowVersion", r.task.workflowVersion); put("baseDraftVersion", op.baseDraftVersion)
        put("originLocalEditVersion", r.originLocalEditVersion); put("acceptedAtUtc", r.acceptedAtUtc)
        put("draftVersion", r.draftVersion); put("predecessor", JsonNull)
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
