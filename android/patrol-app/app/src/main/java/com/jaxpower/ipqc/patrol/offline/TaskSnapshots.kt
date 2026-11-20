package com.jaxpower.ipqc.patrol.offline

import com.jaxpower.ipqc.patrol.api.LineDirectory
import com.jaxpower.ipqc.patrol.api.LineTaskDetail
import com.jaxpower.ipqc.patrol.api.LineTaskSummary
import com.jaxpower.ipqc.patrol.api.PagedLineTasks
import com.jaxpower.ipqc.patrol.api.utc
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import java.security.MessageDigest
import kotlinx.serialization.KSerializer
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

@Serializable
private class SnapshotScope(
    val version: Int,
    val workspace: String,
    val origin: String,
    val identity: SessionIdentity,
    val kind: String,
    val line: String,
    val task: String,
    val workflow: Int,
    val page: Int,
)

@Serializable
private class Snapshot<T>(val scope: SnapshotScope, val downloadedAtUtc: String, val payload: T)

internal class TaskSnapshots(
    private val rows: CacheRows,
    private val cipher: CacheCipher,
    private val origin: String,
) {
    suspend fun directory(identity: SessionIdentity): LineDirectory? =
        read(scope(identity, "directory"), LineDirectory.serializer())

    suspend fun page(identity: SessionIdentity, line: String, page: Int): PagedLineTasks? =
        read(scope(identity, "page", line, workflow = 2, page = page), PagedLineTasks.serializer())

    suspend fun detail(identity: SessionIdentity, task: LineTaskSummary): LineTaskDetail? =
        read(scope(identity, "detail", task.lineId, task.id, task.workflowVersion), LineTaskDetail.serializer())

    suspend fun storeDirectory(identity: SessionIdentity, value: LineDirectory, downloaded: String) =
        write(requireNotNull(scope(identity, "directory", create = true)), value, downloaded, LineDirectory.serializer())

    suspend fun storePage(identity: SessionIdentity, line: String, value: PagedLineTasks, downloaded: String) =
        write(requireNotNull(scope(identity, "page", line, workflow = 2, page = value.page, create = true)),
            value, downloaded, PagedLineTasks.serializer())

    suspend fun storeDetail(identity: SessionIdentity, value: LineTaskDetail, downloaded: String) {
        val task = value.summary
        write(requireNotNull(scope(identity, "detail", task.lineId, task.id, task.workflowVersion, create = true)),
            value, downloaded, LineTaskDetail.serializer())
    }

    private suspend fun scope(
        identity: SessionIdentity, kind: String, line: String = "", task: String = "", workflow: Int = 0,
        page: Int = 0, create: Boolean = false,
    ): SnapshotScope? = rows.transaction { workspaceId(rows, cipher, create) }?.let {
        SnapshotScope(FORMAT, it, origin, identity, kind, line, task, workflow, page)
    }

    private suspend fun <T> read(scope: SnapshotScope?, serializer: KSerializer<T>): T? {
        if (scope == null) return null
        val scopeText = Json.encodeToString(scope)
        val slot = slot(scopeText)
        val ciphertext = rows.read(slot) ?: return null
        return decode(cipher.open(slot, ciphertext)) {
            val snapshot = Json.decodeFromString(Snapshot.serializer(serializer), it)
            check(Json.encodeToString(snapshot.scope) == scopeText)
            utc(snapshot.downloadedAtUtc)
            snapshot.payload
        }
    }

    private suspend fun <T> write(scope: SnapshotScope, payload: T, downloaded: String, serializer: KSerializer<T>) {
        val slot = slot(Json.encodeToString(scope))
        val snapshot = Snapshot(scope, downloaded, payload)
        val plaintext = Json.encodeToString(Snapshot.serializer(serializer), snapshot).encodeToByteArray()
        val sealed = try {
            cipher.seal(slot, plaintext)
        } finally {
            plaintext.fill(0)
        }
        rows.replace(slot, sealed)
    }

    private fun slot(scope: String): String = MessageDigest.getInstance("SHA-256")
        .digest(scope.encodeToByteArray()).joinToString("") { "%02x".format(it) }

    private companion object {
        const val FORMAT = 1
    }
}

internal inline fun <T> decode(clear: ByteArray, parse: (String) -> T): T = try {
    parse(clear.decodeToString(throwOnInvalidSequence = true))
} finally {
    clear.fill(0)
}
