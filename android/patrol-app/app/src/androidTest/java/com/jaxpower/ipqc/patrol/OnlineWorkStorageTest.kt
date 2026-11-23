package com.jaxpower.ipqc.patrol

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.IDENTITY
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.LINE
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.NOW
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.ORIGIN
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.TASK
import com.jaxpower.ipqc.patrol.api.*
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import com.jaxpower.ipqc.patrol.offline.CacheRows
import com.jaxpower.ipqc.patrol.offline.RoomCacheRows
import com.jaxpower.ipqc.patrol.offline.TaskCacheDatabase
import com.jaxpower.ipqc.patrol.offline.TaskSnapshots
import com.jaxpower.ipqc.patrol.tasks.PatrolRepository
import com.jaxpower.ipqc.patrol.work.*
import java.io.File
import java.time.Clock
import java.time.Instant
import java.time.ZoneOffset
import java.util.Base64
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody
import okhttp3.ResponseBody
import okhttp3.ResponseBody.Companion.toResponseBody
import okio.Buffer
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import retrofit2.Response

@RunWith(AndroidJUnit4::class)
class OnlineWorkStorageTest {
    @Test fun issuedProofIsSealedAcrossRoomReopenLogoutAndBPreflight() = runBlocking<Unit>(Dispatchers.IO) {
        OfflineDeviceFixture().use { device ->
            device.download()
            val workApi = CoordinatorApi()
            val oldSnapshot = device.snapshots.page(IDENTITY, LINE.id, 1)
            assertNotNull(oldSnapshot)
            val storeA = ledger(device, device.sessions, device.rows)
            val original = OnlineWorkCoordinator(ORIGIN, device.patrol, workApi, device.sessions, storeA)
            val issued = original.issueOrReuse(original.open(TASK))
            val operation = SampleOperation(id(3), attempt, id(4), requireNotNull(issued.source).contextId, 7,
                SampleKind.CaptureSample, SamplePayload(null, "1.25", "synthetic encrypted sample"), emptyList())
            val saved = storeA.enqueue(issued.access, operation).envelope
            assertSealed(device.databaseFile)
            device.database.close()

            val reopened = TaskCacheDatabase.open(device.context, device.databaseFile)
            try {
                assertEquals(1, reopened.openHelper.readableDatabase.version)
                val rows = RoomCacheRows(reopened)
                val repairedA = OnlineWorkCoordinator(ORIGIN, device.patrol, workApi, device.sessions,
                    ledger(device, device.sessions, rows))
                val reused = repairedA.issueOrReuse(repairedA.open(TASK))
                assertEquals(issued.source, reused.source)
                assertEquals(1, workApi.issues)
                assertEquals(1, workApi.metadataReads)

                device.sessions.logout(attemptRemote = false)
                assertNull(device.vault.read())
                val b = SessionIdentity(id(70), id(71), IDENTITY.environment)
                val apiB = BSessionApi(device.api, b)
                val offlineB = device.openCache(reopened)
                val sessions = SessionRepository(apiB, device.vault, ORIGIN,
                    Clock.fixed(Instant.parse(NOW), ZoneOffset.UTC), offlineB)
                sessions.login("second-fixture", "test-only")
                val storeB = ledger(device, sessions, rows)
                val coordinator = OnlineWorkCoordinator(ORIGIN, PatrolRepository(apiB, sessions, offlineB), workApi, sessions, storeB)
                val read = coordinator.open(TASK)
                assertNull(storeB.operation(read.access, operation.operationId))
                val authorized = coordinator.preflight(read, operation.operationId)
                assertEquals(1, workApi.preflights)
                val work = requireNotNull(storeB.operation(authorized.access, operation.operationId))
                assertEquals(WorkState.Queued, work.state)
                assertEquals(saved.contentHash, work.envelope.contentHash)
                assertEquals(IDENTITY, SessionIdentity(work.envelope.source.contributorUserId,
                    work.envelope.source.originPersonalSessionId, work.envelope.source.task.environmentId))
                assertEquals(oldSnapshot, TaskSnapshots(rows, device.workKey, ORIGIN).page(IDENTITY, LINE.id, 1))
                assertSealed(device.databaseFile)
            } finally {
                reopened.close()
            }
        }
    }

