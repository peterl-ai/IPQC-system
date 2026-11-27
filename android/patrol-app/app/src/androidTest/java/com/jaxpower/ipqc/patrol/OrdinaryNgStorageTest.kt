package com.jaxpower.ipqc.patrol

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.IDENTITY
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.LINE
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.NOW
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.SECOND_LINE
import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.api.ProductionLine
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.offline.CacheRows
import com.jaxpower.ipqc.patrol.offline.RoomCacheRows
import com.jaxpower.ipqc.patrol.offline.TaskCacheDatabase
import com.jaxpower.ipqc.patrol.work.*
import java.io.IOException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.*
import okhttp3.ResponseBody.Companion.toResponseBody
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import retrofit2.Response

@RunWith(AndroidJUnit4::class)
class OrdinaryNgStorageTest {
    private val blank = OrdinaryNgText(null, null, null)

    @Test fun authenticatedBrowseCreatesNoWorkLedgerAndLocalNgCannotAuthorizeDetails() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.workServices()
            val task = fixture.scope()
            val at = fixture.attempt(task.taskId, 0)
            fixture.answerNg(at, state = OrdinaryNgState.NotCurrentlyNG)
            val readOnly = open(app, login = true)
            val beforeBrowse = fixture.device.rows.legacyWorkSlots()
            assertEquals(OrdinaryNgState.NotCurrentlyNG, app.online.readOrdinaryNgDetails(readOnly).items.first().state)
            assertEquals(beforeBrowse, fixture.device.rows.legacyWorkSlots())
            assertNull(app.rows.transaction(task) { existingCatalog() })
            val grant = activate(app, readOnly)
            val samples = app.online.offlineSamples
            samples.save(task, samples.newCapture(at, SampleInput.Qualitative(QualitativeJudgment.NG)), IDENTITY)
            expectWork(WorkProblem.ScopeMismatch) { app.online.prepareOrdinaryNgCapture(grant, at) }
            expectWork(WorkProblem.SourceUnavailable) { app.online.offlineOrdinaryNgDetails.newEdit(task, IDENTITY, at, blank) }
            assertEquals(1L, localVersion(app, task))
            assertEquals(1, fixture.wire.ngReads.get())
            assertEquals(0, fixture.wire.sends.get())
        }
    }

    @Test fun roomRollbackAndLostAcknowledgementPreserveOneExactSetAcrossReopen() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val faults = CommitFault(fixture.device.rows)
            val app = fixture.workServices(rows = faults)
            val task = fixture.scope()
            val at = fixture.attempt(task.taskId, 0)
            val sample = fixture.answerNg(at)
            val grant = activate(app, open(app, login = true))
            app.online.prepareOrdinaryNgCapture(grant, at)
            val local = app.online.offlineOrdinaryNgDetails
            val text = OrdinaryNgText("Synthetic exact metadata", "", " \u00a0 ")
            val command = local.newEdit(task, IDENTITY, at, text)
            val prior = fixture.device.rows.count()
            faults.mode = Fault.Rollback
            expectWork(WorkProblem.StorageUnavailable) { local.save(command) }
            assertEquals(prior, fixture.device.rows.count())
            assertTrue(local.draft(task, IDENTITY, at).proposals.isEmpty())
            assertEquals(0L, localVersion(app, task))
            faults.mode = Fault.LostAcknowledgement
            expectWork(WorkProblem.StorageUnavailable) { local.save(command) }
            val original = local.draft(task, IDENTITY, at).proposals.single()
            assertEquals(command, original.command)
            assertEquals(text, original.command.operation.text)
            assertEquals(listOf(sample), original.currentNgReferences)
            assertEquals(1L, original.originLocalEditVersion)
            assertFalse(original.canonicalPayload.contains("acceptedNgBasis"))
            assertEquals(1, fixture.wire.ngReads.get())

            fixture.device.database.close()
            val reopenedDb = TaskCacheDatabase.open(fixture.device.context, fixture.device.databaseFile)
            try {
                val reopened = fixture.workServices(fixture.device.openCache(reopenedDb), RoomCacheRows(reopenedDb))
                val ledger = reopened.online.offlineOrdinaryNgDetails
                assertEquals(original, ledger.save(command))
                expectWork(WorkProblem.OperationIdentityConflict) { ledger.save(command.copy(operation = command.operation.copy(text = blank))) }
                assertEquals(listOf(original), ledger.draft(task, IDENTITY, at).proposals)
                assertEquals(1L, localVersion(reopened, task))
            } finally {
                reopenedDb.close()
            }
            val db = fixture.device.databaseFile
            db.parentFile!!.listFiles().orEmpty().filter { it.name.startsWith(db.name) }.forEach {
                assertFalse(it.readBytes().toString(Charsets.ISO_8859_1).contains("Synthetic exact metadata"))
            }
        }
    }

    @Test fun correctionUsesLatestHistoricalBasisAndPreservesBothPersonalActors() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.workServices()
            val task = fixture.scope()
            val at = fixture.attempt(task.taskId, 0)
            val oldSample = fixture.answerNg(at)
            val aGrant = activate(app, open(app, login = true))
            val local = app.online.offlineOrdinaryNgDetails
            app.online.prepareOrdinaryNgCapture(aGrant, at)
            val original = local.save(local.newEdit(task, IDENTITY, at, blank))
            assertEquals(OrdinaryNgKind.SetOrdinaryNgDetails, original.command.operation.kind)
            val serverAccepted = OrdinaryNgReceipt(task, original.command.operation, benchId(177), original.contentHash,
                fixture.b.userId, fixture.b.familyId, original.originLocalEditVersion, NOW, 8, listOf(oldSample))
            val current = fixture.answerNg(at, version = 9, valueOperationId = benchId(141), prior = serverAccepted)
            app.sessions.logout()
            fixture.device.api.loginSubject = fixture.b
            val bGrant = activate(app, open(app, fixture.b, true))
            val read = app.online.readOrdinaryNgDetails(bGrant)
            val latest = requireNotNull(read.items.first().latest)
            assertEquals(serverAccepted, latest.receipt)
            assertTrue(latest.latestForAttempt)
            assertFalse(latest.matchesAcceptedNgBasis)
            assertFalse(latest.currentlyApplicable)
            app.online.prepareOrdinaryNgCapture(bGrant, at)
            assertTrue(local.draft(task, fixture.b, at).proposals.isEmpty())
            expectWork(WorkProblem.SourceUnavailable) { local.save(original.command) }
            expectWork(WorkProblem.DependencyMismatch) { local.newEdit(task, fixture.b, at, blank) }
            expectWork(WorkProblem.DependencyMismatch) {
                local.newEdit(task, fixture.b, at, blank, OperationReference(serverAccepted.operation.operationId, "D".repeat(64)))
            }
            val b = local.save(local.newEdit(task, fixture.b, at, OrdinaryNgText(null, "", " "), serverAccepted.reference()))
            assertEquals(OrdinaryNgKind.CorrectOrdinaryNgDetails, b.command.operation.kind)
            assertEquals(fixture.b.userId, b.source.contributorUserId)
            assertEquals(fixture.b.familyId, b.source.originPersonalSessionId)
            assertEquals(listOf(current), b.currentNgReferences)
            assertEquals(2L, b.originLocalEditVersion)
            assertEquals(listOf(serverAccepted), local.draft(task, fixture.b, at).accepted)
            assertEquals(original, app.rows.transaction(task) { checkedOrdinaryNg(task, original.command.operation.operationId) })
        }
    }

    @Test fun remoteDispositionAndObsoleteAttemptKeepAcceptedHistoryAndFenceOriginalCommands() = runBlocking<Unit>(Dispatchers.IO) {
        for (state in listOf(OrdinaryNgState.Removed, OrdinaryNgState.UnableToMeasureNG, OrdinaryNgState.NotCurrentlyNG,
            OrdinaryNgState.AttemptNotCurrent, OrdinaryNgState.CurrentScopeUnavailable)) {
            WorkbenchDeviceFixture().use { fixture ->
                val app = fixture.workServices()
                val task = fixture.scope()
                val at = fixture.attempt(task.taskId, 0)
                val sample = fixture.answerNg(at)
                val accepted = accepted(task, at, sample, 6)
                fixture.answerNg(at, prior = accepted)
                val grant = activate(app, open(app, login = true))
                val local = app.online.offlineOrdinaryNgDetails
                app.online.prepareOrdinaryNgCapture(grant, at)
                val original = local.save(local.newEdit(task, IDENTITY, at, blank, accepted.reference()))
                fixture.answerNg(at, version = 8, prior = accepted, state = state)
                val newer = open(app)
                val read = app.online.readOrdinaryNgDetails(newer)
                val historical = read.history.single()
                assertEquals(state, historical.state)
                assertFalse(historical.currentlyApplicable)
                expectWork(WorkProblem.ScopeMismatch) { app.online.prepareOrdinaryNgCapture(newer, at) }
                val uncommitted = original.command.copy(operation = original.command.operation.copy(operationId = benchId(155)))
                expectWork(WorkProblem.StaleGrant) { local.save(uncommitted) }
                assertEquals(original, local.save(original.command))
                assertEquals(listOf(accepted), local.draft(task, IDENTITY, at).accepted)
                assertEquals(1L, localVersion(app, task))
            }
        }
    }

    @Test fun pinnedOrdinaryConflictFencesAllEarlierDomainsAndKeepsQueuedFacts() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.workServices()
            val task = fixture.scope()
            val at = fixture.attempt(task.taskId, 0)
            fixture.answerNg(at)
            fixture.respondBasicInformation(7)
            val grant = activate(app, open(app, login = true))
            app.online.readBasicInformation(grant)
            app.online.prepareOrdinaryNgCapture(grant, at)
            val ng = app.online.offlineOrdinaryNgDetails
            val pending = ng.newEdit(task, IDENTITY, at, blank)
            val samples = app.online.offlineSamples
            val sample = samples.newCapture(at, SampleInput.Qualitative(QualitativeJudgment.OK))
            val removals = app.online.offlineItemRemovals
            val removal = removals.newRemoval(task, IDENTITY, at, true, RemovalConfirmation.Production)
            val headers = app.online.offlineBasicInformation
            val header = headers.newEdit(task, IDENTITY, BasicInformationField.Customer, null)
            fixture.wire.ngReply = { _, _, _, _, _, _, _ ->
                Response.error(409, """{"status":409,"title":"DraftVersionConflict","code":"DraftVersionConflict"}""".toResponseBody())
            }
            expectIssue(Issue.CONFLICT) { app.online.readOrdinaryNgDetails(grant) }
            assertEquals(2, fixture.wire.ngReads.get())
            assertEquals(7L, app.rows.transaction(task) { catalog().staleThroughDraftVersion })
            expectWork(WorkProblem.StaleGrant) { ng.save(pending) }
            expectWork(WorkProblem.StaleGrant) { samples.save(task, sample, IDENTITY) }
            expectWork(WorkProblem.StaleGrant) { removals.save(removal) }
            expectWork(WorkProblem.StaleGrant) { headers.save(header) }
            assertEquals(0L, localVersion(app, task))

            fixture.answerNg(at, version = 8)
            val higher = open(app)
            assertEquals(8L, app.online.readOrdinaryNgDetails(higher).draftVersion)
            assertEquals(8L, app.rows.transaction(task) { catalog().observedTaskDraftVersion })
            expectWork(WorkProblem.StaleGrant) { ng.save(pending) }
            app.online.prepareOrdinaryNgCapture(higher, at)
            assertEquals(8L, ng.save(ng.newEdit(task, IDENTITY, at, blank)).command.operation.baseDraftVersion)
            assertEquals(1L, localVersion(app, task))
        }
    }

    @Test fun newerOrdinaryReadAdvancesTheProtectedVersionWithoutAnotherTypedDomain() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.workServices()
            val task = fixture.scope()
            val at = fixture.attempt(task.taskId, 0)
            fixture.answerNg(at)
            val grant = activate(app, open(app, login = true))
            app.online.prepareOrdinaryNgCapture(grant, at)
            val old = app.online.offlineOrdinaryNgDetails.newEdit(task, IDENTITY, at, blank)
            val sample = app.online.offlineSamples.newCapture(at, SampleInput.Qualitative(QualitativeJudgment.OK))
            assertEquals(7L, app.rows.transaction(task) { catalog().observedTaskDraftVersion })
            app.sessions.logout()
            fixture.device.api.loginSubject = fixture.b
            fixture.answerNg(at, version = 8)
            val browse = open(app, fixture.b, true)
            assertEquals(7L, app.rows.transaction(task) { catalog().observedTaskDraftVersion })
            assertEquals(8L, app.online.readOrdinaryNgDetails(browse).draftVersion)
            assertEquals(8L, app.rows.transaction(task) { catalog().observedTaskDraftVersion })
            app.sessions.logout()
            fixture.device.api.loginSubject = IDENTITY
            app.sessions.login("synthetic-ordinary-inspector", "test-only")
            expectWork(WorkProblem.StaleGrant) { app.online.offlineOrdinaryNgDetails.save(old) }
            expectWork(WorkProblem.StaleGrant) { app.online.offlineSamples.save(task, sample, IDENTITY) }
            assertEquals(0L, localVersion(app, task))
        }
    }

    @Test fun unresolvedSampleRemovalAndHeaderBatchesBlockNewNgCapture() = runBlocking<Unit>(Dispatchers.IO) {
        for (domain in listOf("sample", "removal", "header")) {
            WorkbenchDeviceFixture().use { fixture ->
                val app = fixture.workServices()
                val task = fixture.scope()
                val at = fixture.attempt(task.taskId, 0)
                fixture.answerNg(at)
                fixture.respondBasicInformation(7)
                val grant = activate(app, open(app, login = true))
                app.online.readBasicInformation(grant)
                app.online.prepareOrdinaryNgCapture(grant, at)
                val ng = app.online.offlineOrdinaryNgDetails
                val command = ng.newEdit(task, IDENTITY, at, blank)
                val source = requireNotNull(grant.source)
                val access = WorkAccess(grant.access.session, task, 7, grant.draft.attempts,
                    setOf(ValidatedWorkSource(source.contextId, source.contributorUserId, source.captureUntilUtc)))
                when (domain) {
                    "sample" -> {
                        val c = app.online.offlineSamples
                        val edit = c.save(task, c.newCapture(at, SampleInput.Qualitative(QualitativeJudgment.OK)), IDENTITY)
                        val batch = c.prepareBatch(task, benchId(169), listOf(edit.envelope.operation.operationId))
                        app.store.startAttempt(access, batch.batchId)
                    }
                    "removal" -> {
                        val c = app.online.offlineItemRemovals
                        val edit = c.save(c.newRemoval(task, IDENTITY, at, true, RemovalConfirmation.Production))
                        val batch = c.prepareBatch(task, IDENTITY, benchId(169), edit.reference())
                        app.store.startItemRemovalAttempt(access, grant.draft.sampleBasis, batch.batchId)
                    }
                    else -> {
                        val c = app.online.offlineBasicInformation
                        val edit = c.save(c.newEdit(task, IDENTITY, BasicInformationField.Customer, null))
                        app.rows.transaction(task) {
                            saveBasicInformationTransport(BasicInformationTransport(BasicInformationBatch.seal(edit, benchId(169)),
                                state = WorkState.OutcomeUnknown))
                        }
                    }
                }
                expectWork(WorkProblem.InvalidState) { ng.save(command) }
                expectWork(WorkProblem.InvalidState) { ng.newEdit(task, IDENTITY, at, blank) }
                assertEquals(1L, localVersion(app, task))
                assertTrue(ng.draft(task, IDENTITY, at).proposals.isEmpty())
                assertEquals(0, fixture.wire.sends.get() + fixture.wire.headerPosts.get() + fixture.wire.removalPosts.get())
            }
        }
    }

    @Test fun localOperationNamespaceAndExpiredRevokedOrReplacedAuthorityStaySeparate() = runBlocking<Unit>(Dispatchers.IO) {
        WorkbenchDeviceFixture().use { fixture ->
            val app = fixture.workServices()
            val task = fixture.scope()
            val at = fixture.attempt(task.taskId, 0)
            fixture.answerNg(at)
            fixture.respondBasicInformation(7)
            val grant = activate(app, open(app, login = true))
            app.online.readBasicInformation(grant)
            app.online.prepareOrdinaryNgCapture(grant, at)
            val ng = app.online.offlineOrdinaryNgDetails
            val pending = ng.newEdit(task, IDENTITY, at, blank)
            val samples = app.online.offlineSamples
            val sample = samples.newCapture(at, SampleInput.Qualitative(QualitativeJudgment.OK))
            val originalSample = samples.save(task, sample, IDENTITY)
            val headers = app.online.offlineBasicInformation
            val header = headers.save(headers.newEdit(task, IDENTITY, BasicInformationField.Customer, null))
            val removals = app.online.offlineItemRemovals
            val removedAt = fixture.attempt(task.taskId, 1)
            val removal = removals.save(removals.newRemoval(task, IDENTITY, removedAt, true, RemovalConfirmation.Production))
            for (id in listOf(originalSample.envelope.operation.operationId, header.command.operation.operationId, removal.command.operationId)) {
                expectWork(WorkProblem.OperationIdentityConflict) { ng.save(pending.copy(operation = pending.operation.copy(operationId = id))) }
            }
            val original = ng.save(pending)
            expectWork(WorkProblem.OperationIdentityConflict) {
                samples.save(task, sample.copy(operationId = original.command.operation.operationId,
                    key = sample.key.copy(clientSampleId = benchId(152))), IDENTITY)
            }
            val second = fixture.scope(SECOND_LINE)
            val secondAt = fixture.attempt(second.taskId, 0)
            activate(app, open(app, line = SECOND_LINE))
            expectWork(WorkProblem.SourceUnavailable) {
                ng.save(original.command.copy(task = second, operation = original.command.operation.copy(attempt = secondAt)))
            }
            val time = fixture.device.sample
            fixture.device.sample = time.copy(elapsedMs = time.elapsedMs + 61_000, utcMs = time.utcMs + 61_000)
            assertEquals(WorkProblem.CaptureWindowClosed, ng.draft(task, IDENTITY, at).captureProblem)
            assertEquals(original, ng.save(pending))
            app.rows.transaction(task) { rememberSourceRevoked(original.source.contextId) }
            expectWork(WorkProblem.SourceUnavailable) { ng.newEdit(task, IDENTITY, at, blank) }
            assertEquals(original, ng.save(pending))
            fixture.device.sample = fixture.device.sample.copy(boot = -1)
            expectIssue(Issue.OFFLINE_CLOCK) { ng.save(pending) }
            assertEquals(4L, localVersion(app, task))
        }
    }

    private suspend fun open(app: WorkbenchWorkServices, owner: SessionIdentity = IDENTITY, login: Boolean = false,
                             line: ProductionLine = LINE): OnlineWorkCoordinator.Grant {
        if (login) app.sessions.login("synthetic-ordinary-inspector", "test-only")
        val selected = app.patrol.lines().single { it.id == line.id }
        return app.online.open(app.patrol.tasks(selected, 1).items.single(), owner)
    }

    private suspend fun activate(app: WorkbenchWorkServices, grant: OnlineWorkCoordinator.Grant): OnlineWorkCoordinator.Grant {
        val sourced = app.online.issueOrReuse(grant)
        app.online.armOfflineSamples(sourced)
        return sourced
    }

    private suspend fun localVersion(app: WorkbenchWorkServices, task: WorkTask) =
        requireNotNull(app.rows.transaction(task) { catalog().localEditVersion })

    private suspend fun expectWork(problem: WorkProblem, run: suspend () -> Any?) =
        assertEquals(problem, (runCatching { run() }.exceptionOrNull() as? WorkFailure)?.problem)

    private suspend fun expectIssue(issue: Issue, run: suspend () -> Any?) =
        assertEquals(issue, (runCatching { run() }.exceptionOrNull() as? PatrolFailure)?.issue)

    private fun accepted(task: WorkTask, at: WorkAttempt, ref: OperationReference, version: Long): OrdinaryNgReceipt {
        val op = OrdinaryNgOperation(benchId(171), OrdinaryNgKind.SetOrdinaryNgDetails, at,
            benchId(151), IDENTITY.userId, version - 1, OrdinaryNgText("Earlier NG", null, ""), null)
        return OrdinaryNgReceipt(task, op, benchId(172), workHash(OrdinaryNgCanonical.operation(task, op, 3)),
            IDENTITY.userId, IDENTITY.familyId, 3, NOW, version, listOf(ref))
    }

    private enum class Fault { None, Rollback, LostAcknowledgement }
    private class CommitFault(private val base: CacheRows) : CacheRows by base {
        var mode = Fault.None
        private var writes = 0
        override suspend fun replace(slot: String, sealed: ByteArray) {
            base.replace(slot, sealed)
            writes++
            if (mode == Fault.Rollback) {
                mode = Fault.None
                throw IOException("Synthetic rollback")
            }
        }
        override suspend fun <T> transaction(block: suspend () -> T): T {
            val before = writes
            val result = base.transaction(block)
            if (writes > before && mode == Fault.LostAcknowledgement) {
                mode = Fault.None
                throw IOException("Synthetic lost acknowledgement")
            }
            return result
        }
    }
}
