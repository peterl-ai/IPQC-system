package com.jaxpower.ipqc.patrol

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.IDENTITY
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.LINE
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.NOW
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.ORIGIN
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.SECOND_LINE
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.TASK
import com.jaxpower.ipqc.patrol.api.*
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import com.jaxpower.ipqc.patrol.offline.CacheRows
import com.jaxpower.ipqc.patrol.offline.RoomCacheRows
import com.jaxpower.ipqc.patrol.offline.TaskCacheDatabase
import com.jaxpower.ipqc.patrol.offline.TaskSnapshots
import com.jaxpower.ipqc.patrol.work.*
import java.io.File
import java.time.Clock
import java.time.Instant
import java.time.ZoneOffset
import java.util.Base64
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import retrofit2.Response

@RunWith(AndroidJUnit4::class)
class SharedWorkStorageTest {
    private val task = WorkTask(IDENTITY.environment, TASK.id, LINE.id, 2)
    private val attempt = WorkAttempt(id(4), id(5))
    private val source = WorkSource(task, id(7), IDENTITY.userId, IDENTITY.familyId,
        NOW, "2026-11-22T14:00:00Z", "2026-11-26T14:00:00Z", 1)
    private val input = SampleOperation(id(10), attempt, id(6), source.contextId, 5, SampleKind.CaptureSample,
        SamplePayload(null, "1.23", "Synthetic encrypted barcode"), emptyList())
    private val proof = Base64.getUrlEncoder().withoutPadding().encodeToString(ByteArray(32) { 89 })
    private val clock = Clock.fixed(Instant.parse(NOW), ZoneOffset.UTC)

    @Test fun encryptedV1CacheAndWorkSurviveExpiryLogoutAccountSwitchAndRepositoryReopen() = runBlocking<Unit>(Dispatchers.IO) {
        OfflineDeviceFixture().use { device ->
            device.download()
            val keeper = store(device, device.sessions)
            val a = access(device.sessions, true)
            val a02 = device.snapshots.page(IDENTITY, LINE.id, 1)
            assertNotNull(a02)
            keeper.storeSource(a, source, proof)
            val envelope = keeper.enqueue(a, input).envelope
            val batch = keeper.prepareBatch(a, id(11), listOf(input.operationId))
            keeper.startAttempt(a, batch.batchId)
            val second = input.copy(operationId = id(12), clientSampleId = id(13))
            keeper.enqueue(a, second)
            assertEncrypted(device.databaseFile)

            val elapsed = 72 * 3600 * 1000L
            device.sample = device.sample.copy(elapsedMs = device.sample.elapsedMs + elapsed, utcMs = device.sample.utcMs + elapsed)
            try {
                device.cache.resume()
                fail("Expected the ordinary A02 receipt to expire")
            } catch (e: PatrolFailure) {
                assertEquals(Issue.OFFLINE_EXPIRED, e.issue)
            }
            device.sessions.logout(attemptRemote = false)
            assertNull(device.vault.read())
            assertEncrypted(device.databaseFile)
            device.database.close()

            val b = SessionIdentity(id(200), id(201), IDENTITY.environment)
            val api = SecondUserApi(device.api, b)
            val login = SessionRepository(api, device.vault, ORIGIN, clock)
            login.login("second-fixture", "test-only")
            val stale = access(login, false)
            val restarted = SessionRepository(api, device.vault, ORIGIN, clock)
            assertNotNull(restarted.restore())
            val unverifiedSource = access(restarted, false)
            val bAccess = access(restarted, true)

            val database = TaskCacheDatabase.open(device.context, device.databaseFile)
            try {
                assertEquals(1, database.openHelper.readableDatabase.version)
                val rows = RoomCacheRows(database)
                val after = TaskSnapshots(rows, device.workKey, ORIGIN).page(IDENTITY, LINE.id, 1)
                assertEquals(a02, after)
                val restored = store(device, restarted, rows)
                assertTrue(restored.operations(unverifiedSource).isEmpty())
                assertNotSame(stale.session.authorityId, bAccess.session.authorityId)
                restored.recoverInterrupted(unverifiedSource)
                val entry = requireNotNull(restored.operation(bAccess, input.operationId))
                assertEquals(WorkState.OutcomeUnknown, entry.state)
                assertEquals(envelope.workspaceId, entry.envelope.workspaceId)
                assertEquals(envelope.contentHash, entry.envelope.contentHash)
                assertEquals(IDENTITY.userId, entry.envelope.source.contributorUserId)
                assertEquals(IDENTITY.familyId, entry.envelope.source.originPersonalSessionId)
                assertEquals(batch.fingerprint, restored.batch(bAccess, batch.batchId)?.fingerprint)
                expect(WorkProblem.InvalidState) { restored.startAttempt(bAccess, batch.batchId) }

                // A separate queued operation can supply a proof only through coordinator preflight.
                try {
                    restored.operations(stale)
                    fail("Expected rotated session authority to be rejected")
                } catch (e: PatrolFailure) {
                    assertEquals(Issue.UNAUTHENTICATED, e.issue)
                }
                restored.withSourceForPreflight(unverifiedSource, second.operationId) {
                    assertEquals(source, it.source)
                    assertTrue(proof == it.proof)
                }
            } finally {
                database.close()
            }
        }
    }