    @Test fun failedVaultCommitPreservesProtectedUnknownMarkerAcrossRoomReopen() = runBlocking<Unit>(Dispatchers.IO) {
        OfflineDeviceFixture().use { device ->
            device.download()
            val workApi = CoordinatorApi()
            val before = device.rows.count()
            val fault = object : CacheRows by device.rows {
                var writes = 0
                override suspend fun replace(slot: String, sealed: ByteArray) {
                    device.rows.replace(slot, sealed)
                    if (++writes == 3) error("Injected interruption after source and completion writes")
                }
            }
            val coordinator = OnlineWorkCoordinator(ORIGIN, device.patrol, workApi, device.sessions,
                ledger(device, device.sessions, fault))
            val selected = coordinator.open(TASK)
            expect(WorkProblem.StorageUnavailable) { coordinator.issueOrReuse(selected) }
            assertEquals(before + 1, device.rows.count())
            assertSealed(device.databaseFile)
            device.database.close()

            val reopened = TaskCacheDatabase.open(device.context, device.databaseFile)
            try {
                val rows = RoomCacheRows(reopened)
                val restored = OnlineWorkCoordinator(ORIGIN, device.patrol, workApi, device.sessions,
                    ledger(device, device.sessions, rows))
                val current = restored.open(TASK)
                expect(WorkProblem.IssuanceUncertain) { restored.issueOrReuse(current) }
                assertEquals(1, workApi.issues)
                assertEquals(before + 1, rows.count())
                assertNotNull(TaskSnapshots(rows, device.workKey, ORIGIN).page(IDENTITY, LINE.id, 1))
                assertSealed(device.databaseFile)
            } finally {
                reopened.close()
            }
        }
    }

    @Test fun noUploadBeforeRoomCommitOrWhenTheCommitResultIsLost() = runBlocking<Unit>(Dispatchers.IO) {
        for (beforeCommit in listOf(true, false)) {
            OfflineDeviceFixture().use { device ->
                val e = prepareUpload(device)
                val failing = CommitFault(device.rows, 1, beforeCommit)
                val coordinator = e.coordinator(failing)
                val grant = coordinator.open(TASK)
                expect(WorkProblem.StorageUnavailable) { coordinator.uploadBatch(grant, e.batch.batchId) }
                assertEquals(0, e.wire.sampleUploads)
                assertEquals(1, e.wire.samplePreflights)
                assertEquals(if (beforeCommit) WorkState.Queued else WorkState.OutcomeUnknown, e.state(device.rows))
                assertEquals(e.snapshot, device.snapshots.page(IDENTITY, LINE.id, 1))
                assertSealed(device.databaseFile)

                device.database.close()
                val reopened = TaskCacheDatabase.open(device.context, device.databaseFile)
                try {
                    val rows = RoomCacheRows(reopened)
                    assertEquals(if (beforeCommit) WorkState.Queued else WorkState.OutcomeUnknown, e.state(rows))
                    if (!beforeCommit) {
                        val repaired = e.coordinator(rows)
                        val current = repaired.open(TASK)
                        expect(WorkProblem.InvalidState) { repaired.uploadBatch(current, e.batch.batchId) }
                        assertEquals(0, e.wire.sampleUploads)
                    }
                } finally {
                    reopened.close()
                }
            }
        }
    }

