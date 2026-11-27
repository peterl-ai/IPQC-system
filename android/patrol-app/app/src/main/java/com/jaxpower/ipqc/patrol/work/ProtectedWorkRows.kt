package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.offline.CacheCipher
import com.jaxpower.ipqc.patrol.offline.CacheRows
import com.jaxpower.ipqc.patrol.offline.KeystoreBox
import com.jaxpower.ipqc.patrol.offline.TaskCacheDatabase
import com.jaxpower.ipqc.patrol.offline.decode
import com.jaxpower.ipqc.patrol.offline.workspaceId
import kotlinx.coroutines.CancellationException
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.decodeFromJsonElement
import kotlinx.serialization.json.jsonObject

@Serializable
internal data class WorkCatalog(
    val localEditVersion: Long = 0,
    val operationIds: List<String> = emptyList(),
    val batchIds: List<String> = emptyList(),
    val itemRemovalIds: List<String> = emptyList(),
    // Keep the persisted V1 key so earlier removal observations stay effective.
    @SerialName("observedItemRemovalVersion") val observedTaskDraftVersion: Long = 0,
    val staleThroughDraftVersion: Long? = null,
    val tracksBasicInformation: Boolean = false,
    val basicInformationIds: List<String> = emptyList(),
)

@Serializable
internal data class OperationEntry(
    val envelope: OperationEnvelope,
    val batchId: String? = null,
    val quarantine: ConflictEvidence? = null,
    val offlineCapture: OfflineCaptureEvidence? = null,
)

@Serializable
internal data class OfflineCaptureEvidence(val atUtc: String, val untilUtc: String)

@Serializable
internal data class WorkIssuanceState(val contextId: String? = null, val pendingId: String? = null)

@Serializable
private data class ReceiptConflicts(val reasons: Set<ReceiptConflictReason>)

@Serializable
private data class WorkSourceStatus(val revoked: Boolean)

@Serializable
private data class WorkspaceTask(val origin: String, val workspaceId: String, val task: WorkTask)