    @Test fun realRoomRollsBackInterruptedWritesAndCommittedRetryIsIdempotent() = runBlocking<Unit>(Dispatchers.IO) {
        OfflineDeviceFixture().use { device ->
            device.download()
            val access = access(device.sessions, true)
            val base = store(device, device.sessions)
            base.storeSource(access, source, proof)
            val before = device.rows.count()
            val failing = object : CacheRows by device.rows {
                override suspend fun replace(slot: String, sealed: ByteArray) {
                    device.rows.replace(slot, sealed)
                    if (slot.startsWith("work-v1:")) error("Synthetic interruption after task record SQL write")
                }
            }
            expect(WorkProblem.StorageUnavailable) { store(device, device.sessions, failing).enqueue(access, input) }
            assertEquals(before, device.rows.count())
            assertNull(base.operation(access, input.operationId))
            val saved = base.enqueue(access, input)
            assertEquals(1L, saved.envelope.originLocalEditVersion)
            val batchId = id(20)
            expect(WorkProblem.StorageUnavailable) {
                store(device, device.sessions, failing).prepareBatch(access, batchId, listOf(input.operationId))
            }
            assertNull(base.batch(access, batchId))
            assertNull(base.operation(access, input.operationId)?.batchId)
            base.prepareBatch(access, batchId, listOf(input.operationId))
            base.startAttempt(access, batchId)
            device.database.close()

            val reopened = TaskCacheDatabase.open(device.context, device.databaseFile)
            try {
                val store = store(device, device.sessions, RoomCacheRows(reopened))
                val retried = store.enqueue(access, input)
                assertEquals(1L, retried.envelope.originLocalEditVersion)
                assertEquals(saved.envelope.contentHash, retried.envelope.contentHash)
                assertEquals(batchId, retried.batchId)
                store.recoverInterrupted(access)
                store.recordOutcomeUnknown(access, batchId)
                assertEquals(WorkState.OutcomeUnknown, store.batch(access, batchId)?.state)
            } finally {
                reopened.close()
            }
        }
    }

    @Test fun deadlineReachedDuringRoomWriteRollsBackOperationAndIdentityClaim() = runBlocking<Unit>(Dispatchers.IO) {
        OfflineDeviceFixture().use { device ->
            device.download()
            val access = access(device.sessions, true)
            val base = store(device, device.sessions)
            val short = source.copy(captureUntilUtc = Instant.parse(NOW).plusSeconds(1).toString())
            base.storeSource(access, short, proof)
            val before = device.rows.count()
            val crossing = object : CacheRows by device.rows {
                override suspend fun replace(slot: String, sealed: ByteArray) {
                    device.rows.replace(slot, sealed)
                    if (slot.startsWith("work-v1:")) {
                        device.sample = device.sample.copy(elapsedMs = 21_000, utcMs = Instant.parse(NOW).toEpochMilli() + 1_000)
                    }
                }
            }
            expect(WorkProblem.CaptureWindowClosed) { store(device, device.sessions, crossing).enqueue(access, input) }
            assertEquals(before, device.rows.count())
            assertNull(base.operation(access, input.operationId))

            val fresh = source.copy(contextId = id(9))
            base.storeSource(access, fresh, proof)
            val accepted = base.enqueue(access, input.copy(sourceWorkContextId = fresh.contextId))
            assertEquals(1L, accepted.envelope.originLocalEditVersion)
            assertEquals(fresh, accepted.envelope.source)
            assertEncrypted(device.databaseFile)
        }
    }

