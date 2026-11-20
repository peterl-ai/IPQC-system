package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.offline.CacheCipher
import com.jaxpower.ipqc.patrol.offline.CacheRows
import com.jaxpower.ipqc.patrol.offline.KeystoreBox
import com.jaxpower.ipqc.patrol.offline.TaskCacheDatabase
import com.jaxpower.ipqc.patrol.offline.decode
import com.jaxpower.ipqc.patrol.offline.workspaceId
import kotlinx.coroutines.CancellationException
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

@Serializable
internal data class WorkCatalog(
    val localEditVersion: Long = 0,
    val operationIds: List<String> = emptyList(),
    val batchIds: List<String> = emptyList(),
)

@Serializable
internal data class OperationEntry(
    val envelope: OperationEnvelope,
    val batchId: String? = null,
    val quarantine: ConflictEvidence? = null,
)

@Serializable
private data class WorkspaceTask(val origin: String, val workspaceId: String, val task: WorkTask)

internal class ProtectedWorkRows(
    private val rows: CacheRows,
    val origin: String,
    private val cipher: CacheCipher = KeystoreBox(TaskCacheDatabase.WORK_KEY_ALIAS),
) {
    suspend fun <T> transaction(task: WorkTask, create: Boolean = false, action: suspend WorkTransaction.() -> T): T? =
        try {
            rows.transaction {
                val workspace = workspaceId(rows, cipher, create) ?: return@transaction null
                val namespace = workHash(Json.encodeToString(WorkspaceTask(origin, workspace, task)))
                WorkTransaction(workspace, namespace, rows, cipher).action()
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: WorkFailure) {
            throw e
        } catch (_: Exception) {
            // Serialization, SQLite, and crypto exception text may contain protected values.
            throw WorkFailure(WorkProblem.StorageUnavailable)
        }
}

internal class WorkTransaction(
    val workspaceId: String,
    private val namespace: String,
    private val rows: CacheRows,
    private val cipher: CacheCipher,
) {
    suspend fun catalog(): WorkCatalog = read("catalog") ?: WorkCatalog()
    suspend fun save(catalog: WorkCatalog) = write("catalog", value = catalog)

    suspend fun operation(id: String): OperationEntry? = read<OperationEntry>("operation", id)?.also {
        workCheck(it.envelope.operation.operationId == id && it.envelope.workspaceId == workspaceId,
            WorkProblem.StorageUnavailable)
    }

    suspend fun save(entry: OperationEntry) = write("operation", entry.envelope.operation.operationId, entry)

    suspend fun batch(id: String): BatchRecord? = read<BatchRecord>("batch", id)?.also {
        workCheck(it.batchId == id, WorkProblem.StorageUnavailable)
    }

    suspend fun save(batch: BatchRecord) = write("batch", batch.batchId, batch)

    suspend fun source(id: String): StoredWorkSource? = read<StoredWorkSource>("source", id)?.also {
        workCheck(it.source.contextId == id, WorkProblem.StorageUnavailable)
    }

    suspend fun save(source: StoredWorkSource) = write("source", source.source.contextId, source)

    private fun slot(kind: String, id: String) = "work-v1:" + workHash("$namespace:$kind:$id")

    private suspend inline fun <reified T> read(kind: String, id: String = ""): T? {
        val slot = slot(kind, id)
        val sealed = rows.read(slot) ?: return null
        return decode(cipher.open(slot, sealed)) { Json.decodeFromString<T>(it) }
    }

    private suspend inline fun <reified T> write(kind: String, id: String = "", value: T) {
        val slot = slot(kind, id)
        val bytes = Json.encodeToString(value).encodeToByteArray()
        try {
            rows.replace(slot, cipher.seal(slot, bytes))
        } finally {
            bytes.fill(0)
        }
    }
}