@Serializable
private data class OperationIdClaim(val operationSlot: String?)

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
        } catch (e: PatrolFailure) {
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

    suspend fun itemRemoval(id: String): ItemRemovalEntry? = read<ItemRemovalEntry>("item-removal", id)?.also {
        workCheck(it.removal.command.operationId == id && it.removal.workspaceId == workspaceId,
            WorkProblem.StorageUnavailable)
    }

    suspend fun saveItemRemoval(entry: ItemRemovalEntry) = write("item-removal", entry.removal.command.operationId, entry)

    // A new operation and its cross-task/cross-kind claim share the caller's Room transaction.
    suspend fun reserveOperationId(id: String) = claimOperationId(id, slot("operation", id))

    suspend fun reserveItemRemovalId(id: String) = claimOperationId(id, slot("item-removal", id))

    suspend fun reserveBasicInformationId(id: String) = claimOperationId(id, slot("basic-information", id))

    suspend fun basicInformationOperation(id: String): BasicInformationEnvelope? = read("basic-information", id)

    suspend fun saveBasicInformation(envelope: BasicInformationEnvelope) =
        write("basic-information", envelope.command.operation.operationId, envelope)

    suspend fun basicInformationTransport(id: String): BasicInformationTransport? = read("basic-information-transport", id)

    suspend fun saveBasicInformationTransport(record: BasicInformationTransport) =
        write("basic-information-transport", record.batch.operation.operationId, record)

    suspend fun basicInformationBasis(owner: SessionIdentity): BasicInformationBasis? =
        read("basic-information-basis", "${owner.userId}:${owner.familyId}")

    suspend fun saveBasicInformationBasis(owner: SessionIdentity, basis: BasicInformationBasis) =
        write("basic-information-basis", "${owner.userId}:${owner.familyId}", basis)

    private suspend fun claimOperationId(id: String, operationSlot: String) {
        val marker = registrySlot("ready")
        when (readSealed<Int>(marker)) {
            null -> {
                indexLegacyOperations()
                writeSealed(marker, 1)
            }
            1 -> Unit
            else -> throw WorkFailure(WorkProblem.StorageUnavailable)
        }
        val claim = registrySlot(id)
        workCheck(readSealed<OperationIdClaim>(claim) == null, WorkProblem.OperationIdentityConflict)
        writeSealed(claim, OperationIdClaim(operationSlot))
    }

    private suspend fun indexLegacyOperations() {
        for (legacySlot in rows.legacyWorkSlots()) {
            val sealed = checkNotNull(rows.read(legacySlot))
            val entry = decode(cipher.open(legacySlot, sealed)) { text ->
                val record = Json.parseToJsonElement(text).jsonObject
                if ("envelope" in record) Json.decodeFromJsonElement<OperationEntry>(record) else null
            } ?: continue
            val envelope = entry.envelope
            val id = envelope.operation.operationId
            workCheck(envelope.workspaceId == workspaceId && isWorkId(id), WorkProblem.StorageUnavailable)
            val claim = registrySlot(id)
            val owner = readSealed<OperationIdClaim>(claim)
            // If a pre-repair build already duplicated an ID, preserve both records and block new claims.
            if (owner == null) writeSealed(claim, OperationIdClaim(legacySlot))
            else if (owner.operationSlot != legacySlot) writeSealed(claim, OperationIdClaim(null))
        }
    }

    private fun registrySlot(id: String) = "work-id-v1:" + workHash("$workspaceId:$id")

    suspend fun batch(id: String): BatchRecord? = read<BatchRecord>("batch", id)?.also {
        workCheck(it.batchId == id, WorkProblem.StorageUnavailable)
    }

    suspend fun save(batch: BatchRecord) = write("batch", batch.batchId, batch)

    suspend fun individualReceipt(id: String): ServerOperationReceipt? =
        read<ServerOperationReceipt>("individual-receipt", id)?.also {
            workCheck(it.operationId == id, WorkProblem.StorageUnavailable)
        }

    suspend fun saveIndividualReceipt(receipt: ServerOperationReceipt) =
        write("individual-receipt", receipt.operationId, receipt)

    suspend fun rememberReceiptConflict(lookup: ReceiptLookup, reason: ReceiptConflictReason) {
        val kind = when (lookup.kind) {
            ReceiptKind.Batch -> "batch-receipt-conflict"
            ReceiptKind.Operation -> "operation-receipt-conflict"
        }
        val old = read<ReceiptConflicts>(kind, lookup.id)?.reasons.orEmpty()
        if (reason !in old) write(kind, lookup.id, ReceiptConflicts(old + reason))
    }

    suspend fun source(id: String): StoredWorkSource? = read<StoredWorkSource>("source", id)?.also {
        workCheck(it.source.contextId == id, WorkProblem.StorageUnavailable)
    }

    suspend fun save(source: StoredWorkSource) = write("source", source.source.contextId, source)

    suspend fun offlineProfile(userId: String): OfflineSampleProfile? = read("offline-profile", userId)
    suspend fun saveOfflineProfile(profile: OfflineSampleProfile) = write("offline-profile", profile.owner.userId, profile)

    suspend fun issuance(userId: String): WorkIssuanceState = read("issuance", userId) ?: WorkIssuanceState()
    suspend fun saveIssuance(userId: String, state: WorkIssuanceState) = write("issuance", userId, state)

    suspend fun sourceRevoked(contextId: String): Boolean = read<WorkSourceStatus>("source-revoked", contextId)?.revoked == true
    suspend fun rememberSourceRevoked(contextId: String) = write("source-revoked", contextId, WorkSourceStatus(true))

    private fun slot(kind: String, id: String) = "work-v1:" + workHash("$namespace:$kind:$id")

    private suspend inline fun <reified T> read(kind: String, id: String = ""): T? = readSealed(slot(kind, id))

    private suspend inline fun <reified T> readSealed(slot: String): T? {
        val sealed = rows.read(slot) ?: return null
        return decode(cipher.open(slot, sealed)) { Json.decodeFromString<T>(it) }
    }

    private suspend inline fun <reified T> write(kind: String, id: String = "", value: T) =
        writeSealed(slot(kind, id), value)

    private suspend inline fun <reified T> writeSealed(slot: String, value: T) {
        val bytes = Json.encodeToString(value).encodeToByteArray()
        try {
            rows.replace(slot, cipher.seal(slot, bytes))
        } finally {
            bytes.fill(0)
        }
    }
}
