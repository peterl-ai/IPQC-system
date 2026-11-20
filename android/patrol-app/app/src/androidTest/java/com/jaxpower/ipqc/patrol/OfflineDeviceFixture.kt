package com.jaxpower.ipqc.patrol

import android.content.Context
import androidx.test.platform.app.InstrumentationRegistry
import com.jaxpower.ipqc.patrol.api.*
import com.jaxpower.ipqc.patrol.auth.KeystoreCredentialVault
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import com.jaxpower.ipqc.patrol.offline.*
import com.jaxpower.ipqc.patrol.tasks.PatrolRepository
import java.io.File
import java.time.Clock
import java.time.Instant
import java.time.ZoneOffset
import java.util.UUID
import java.util.concurrent.atomic.AtomicInteger
import retrofit2.Response

internal class OfflineDeviceFixture : AutoCloseable {
    val context: Context = InstrumentationRegistry.getInstrumentation().targetContext
    private val stem = "a02-test-" + UUID.randomUUID()
    val receiptFile = File(context.noBackupFilesDir, "$stem.admission")
    val databaseFile = File(context.noBackupFilesDir, "$stem.db")
    val receiptKey = KeystoreBox("$stem.admission")
    val workKey = KeystoreBox("$stem.work")
    val vault = KeystoreCredentialVault(context, "$stem.credential", "$stem.credential")
    val database = TaskCacheDatabase.open(context, databaseFile)
    val rows = RoomCacheRows(database)
    val snapshots = TaskSnapshots(rows, workKey, ORIGIN)
    val api = FixtureApi()
    @Volatile var sample = ClockSample(8, 20_000, Instant.parse(NOW).toEpochMilli())
    val cache = openCache()
    val sessions = SessionRepository(api, vault, ORIGIN, Clock.fixed(Instant.parse(NOW), ZoneOffset.UTC), cache)
    val patrol = PatrolRepository(api, sessions, cache)

    fun openCache(db: TaskCacheDatabase = database) = OfflineCache(
        ORIGIN, OfflineClock { sample }, ProtectedReceipt(receiptFile, receiptKey),
        TaskSnapshots(RoomCacheRows(db), workKey, ORIGIN),
    )

    suspend fun download() {
        sessions.login("synthetic", "test-only")
        patrol.lines()
        patrol.tasks(LINE, 1)
        patrol.detail(LINE, TASK)
    }

    override fun close() {
        database.close()
        context.deleteDatabase(databaseFile.absolutePath)
        ProtectedReceipt(receiptFile, receiptKey).revoke()
        workKey.removeKey()
        vault.clear()
    }

    class FixtureApi : PatrolApi {
        val calls = AtomicInteger()

        override suspend fun login(input: NativeLogin): Response<NativeAuth> {
            calls.incrementAndGet()
            return Response.success(NativeAuth(
                "fixture-access", "2026-11-19T14:05:00Z",
                CurrentUser(IDENTITY.userId, "synthetic", "Fixture Inspector", "IPQA", true, false,
                    listOf("LinePatrol.Read")),
                "fixture-refresh", IDENTITY.familyId, IDENTITY.environment, "2026-11-26T14:00:00Z",
            ))
        }

        override suspend fun verify(bearer: String): Response<OfflineAccessReceipt> {
            calls.incrementAndGet()
            return Response.success(OfflineAccessReceipt(1, IDENTITY.userId, IDENTITY.familyId,
                IDENTITY.environment, NOW, "2026-11-22T14:00:00Z", "2026-11-26T14:00:00Z"))
        }

        override suspend fun lines(bearer: String): Response<LineDirectory> {
            calls.incrementAndGet()
            return Response.success(LineDirectory(listOf(LINE, SECOND_LINE)))
        }

        override suspend fun tasks(bearer: String, lineId: String, page: Int, pageSize: Int): Response<PagedLineTasks> {
            calls.incrementAndGet()
            check(lineId == LINE.id)
            return Response.success(PagedLineTasks(listOf(TASK), page, pageSize, 1))
        }

        override suspend fun detail(bearer: String, taskId: String, lineId: String): Response<LineTaskDetail> {
            calls.incrementAndGet()
            check(taskId == TASK.id && lineId == LINE.id)
            return Response.success(DETAIL)
        }

        override suspend fun refresh(input: NativeRefresh) = error("Unexpected refresh")
        override suspend fun logout(bearer: String, input: NativeRefresh) = error("Unexpected remote logout")
        override suspend fun changePassword(bearer: String, input: PasswordChange) = error("Unexpected credential mutation")
    }

    companion object {
        const val ORIGIN = "https://device-fixture.example.invalid/"
        const val NOW = "2026-11-19T14:00:00Z"
        const val PRIVATE_FIXTURE_TEXT = "Synthetic sealed definition value"
        val IDENTITY = SessionIdentity("10000000-0000-0000-0000-000000000001",
            "20000000-0000-0000-0000-000000000001", "synthetic-device-env")
        val LINE = ProductionLine("30000000-0000-0000-0000-000000000001", "CELL-A", "Assembly East", 1)
        val SECOND_LINE = ProductionLine("30000000-0000-0000-0000-000000000002", "CELL-B", "Assembly West", 2)
        private const val TASK_ID = "40000000-0000-0000-0000-000000000001"
        val TASK = LineTaskSummary(TASK_ID, "VM-FIXTURE-001", LINE.id, 2, "PendingInspection",
            NOW, NOW, 0, readFoundationStatus = "FoundationOnly", canExecute = false,
            validatedAccess = ValidatedTaskAccess(IDENTITY.userId, IDENTITY.familyId, IDENTITY.environment,
                NOW, TASK_ID, LINE.id, 2), planningSnapshotVersion = 1, planningSnapshotStatus = "ValidV1")
        val DETAIL = LineTaskDetail(TASK, "50000000-0000-0000-0000-000000000001",
            "50000000-0000-0000-0000-000000000002", NOW,
            listOf(LineTaskDefinition("60000000-0000-0000-0000-000000000001", "60000000-0000-0000-0000-000000000002",
                1, "FI", "Inspection", "Visual", PRIVATE_FIXTURE_TEXT, "Verify item", "", "", "", "",
                "Qualitative", "Per round", "2", "Fixture requirement", "Minor")), false)
    }
}
