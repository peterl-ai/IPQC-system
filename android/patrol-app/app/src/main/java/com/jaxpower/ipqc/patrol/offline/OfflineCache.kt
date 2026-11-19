package com.jaxpower.ipqc.patrol.offline

import android.content.Context
import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.LineDirectory
import com.jaxpower.ipqc.patrol.api.LineTaskDetail
import com.jaxpower.ipqc.patrol.api.LineTaskSummary
import com.jaxpower.ipqc.patrol.api.OfflineAccessReceipt
import com.jaxpower.ipqc.patrol.api.PagedLineTasks
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.api.ProductionLine
import com.jaxpower.ipqc.patrol.api.utc
import com.jaxpower.ipqc.patrol.api.validGuid
import com.jaxpower.ipqc.patrol.auth.Operator
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.tasks.PatrolRepository
import java.io.File
import java.time.Instant
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

data class OfflineSession(
    val identity: SessionIdentity,
    val operator: Operator,
    val verifiedAt: Instant,
    val expiresAt: Instant,
    val apparentNow: Instant,
)

@Serializable
internal data class OfflineAdmission(
    val format: Int,
    val origin: String,
    val identity: SessionIdentity,
    val displayName: String,
    val receipt: OfflineAccessReceipt,
    val progress: ClockProgress,
)

