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
import com.jaxpower.ipqc.patrol.offline.ClockSample
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
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
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

    @Test fun offlineSamplesAreSerializedAndSurviveRealRoomReopenWithOriginalEncryptedProvenance() = runBlocking<Unit>(Dispatchers.IO) {
        OfflineDeviceFixture(canContribute = true).use { device ->
            val (source, first) = armedSamples(device)
            val second = OfflineSampleCoordinator(ledger(device, device.sessions, device.rows))
            val calls = device.api.calls.get()
            val a = first.newCapture(attempt, good)
            val b = second.newCapture(attempt, bad)
            val both = listOf(async { first.save(source.task, a) }, async { second.save(source.task, b) }).awaitAll()
            assertEquals(setOf(1L, 2L), both.map { it.envelope.originLocalEditVersion }.toSet())
            assertEquals(2, both.map { it.envelope.operation.clientSampleId }.toSet().size)
            val duplicate = second.save(source.task, a)
            assertEquals(both.first().envelope.contentHash, duplicate.envelope.contentHash)
            expect(WorkProblem.OperationIdentityConflict) { second.save(source.task, a.copy(input = bad)) }

            val capture = first.newCapture(attempt, good)
            val captured = capture.operation(source.contextId, 7)
            val ref = OperationReference(captured.operationId, workHash(DraftOperationCanonical.operation(source, captured, 3)))
            val reached = CompletableDeferred<Unit>()
            val release = CompletableDeferred<Unit>()
            val hooked = object : CacheRows by device.rows {
                override suspend fun replace(slot: String, sealed: ByteArray) {
                    device.rows.replace(slot, sealed)
                    if (reached.complete(Unit)) release.await()
                }
            }
            val held = OfflineSampleCoordinator(ledger(device, device.sessions, hooked))
            val racedCapture = async { held.save(source.task, capture) }
            withTimeout(30_000) { reached.await() }
            val racedCorrection = async(start = CoroutineStart.UNDISPATCHED) {
                second.save(source.task, OfflineSampleEdit.Correct(capture.key, bad, ref))
            }
            assertFalse(racedCorrection.isCompleted)
            release.complete(Unit)
            assertEquals(ref, racedCapture.await().envelope.reference())
            assertEquals(4L, racedCorrection.await().envelope.originLocalEditVersion)

            val prior = both.first().envelope.reference()
            val corrected = OfflineSampleEdit.Correct(a.key, bad, prior)
            val removed = OfflineSampleEdit.Remove(a.key, prior)
            val competitors = listOf(
                async { runCatching { first.save(source.task, corrected) } },
                async { runCatching { second.save(source.task, removed) } },
            ).awaitAll()
            assertEquals(1, competitors.count { it.isSuccess })
            assertEquals(WorkProblem.DependencyMismatch,
                (competitors.single { it.isFailure }.exceptionOrNull() as WorkFailure).problem)
            assertEquals(5L, first.draft(source.task).localEditVersion)
            assertEquals(calls, device.api.calls.get())
            assertSealed(device.databaseFile)
            assertFalse(device.databaseFile.readBytes().toString(Charsets.ISO_8859_1).contains(a.operationId))

            device.database.close()
            withReopenedDatabase(device) { db ->
                val restart = SessionRepository(device.api, device.vault, ORIGIN, clock, device.openCache(db))
                val current = OfflineSampleCoordinator(ledger(device, restart, RoomCacheRows(db)))
                assertEquals(1, db.openHelper.readableDatabase.version)
                val row = requireNotNull(current.savedOperation(source.task, capture.operationId))
                assertEquals(source, row.envelope.source)
                assertEquals(ref, row.envelope.reference())
                assertEquals("NG", current.draft(source.task).effective.single { it.key == capture.key }.value?.judgment)
                device.sample = ClockSample(device.sample.boot + 1, 1_000, Instant.parse(NOW).toEpochMilli() + 9_000)
                current.save(source.task, current.newCapture(attempt, good))
                assertEquals(6L, current.draft(source.task).localEditVersion)
                assertEquals(calls, device.api.calls.get())
            }
        }
    }

    @Test fun RoomRollbackAndLostCommitAcknowledgementResolveByExactOperationId() = runBlocking<Unit>(Dispatchers.IO) {
        for (before in listOf(true, false)) {
            OfflineDeviceFixture(canContribute = true).use { device ->
                val (source, coordinator) = armedSamples(device)
                val command = coordinator.newCapture(attempt, good)
                // The first operation writes the registry marker, claim, envelope and catalog.
                val fault = CommitFault(device.rows, 4, before)
                val interrupted = OfflineSampleCoordinator(ledger(device, device.sessions, fault))
                expect(WorkProblem.StorageUnavailable) { interrupted.save(source.task, command) }
                device.database.close()
                withReopenedDatabase(device) { db ->
                    val restart = SessionRepository(device.api, device.vault, ORIGIN, clock, device.openCache(db))
                    val restored = OfflineSampleCoordinator(ledger(device, restart, RoomCacheRows(db)))
                    val durable = restored.savedOperation(source.task, command.operationId)
                    assertEquals(!before, durable != null)
                    val saved = restored.save(source.task, command)
                    assertEquals(1L, saved.envelope.originLocalEditVersion)
                    assertEquals(command.operationId, saved.envelope.operation.operationId)
                    if (!before) assertEquals(durable?.envelope?.contentHash, saved.envelope.contentHash)
                    assertEquals(1, restored.draft(source.task).history.size)
                }
            }
        }
    }

    @Test fun RoomCommitRechecksCaptureDeadlineA02ExpiryAndConcurrentOperatorTransitions() = runBlocking<Unit>(Dispatchers.IO) {
        for (case in listOf("capture", "receipt", "logout", "login")) {
            OfflineDeviceFixture(canContribute = true).use { device ->
                val expiry = if (case == "receipt") "2026-11-22T14:00:00Z" else CAPTURE
                val (source, ordinary) = armedSamples(device, CoordinatorApi(expiry))
                val edit = ordinary.newCapture(attempt, good)
                val initial = device.rows.count()
                val reachedWrite = CompletableDeferred<Unit>()
                val release = CompletableDeferred<Unit>()
                val hooked = object : CacheRows by device.rows {
                    override suspend fun replace(slot: String, sealed: ByteArray) {
                        device.rows.replace(slot, sealed)
                        if (reachedWrite.complete(Unit)) release.await()
                    }
                }
                val coordinator = OfflineSampleCoordinator(ledger(device, device.sessions, hooked))
                val saving = async { runCatching { coordinator.save(source.task, edit) } }
                withTimeout(30_000) { reachedWrite.await() }
                val ending = when (case) {
                    "logout" -> async(start = CoroutineStart.UNDISPATCHED) {
                        device.sessions.logout(attemptRemote = false)
                        Unit
                    }
                    "login" -> async(start = CoroutineStart.UNDISPATCHED) {
                        device.api.loginSubject = SessionIdentity(id(70), id(71), IDENTITY.environment)
                        device.sessions.login("second-fixture", "test-only")
                        Unit
                    }
                    else -> {
                        val delta = java.time.Duration.between(Instant.parse(NOW), Instant.parse(expiry)).toMillis()
                        device.sample = device.sample.copy(elapsedMs = device.sample.elapsedMs + delta,
                            utcMs = device.sample.utcMs + delta)
                        null
                    }
                }
                release.complete(Unit)
                val error = saving.await().exceptionOrNull()
                when (case) {
                    "capture" -> assertEquals(WorkProblem.CaptureWindowClosed, (error as WorkFailure).problem)
                    "receipt" -> assertEquals(Issue.OFFLINE_EXPIRED, (error as PatrolFailure).issue)
                    "logout", "login" -> assertEquals(Issue.UNAUTHENTICATED, (error as PatrolFailure).issue)
                }
                ending?.await()
                if (case == "login") assertEquals(device.api.loginSubject, device.cache.resume()?.identity)
                assertEquals(initial, device.rows.count())
                assertNull(ProtectedWorkRows(device.rows, ORIGIN, device.workKey).transaction(source.task) {
                    operation(edit.operationId)
                })
            }
        }
    }

    @Test fun missingWorkKeyAndCorruptedEncryptedOperationFailClosedWithoutDeletingRows() = runBlocking<Unit>(Dispatchers.IO) {
        for (missingKey in listOf(true, false)) {
            OfflineDeviceFixture(canContribute = true).use { device ->
                val (source, edits) = armedSamples(device)
                val edit = edits.newCapture(attempt, good)
                edits.save(source.task, edit)
                val count = device.rows.count()
                if (missingKey) device.workKey.removeKey()
                else {
                    val slot = device.rows.legacyWorkSlots().single { key ->
                        val sealed = checkNotNull(device.rows.read(key))
                        val clear = device.workKey.open(key, sealed)
                        try {
                            val json = clear.decodeToString()
                            json.contains("\"envelope\"") && json.contains(edit.operationId)
                        } finally {
                            clear.fill(0)
                        }
                    }
                    device.rows.replace(slot, byteArrayOf(42, 3, 7))
                }
                expect(WorkProblem.StorageUnavailable) { edits.savedOperation(source.task, edit.operationId) }
                expect(WorkProblem.StorageUnavailable) { edits.save(source.task, edits.newCapture(attempt, good)) }
                assertEquals(count, device.rows.count())
                assertNotNull(device.cache.resume(IDENTITY))
            }
        }
    }

    private suspend fun withReopenedDatabase(device: OfflineDeviceFixture, action: suspend (TaskCacheDatabase) -> Unit) {
        val database = TaskCacheDatabase.open(device.context, device.databaseFile)
        try {
            action(database)
        } finally {
            database.close()
        }
    }

    private suspend fun armedSamples(device: OfflineDeviceFixture, wire: CoordinatorApi = CoordinatorApi()):
        Pair<WorkSource, OfflineSampleCoordinator> {
        device.sessions.login("synthetic", "test-only")
        val store = ledger(device, device.sessions, device.rows)
        val online = OnlineWorkCoordinator(ORIGIN, device.patrol, wire, device.sessions, store)
        val grant = online.issueOrReuse(online.open(TASK))
        online.armOfflineSamples(grant)
        return requireNotNull(grant.source) to online.offlineSamples
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

    private class CoordinatorApi(private val captureUntil: String = CAPTURE) : WorkContextApi {
        var sampleReceipt: ServerBatchReceipt? = null
        var sampleUploads = 0
        var samplePreflights = 0
        var batchQueries = 0
        var beforeSampleUpload: suspend () -> Unit = {}

        override suspend fun uploadBasicInformationBatch(bearer: String, taskId: String, input: RequestBody,
                                                          guard: GuardedUploadExchange): Response<ResponseBody> =
            error("Unexpected Basic Information upload")

        override suspend fun basicInformationOperationReceipt(bearer: String, taskId: String, operationId: String):
            Response<ResponseBody> = error("Unexpected Basic Information receipt read")

        override suspend fun basicInformation(bearer: String, taskId: String, lineId: String,
                                             expectedDraftVersion: Long?, beforeDraftVersion: Long?,
                                             limit: Int): Response<ResponseBody> = error("Unexpected Basic Information read")

        override suspend fun uploadSampleBatch(bearer: String, taskId: String, input: RequestBody,
                                               guard: GuardedUploadExchange): Response<ResponseBody> {
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

        override suspend fun uploadItemRemovalBatch(bearer: String, taskId: String, input: RequestBody,
                                                    guard: GuardedUploadExchange): Response<ResponseBody> =
            error("Unexpected item removal upload")

        override suspend fun preflightUploadSources(bearer: String, taskId: String, input: WorkContextPreflightInputWire,
                                                  guard: GuardedUploadExchange): Response<ResponseBody> {
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
            return Response.success(201, WorkContextIssuedWire(metadata(captureUntil), proof, receipt(IDENTITY), false))
        }

        override suspend fun context(bearer: String, taskId: String, contextId: String): Response<WorkContextMetadataWire> {
            check(bearer == "Bearer fixture-access" && taskId == TASK.id && contextId == OnlineWorkStorageTest.contextId)
            metadataReads++
            return Response.success(metadata(captureUntil))
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
        val good = SampleInput.Qualitative(QualitativeJudgment.OK)
        val bad = SampleInput.Qualitative(QualitativeJudgment.NG)

        fun metadata(captureUntil: String) = WorkContextMetadataWire(contextId, 1, IDENTITY.environment, IDENTITY.userId,
            IDENTITY.familyId, 4, TASK.id, LINE.id, 2, NOW, captureUntil, FAMILY, revision = 1)

        fun receipt(identity: SessionIdentity) = OfflineAccessReceipt(1, identity.userId,
            identity.familyId, identity.environment, NOW, "2026-11-22T14:00:00Z", FAMILY)
    }
}