    @Test fun serverAcceptanceSurvivesInterruptedRoomReceiptWritesAndReplaysByGet() = runBlocking<Unit>(Dispatchers.IO) {
        for (beforeCommit in listOf(true, false)) {
            OfflineDeviceFixture().use { device ->
                val e = prepareUpload(device)
                val failing = CommitFault(device.rows, 2, beforeCommit)
                e.wire.beforeSampleUpload = { assertEquals(WorkState.InFlight, e.state(device.rows)) }
                val coordinator = e.coordinator(failing)
                val grant = coordinator.open(TASK)
                expect(WorkProblem.StorageUnavailable) { coordinator.uploadBatch(grant, e.batch.batchId) }
                assertEquals(1, e.wire.sampleUploads)
                assertEquals(if (beforeCommit) WorkState.OutcomeUnknown else WorkState.Accepted, e.state(device.rows))

                device.database.close()
                val reopened = TaskCacheDatabase.open(device.context, device.databaseFile)
                try {
                    val rows = RoomCacheRows(reopened)
                    assertEquals(1, reopened.openHelper.readableDatabase.version)
                    val cache = device.openCache(reopened)
                    val restarted = SessionRepository(e.api, device.vault, ORIGIN, clock, cache)
                    assertNotNull(restarted.restore())
                    val store = ledger(device, restarted, rows)
                    val current = OnlineWorkCoordinator(ORIGIN, PatrolRepository(e.api, restarted, cache),
                        e.wire, restarted, store)
                    val renewed = current.open(TASK)
                    val confirmed = ReceiptRecovery.BatchConfirmed(e.receipt)
                    assertEquals(confirmed, current.recoverBatchReceipt(renewed, e.batch.batchId))
                    assertEquals(confirmed, current.recoverBatchReceipt(renewed, e.batch.batchId))
                    assertEquals(2, e.wire.batchQueries)
                    assertEquals(1, e.wire.sampleUploads)
                    val recovered = requireNotNull(store.operation(renewed.access, e.original.operation.operationId))
                    assertEquals(WorkState.Accepted, recovered.state)
                    assertEquals(e.original.contentHash, recovered.envelope.contentHash)
                    assertEquals(e.original.source, recovered.envelope.source)
                    assertEquals(e.receipt.uploadedPersonalSessionId, store.batch(renewed.access, e.batch.batchId)?.receipt?.uploadedPersonalSessionId)
                    assertEquals(e.snapshot, TaskSnapshots(rows, device.workKey, ORIGIN).page(IDENTITY, LINE.id, 1))
                    assertSealed(device.databaseFile)
                } finally {
                    reopened.close()
                }
            }
        }
    }

    private suspend fun prepareUpload(device: OfflineDeviceFixture): DeviceUpload {
        device.download()
        val snapshot = requireNotNull(device.snapshots.page(IDENTITY, LINE.id, 1))
        val wire = CoordinatorApi()
        val original = ledger(device, device.sessions, device.rows)
        val a = OnlineWorkCoordinator(ORIGIN, device.patrol, wire, device.sessions, original)
        val issued = a.issueOrReuse(a.open(TASK))
        val op = SampleOperation(id(3), attempt, id(4), requireNotNull(issued.source).contextId, 7,
            SampleKind.CaptureSample, SamplePayload("OK", null, null), emptyList())
        val saved = original.enqueue(issued.access, op).envelope
        val batch = original.prepareBatch(issued.access, id(10), listOf(op.operationId))
        device.sessions.logout(attemptRemote = false)
        assertNull(device.vault.read())
        val b = SessionIdentity(id(70), id(71), IDENTITY.environment)
        val api = BSessionApi(device.api, b)
        val sessions = SessionRepository(api, device.vault, ORIGIN, clock, device.cache)
        sessions.login("second-fixture", "test-only")
        val receipt = ServerBatchReceipt(TASK.id, batch.batchId, batch.fingerprint, b.userId, b.familyId, NOW, 7, 8,
            listOf(ServerOperationReceipt(TASK.id, op.operationId, batch.batchId, 1, saved.contentHash, op.kind,
                attempt.taskItemId, attempt.inspectionAttemptId, id(42), op.clientSampleId, op.sourceWorkContextId, IDENTITY.userId,
                b.userId, b.familyId, IDENTITY.environment, LINE.id, 2, NOW, 8,
                ServerSampleValue("Qualitative", null, "OK", null), false, op.operationId)))
        wire.sampleReceipt = receipt
        return DeviceUpload(device, api, sessions, wire, snapshot, saved, batch, receipt)
    }

    private class DeviceUpload(
        val device: OfflineDeviceFixture, val api: PatrolApi, val sessions: SessionRepository, val wire: CoordinatorApi,
        val snapshot: PagedLineTasks, val original: OperationEnvelope, val batch: BatchRecord, val receipt: ServerBatchReceipt,
    ) {
        fun coordinator(rows: CacheRows) = OnlineWorkCoordinator(ORIGIN, PatrolRepository(api, sessions, device.cache),
            wire, sessions, SharedWorkStore(ProtectedWorkRows(rows, ORIGIN, device.workKey), sessions))

        suspend fun state(rows: CacheRows) = ProtectedWorkRows(rows, ORIGIN, device.workKey).transaction(original.source.task) {
            batch(batch.batchId)?.state
        }
    }