class OfflineCache internal constructor(
    private val origin: String,
    private val clock: OfflineClock,
    private val admission: ReceiptStorage,
    private val snapshots: TaskSnapshots,
) {
    private val mutex = Mutex()
    private var revoked = false

    internal suspend fun sample(): ClockSample = storage { clock.sample() }

    // Called only after SessionRepository verifies current authenticated authority over HTTPS.
    internal suspend fun accept(identity: SessionIdentity, operator: Operator, receipt: OfflineAccessReceipt,
                                beforeRequest: ClockSample) = locked {
        val record = OfflineAdmission(FORMAT, origin, identity, operator.displayName, receipt,
            ClockProgress.startingAt(beforeRequest, receipt.verifiedAtUtc))
        require(!operator.mustChangePassword)
        checkReceipt(record)
        val previous = if (revoked) null else runCatching { loadAdmission() }.getOrNull()
        if (previous?.identity == identity && previous.origin == origin &&
            utc(previous.receipt.verifiedAtUtc) >= utc(receipt.verifiedAtUtc)) {
            // A duplicate or late reply cannot move a persisted anchor forward in device time.
            observe(identity)
            return@locked
        }
        val observed = record.progress.observe(receipt, clock.sample())
        persist(record.copy(progress = observed))
        revoked = false
    }

    internal suspend fun revoke() = locked { revokeAdmission() }

    suspend fun resume(expected: SessionIdentity? = null): OfflineSession? = locked { observe(expected) }

    internal suspend fun cacheLines(identity: SessionIdentity, lines: List<ProductionLine>) = locked {
        snapshots.storeDirectory(identity, LineDirectory(lines), downloadedTime())
    }

    internal suspend fun cachePage(identity: SessionIdentity, line: ProductionLine, page: PagedLineTasks) = locked {
        snapshots.storePage(identity, line.id, page, downloadedTime())
    }

    internal suspend fun cacheDetail(identity: SessionIdentity, detail: LineTaskDetail) = locked {
        snapshots.storeDetail(identity, detail, downloadedTime())
    }

    suspend fun lines(identity: SessionIdentity): List<ProductionLine> = read(identity) {
        snapshots.directory(identity)?.items.orEmpty()
    }

    suspend fun tasks(identity: SessionIdentity, line: ProductionLine, page: Int): PagedLineTasks = read(identity) {
        requireCachedLine(identity, line)
        require(page >= 1)
        val result = snapshots.page(identity, line.id, page)
            ?: return@read PagedLineTasks(emptyList(), page, PatrolRepository.PAGE_SIZE, 0)
        check(result.page == page && result.pageSize == PatrolRepository.PAGE_SIZE &&
            result.items.all { matches(it, identity, line.id) })
        result
    }

    suspend fun detail(identity: SessionIdentity, line: ProductionLine, task: LineTaskSummary,
                       listPage: Int): LineTaskDetail = read(identity) {
        requireCachedLine(identity, line)
        if (!matches(task, identity, line.id) ||
            snapshots.page(identity, line.id, listPage)?.items?.contains(task) != true)
            throw PatrolFailure(Issue.CACHE_DETAIL_MISSING)
        val detail = snapshots.detail(identity, task)
        if (detail == null || detail.summary.currentRevisionNo != task.currentRevisionNo ||
            detail.summary.planningSnapshotVersion != task.planningSnapshotVersion ||
            detail.summary.planningSnapshotStatus != task.planningSnapshotStatus)
            throw PatrolFailure(Issue.CACHE_DETAIL_MISSING)
        check(matches(detail.summary, identity, line.id) && detail.summary.id == task.id &&
            !detail.canExecute && detail.items.isNotEmpty())
        detail
    }

    private suspend fun requireCachedLine(identity: SessionIdentity, line: ProductionLine) {
        if (snapshots.directory(identity)?.items?.contains(line) != true)
            throw PatrolFailure(Issue.LINE_MISMATCH)
    }

    private fun matches(task: LineTaskSummary, identity: SessionIdentity, lineId: String): Boolean {
        val access = task.validatedAccess
        return validGuid(task.id) && task.lineId == lineId && task.workflowVersion == 2 && !task.canExecute &&
            access.lineId == lineId && access.taskId == task.id && access.workflowVersion == task.workflowVersion &&
            SessionIdentity(access.userId, access.personalSessionId, access.environmentId) == identity
    }

    private suspend fun <T> read(identity: SessionIdentity, block: suspend () -> T): T = locked {
        observe(identity) ?: throw PatrolFailure(Issue.OFFLINE)
        val result = block()
        // Room IO and decryption can cross the expiry boundary.
        observe(identity) ?: throw PatrolFailure(Issue.OFFLINE)
        result
    }

    private fun observe(expected: SessionIdentity?): OfflineSession? {
        if (revoked) return null
        val record = loadAdmission() ?: return null
        checkReceipt(record)
        if (expected != null && record.identity != expected) throw PatrolFailure(Issue.OFFLINE)
        val next = try {
            record.progress.observe(record.receipt, clock.sample())
        } catch (e: PatrolFailure) {
            revokeAdmission()
            throw if (e.issue == Issue.OFFLINE_EXPIRED) e else PatrolFailure(Issue.OFFLINE_CLOCK)
        }
        if (next != record.progress) persist(record.copy(progress = next))
        return OfflineSession(record.identity, Operator(record.displayName, false),
            utc(record.receipt.verifiedAtUtc), utc(record.receipt.offlineAccessUntilUtc), utc(next.lastServerUtc))
    }

    private fun loadAdmission(): OfflineAdmission? =
        admission.read()?.let { decode(it) { text -> Json.decodeFromString<OfflineAdmission>(text) } }

    private fun checkReceipt(record: OfflineAdmission) {
        val identity = record.identity
        val receipt = record.receipt
        if (record.format != FORMAT || record.origin != origin || record.displayName.isBlank() ||
            !validGuid(identity.userId) || !validGuid(identity.familyId) || identity.environment.isBlank() ||
            identity != SessionIdentity(receipt.userId, receipt.personalSessionId, receipt.environmentId)) {
            revokeAdmission()
            throw PatrolFailure(Issue.OFFLINE_CLOCK)
        }
    }

    private fun revokeAdmission() {
        revoked = true
        admission.revoke()
    }

    private fun persist(record: OfflineAdmission) {
        val plaintext = Json.encodeToString(record).encodeToByteArray()
        try {
            admission.replace(plaintext)
        } finally {
            plaintext.fill(0)
        }
    }

    private fun downloadedTime(): String = Instant.now().toString()

    private suspend fun <T> locked(block: suspend () -> T): T = mutex.withLock { storage(block) }

    private suspend fun <T> storage(block: suspend () -> T): T = try {
        withContext(Dispatchers.IO) { block() }
    } catch (e: CancellationException) {
        throw e
    } catch (e: PatrolFailure) {
        throw e
    } catch (_: Exception) {
        throw PatrolFailure(Issue.CACHE_STORAGE)
    }

    companion object {
        private const val FORMAT = 1

        fun open(context: Context, origin: String): OfflineCache {
            val directory = context.noBackupFilesDir
            val database = TaskCacheDatabase.open(context, File(directory, "patrol-task-cache.db"))
            val receipt = ProtectedReceipt(File(directory, "offline-admission.v1"), KeystoreBox("ipqc.offline-admission.v1"))
            val snapshots = TaskSnapshots(RoomCacheRows(database.entries()), KeystoreBox("ipqc.shared-task-cache.v1"), origin)
            return OfflineCache(origin, AndroidOfflineClock(context.applicationContext), receipt, snapshots)
        }
    }
}
