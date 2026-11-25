package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.api.GuardedUploadExchange
import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.api.ensureResponse
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody
import okhttp3.RequestBody.Companion.toRequestBody

internal sealed interface ItemRemovalUploadResult {
    data class Accepted(val receipt: ItemRemovalBatchReceipt, val replayOnly: Boolean) : ItemRemovalUploadResult
    data class NeedsBatchReceipt(val operation: ItemRemovalReceipt) : ItemRemovalUploadResult
    data class Rejected(val reason: WorkConflict) : ItemRemovalUploadResult
    data class NotSent(val issue: Issue) : ItemRemovalUploadResult
    data class OutcomeUnknown(val issue: Issue) : ItemRemovalUploadResult
}

internal class ItemRemovalUploadCandidate(
    val target: ReceiptTarget,
    val entry: ItemRemovalEntry,
    val source: StoredWorkSource,
) {
    fun preflightInput(): WorkContextPreflightInputWire {
        val task = target.task
        return WorkContextPreflightInputWire(task.lineId, task.workflowVersion, task.environmentId,
            listOf(WorkContextSourceWire(source.source.contextId, source.proof, source.source.contributorUserId)))
    }
}

@Serializable
private class RemovalBatchInput(
    val batchId: String,
    val canonicalizationVersion: Int,
    val expectedTaskLineId: String,
    val expectedWorkflowVersion: Int,
    val expectedEnvironmentId: String,
    val expectedDraftVersion: Long,
    val operation: RemovalOperationInput,
    val sourceProof: UploadSourceProof,
)

@Serializable
private class RemovalOperationInput(
    val operationId: String,
    val canonicalizationVersion: Int,
    val kind: String,
    val taskItemId: String,
    val inspectionAttemptId: String,
    val sourceWorkContextId: String,
    val contributorUserIdAssertion: String,
    val baseDraftVersion: Long,
    val originLocalEditVersion: Long,
    val confirmedAbsentOrNotApplicable: Boolean,
    val confirmationSource: RemovalConfirmation,
    val reason: String?,
)

// Called after the protected transaction has rechecked canonical bytes, hashes,
// original proof and preflight. The Backend recomputes the two hashes from this input.
internal fun ItemRemovalUploadCandidate.encodeBatch(): RequestBody {
    val task = target.task
    val batch = requireNotNull(entry.batch)
    val removal = entry.removal
    val cmd = removal.command
    val input = RemovalBatchInput(batch.batchId, batch.canonicalizationVersion, task.lineId, task.workflowVersion,
        task.environmentId, batch.expectedDraftVersion, RemovalOperationInput(cmd.operationId,
            removal.canonicalizationVersion, "RemoveItem", cmd.attempt.taskItemId, cmd.attempt.inspectionAttemptId,
            removal.source.contextId, removal.source.contributorUserId, cmd.expectedDraftVersion,
            removal.localEditVersion, cmd.confirmedAbsentOrNotApplicable, cmd.confirmation, cmd.reason),
        UploadSourceProof(source.source.contextId, source.proof))
    val bytes = Json.encodeToString(input).encodeToByteArray()
    workCheck(bytes.size <= 131_072, WorkProblem.InvalidState)
    return bytes.toRequestBody("application/json".toMediaType())
}

internal class ItemRemovalBatchResult(val receipt: ItemRemovalBatchReceipt?, val operation: ItemRemovalReceipt,
                                       val replayOnly: Boolean)

internal suspend fun WorkContextApi.sendItemRemovalBatch(bearer: String, taskId: String, body: RequestBody,
    guard: GuardedUploadExchange): UploadExchange<ItemRemovalBatchResult> =
    uploadExchange({ uploadItemRemovalBatch(bearer, taskId, body, guard) },
        JsonObject::decodeItemRemovalUpload, ::removalRejection)

private fun JsonObject.decodeItemRemovalUpload(): ItemRemovalBatchResult {
    ensureResponse(keys == setOf("batchReceipt", "operation", "replayOnly", "canExecute", "acceptsFormalInspection") &&
        get("canExecute") == JsonPrimitive(false) && get("acceptsFormalInspection") == JsonPrimitive(false))
    val replay = get("replayOnly") as? JsonPrimitive
    ensureResponse(replay != null && !replay.isString && replay.booleanOrNull != null)
    val replayOnly = requireNotNull(replay?.booleanOrNull)
    val item = (get("operation") as? JsonObject)?.decodeItemRemovalReceipt() ?: throw PatrolFailure(Issue.RESPONSE)
    val receipt = when (val nested = get("batchReceipt")) {
        JsonNull -> null
        is JsonObject -> nested.decodeItemRemovalBatchReceipt()
        else -> throw PatrolFailure(Issue.RESPONSE)
    }
    ensureResponse(if (receipt == null) replayOnly else receipt.itemOperations.single() == item)
    return ItemRemovalBatchResult(receipt, item, replayOnly)
}

private fun removalRejection(status: Int, body: JsonObject): UploadRejection? {
    val code = (body["code"] as? JsonPrimitive)?.content ?: return null
    if (status == 400 && code in setOf("InvalidItemRemovalBatch", "InvalidItemRemoval", "InvalidDraftJson",
            "SourceManifestConflict", "UnsupportedOperationVersion")) return UploadRejection(WorkConflict.BatchInputRejected)
    if (status != 409) return null
    return when (code) {
        "DraftVersionConflict" -> UploadRejection(WorkConflict.DraftVersionConflict)
        "DraftScopeMismatch", "DraftAttemptConflict", "DraftAcceptanceUnavailable", "DraftItemAlreadyRemoved" ->
            UploadRejection(WorkConflict.TaskScopeConflict)
        "SourceSecurityRevoked", "SourceProvenanceConflict" -> {
            if (body["preserveOriginals"] != JsonPrimitive(true) || body["quarantineRequired"] != JsonPrimitive(true))
                return null
            val contexts = body.ids("sourceContextIds") ?: return null
            val operations = body.ids("operationIds") ?: return null
            val reason = if (code == "SourceSecurityRevoked") WorkConflict.SourceSecurityRevoked
                else WorkConflict.SourceProvenanceConflict
            UploadRejection(reason, contexts, operations)
        }
        else -> null
    }
}