    private class CommitFault(private val base: CacheRows, private val failOn: Int, private val before: Boolean) : CacheRows by base {
        private var writes = 0

        override suspend fun replace(slot: String, sealed: ByteArray) {
            base.replace(slot, sealed)
            if (++writes == failOn && before) error("Injected failure before Room commit")
        }

        override suspend fun <T> transaction(block: suspend () -> T): T {
            val old = writes
            val result = base.transaction(block)
            if (writes != old && writes == failOn && !before) error("Injected lost Room commit acknowledgement")
            return result
        }
    }

    private fun ledger(device: OfflineDeviceFixture, sessions: SessionRepository, rows: CacheRows) =
        SharedWorkStore(ProtectedWorkRows(rows, ORIGIN, device.workKey), sessions)

    private suspend fun expect(problem: WorkProblem, action: suspend () -> Any?) {
        try {
            action()
            fail("Expected " + problem.name)
        } catch (e: WorkFailure) {
            assertEquals(problem, e.problem)
        }
    }

    private fun assertSealed(db: File) {
        for (file in listOf(db, File(db.path + "-wal"), File(db.path + "-shm")).filter(File::exists)) {
            val bytes = file.readBytes().toString(Charsets.ISO_8859_1)
            assertFalse(bytes.contains(proof) || bytes.contains(contextId) ||
                bytes.contains("synthetic encrypted sample") || bytes.contains(IDENTITY.familyId))
        }
    }

    private class CoordinatorApi : WorkContextApi {
        var sampleReceipt: ServerBatchReceipt? = null
        var sampleUploads = 0
        var samplePreflights = 0
        var batchQueries = 0
        var beforeSampleUpload: suspend () -> Unit = {}

        override suspend fun uploadSampleBatch(bearer: String, taskId: String, input: RequestBody,
                                               guard: GuardedSampleExchange): Response<ResponseBody> {
            val receipt = requireNotNull(sampleReceipt)
            check(bearer == "Bearer fixture-device-b" && taskId == TASK.id && guard.maySend())
            beforeSampleUpload()
            sampleUploads++
            val body = Buffer().also(input::writeTo).readUtf8()
            check(body.contains(proof) && body.contains(receipt.batchId))
            val payload = Json.encodeToString(receipt)
            val operations = Json.encodeToString(receipt.operations)
            return json("""{"batchReceipt":$payload,"operations":$operations,"replayOnly":false,
                "canExecute":false,"acceptsFormalInspection":false}""")
        }

        override suspend fun preflightSampleBatch(bearer: String, taskId: String, input: WorkContextPreflightInputWire,
                                                  guard: GuardedSampleExchange): Response<ResponseBody> {
            check(guard.maySend() && sampleReceipt != null)
            samplePreflights++
            return json(Json.encodeToString(requireNotNull(preflight(bearer, taskId, input).body())))
        }

        override suspend fun batchReceipt(bearer: String, taskId: String, batchId: String): Response<ResponseBody> {
            val data = requireNotNull(sampleReceipt)
            check(bearer == "Bearer fixture-device-b" && taskId == TASK.id && batchId == data.batchId)
            batchQueries++
            return json(Json.encodeToString(data))
        }

        private fun json(value: String): Response<ResponseBody> =
            Response.success(value.toResponseBody("application/json".toMediaType()))

        override suspend fun operationReceipt(bearer: String, taskId: String, operationId: String): Nothing =
            error("Unexpected operation receipt GET")

        var issues = 0
        var metadataReads = 0
        var preflights = 0

        override suspend fun sharedDraft(bearer: String, taskId: String): Response<SharedDraftWire> {
            check(bearer in setOf("Bearer fixture-access", "Bearer fixture-device-b") && taskId == TASK.id)
            return Response.success(SharedDraftWire(TASK.id, LINE.id, 2, 7, "SharedDraftInitialized",
                listOf(DraftAttemptWire(attempt.taskItemId, attempt.inspectionAttemptId, 2)), emptyList(), false,
                "NotEvaluatedForFormalSubmission",
                items = listOf(DraftItemWire(attempt.taskItemId, attempt.inspectionAttemptId, 2, "Applicable")),
                allItemsRemoved = false))
        }