    @Test fun populatedLegacyV1IndexesAcrossLinesWithoutRewritingTaskRows() = runBlocking<Unit>(Dispatchers.IO) {
        OfflineDeviceFixture().use { device ->
            device.download()
            val access = access(device.sessions, true)
            val store = store(device, device.sessions)
            store.storeSource(access, source, proof)
            val original = requireNotNull(ProtectedWorkRows(device.rows, ORIGIN, device.workKey).transaction(task) {
                val payload = DraftOperationCanonical.operation(source, input, 1)
                val envelope = OperationEnvelope(workspaceId, input, source, 1, CANONICAL_VERSION, payload, workHash(payload))
                save(OperationEntry(envelope))
                save(WorkCatalog(1, listOf(input.operationId)))
                envelope
            })
            val otherTask = task.copy(taskId = id(29), lineId = SECOND_LINE.id)
            val other = WorkAccess(access.session, otherTask, 5, setOf(attempt))
            val otherSource = source.copy(task = otherTask, contextId = id(8))
            store.storeSource(other, otherSource, proof)
            val taskRecords = device.rows.legacyWorkSlots().associateWith { device.rows.read(it) }
            val cached = device.snapshots.page(IDENTITY, LINE.id, 1)
            device.database.close()

            val reopened = TaskCacheDatabase.open(device.context, device.databaseFile)
            try {
                assertEquals(1, reopened.openHelper.readableDatabase.version)
                val rows = RoomCacheRows(reopened)
                val ledger = store(device, device.sessions, rows)
                val foreign = input.copy(sourceWorkContextId = otherSource.contextId)
                expect(WorkProblem.OperationIdentityConflict) { ledger.enqueue(other, foreign) }
                assertNull(ledger.operation(other, foreign.operationId))
                val different = ledger.enqueue(other, foreign.copy(operationId = id(30)))
                assertEquals(original.workspaceId, different.envelope.workspaceId)
                assertEquals(1L, different.envelope.originLocalEditVersion)
                assertEquals(original.contentHash, ledger.enqueue(access, input).envelope.contentHash)
                assertEquals(cached, TaskSnapshots(rows, device.workKey, ORIGIN).page(IDENTITY, LINE.id, 1))
                for ((slot, ciphertext) in taskRecords) assertArrayEquals(ciphertext, rows.read(slot))
                assertEncrypted(device.databaseFile)
            } finally {
                reopened.close()
            }
        }
    }

    @Test fun corruptedCiphertextAndMissingWorkKeyFailClosedWithoutErasingRows() = runBlocking<Unit>(Dispatchers.IO) {
        OfflineDeviceFixture().use { device ->
            device.download()
            val access = access(device.sessions, true)
            val store = store(device, device.sessions)
            store.storeSource(access, source, proof)
            store.enqueue(access, input)
            val count = device.rows.count()
            device.database.openHelper.writableDatabase.execSQL(
                "UPDATE cache_chunk SET ciphertext=zeroblob(length(ciphertext)) WHERE slot LIKE 'work-v1:%'"
            )
            expect(WorkProblem.StorageUnavailable) { store.operations(access) }
            assertEquals(count, device.rows.count())
            assertTrue(device.workKey.removeKey())
            expect(WorkProblem.StorageUnavailable) { store.storeSource(access, source, proof) }
            assertEquals(count, device.rows.count())
        }
    }

    private fun id(i: Int) = "10000000-0000-0000-0000-" + i.toString().padStart(12, '0')

    private fun store(device: OfflineDeviceFixture, sessions: SessionRepository, rows: CacheRows = device.rows) =
        SharedWorkStore(ProtectedWorkRows(rows, ORIGIN, device.workKey), sessions)

    private suspend fun access(sessions: SessionRepository, approved: Boolean) = WorkAccess(
        sessions.bindWorkSession(), task, 5, setOf(attempt),
        if (approved) setOf(ValidatedWorkSource(source.contextId, source.contributorUserId, source.captureUntilUtc)) else emptySet(),
    )

    private fun assertEncrypted(database: File) {
        for (file in listOf(database, File(database.path + "-wal"), File(database.path + "-shm")).filter(File::exists)) {
            val bytes = file.readBytes().toString(Charsets.ISO_8859_1)
            for (value in listOf(proof, "Synthetic encrypted barcode", input.operationId, source.originPersonalSessionId)) {
                assertFalse("Protected fixture data present in SQLite files", bytes.contains(value))
            }
        }
    }

    private suspend fun expect(problem: WorkProblem, block: suspend () -> Any?) {
        try {
            block()
            fail("Expected " + problem.name)
        } catch (e: WorkFailure) {
            assertEquals(problem, e.problem)
        }
    }

    private class SecondUserApi(delegate: PatrolApi, private val subject: SessionIdentity) : PatrolApi by delegate {
        override suspend fun login(input: NativeLogin): Response<NativeAuth> = Response.success(auth("fixture-B-initial"))
        override suspend fun refresh(input: NativeRefresh): Response<NativeAuth> = Response.success(auth("fixture-B-rotated"))

        override suspend fun verify(bearer: String): Response<OfflineAccessReceipt> =
            Response.success(OfflineAccessReceipt(1, subject.userId, subject.familyId, subject.environment,
                NOW, "2026-11-22T14:00:00Z", "2026-11-26T14:00:00Z"))

        private fun auth(refresh: String) = NativeAuth("fixture-B-access", "2026-11-19T14:05:00Z",
            CurrentUser(subject.userId, "synthetic", "Second fixture", "IPQA", true, false, listOf("LinePatrol.Read")),
            refresh, subject.familyId, subject.environment, "2026-11-26T14:00:00Z")
    }
}