        override suspend fun issue(bearer: String, taskId: String, input: WorkContextIssueWire): Response<WorkContextIssuedWire> {
            check(bearer == "Bearer fixture-access" && taskId == TASK.id && input.expectedTaskLineId == LINE.id &&
                input.expectedWorkflowVersion == 2 && input.expectedEnvironmentId == IDENTITY.environment)
            issues++
            return Response.success(201, WorkContextIssuedWire(metadata(), proof, receipt(IDENTITY), false))
        }

        override suspend fun context(bearer: String, taskId: String, contextId: String): Response<WorkContextMetadataWire> {
            check(bearer == "Bearer fixture-access" && taskId == TASK.id && contextId == OnlineWorkStorageTest.contextId)
            metadataReads++
            return Response.success(metadata())
        }

        override suspend fun preflight(bearer: String, taskId: String, input: WorkContextPreflightInputWire): Response<WorkContextPreflightWire> {
            check(bearer == "Bearer fixture-device-b" && taskId == TASK.id && input.expectedTaskLineId == LINE.id &&
                input.expectedWorkflowVersion == 2 && input.expectedEnvironmentId == IDENTITY.environment)
            val source = input.sources.single()
            check(source.workContextId == contextId && source.contributorUserIdAssertion == IDENTITY.userId &&
                source.proofSecret == proof)
            preflights++
            return Response.success(WorkContextPreflightWire(NOW,
                listOf(WorkContextValidatedWire(contextId, IDENTITY.userId, CAPTURE)), "SourcePreflightOnly", false))
        }
    }

    private class BSessionApi(delegate: PatrolApi, private val b: SessionIdentity) : PatrolApi by delegate {
        override suspend fun login(input: NativeLogin): Response<NativeAuth> = auth("fixture-device-b-refresh")

        override suspend fun refresh(input: NativeRefresh): Response<NativeAuth> = auth("fixture-device-b-rotated")

        private fun auth(refresh: String): Response<NativeAuth> = Response.success(NativeAuth(
            "fixture-device-b", "2026-11-19T14:05:00Z",
            CurrentUser(b.userId, "synthetic", "Fixture B", "IPQA", true, false,
                listOf("LinePatrol.Read", "LinePatrol.ContributeDraft")),
            refresh, b.familyId, b.environment, FAMILY,
        ))

        override suspend fun verify(bearer: String): Response<OfflineAccessReceipt> = Response.success(receipt(b))

        override suspend fun detail(bearer: String, taskId: String, lineId: String): Response<LineTaskDetail> {
            check(bearer == "Bearer fixture-device-b" && taskId == TASK.id && lineId == LINE.id)
            val detail = OfflineDeviceFixture.DETAIL
            return Response.success(detail.copy(summary = detail.summary.copy(validatedAccess =
                ValidatedTaskAccess(b.userId, b.familyId, b.environment, NOW, TASK.id, LINE.id, 2))))
        }
    }

    private companion object {
        val clock: Clock = Clock.fixed(Instant.parse(NOW), ZoneOffset.UTC)
        const val FAMILY = "2026-11-26T14:00:00Z"
        const val CAPTURE = "2026-11-19T14:01:00Z"
        fun id(i: Int) = "50000000-0000-0000-0000-" + i.toString().padStart(12, '0')
        val contextId = id(8)
        val attempt = WorkAttempt(OfflineDeviceFixture.DETAIL.items.single().id, id(2))
        val proof = Base64.getUrlEncoder().withoutPadding().encodeToString(ByteArray(32) { 27 })

        fun metadata() = WorkContextMetadataWire(contextId, 1, IDENTITY.environment, IDENTITY.userId,
            IDENTITY.familyId, 4, TASK.id, LINE.id, 2, NOW, CAPTURE, FAMILY, revision = 1)

        fun receipt(identity: SessionIdentity) = OfflineAccessReceipt(1, identity.userId,
            identity.familyId, identity.environment, NOW, "2026-11-22T14:00:00Z", FAMILY)
    }
}
