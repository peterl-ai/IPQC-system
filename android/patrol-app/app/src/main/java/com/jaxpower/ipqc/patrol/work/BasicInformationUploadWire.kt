package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.api.GuardedUploadExchange
import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.api.ensureResponse
import com.jaxpower.ipqc.patrol.api.responseBody
import java.io.IOException
import java.nio.charset.CharacterCodingException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody
import okhttp3.RequestBody.Companion.toRequestBody

@Serializable
private class BasicInformationBatchInput(
    val batchId: String,
    val canonicalizationVersion: Int,
    val expectedTaskLineId: String,
    val expectedWorkflowVersion: Int,
    val expectedEnvironmentId: String,
    val expectedDraftVersion: Long,
    val operation: BasicInformationOperationInput,
    val sourceProof: UploadSourceProof,
)

@Serializable
private class BasicInformationOperationInput(
    val operationId: String,
    val canonicalizationVersion: Int,
    val kind: BasicInformationKind,
    val field: BasicInformationField,
    val sourceWorkContextId: String,
    val contributorUserIdAssertion: String,
    val baseDraftVersion: Long,
    val originLocalEditVersion: Long,
    val value: String?,
    val predecessor: OperationReference?,
)

internal fun BasicInformationUploadCandidate.encodeBatch(): RequestBody {
    val task = target.task
    val batch = transport.batch
    val operation = original.command.operation
    val input = BasicInformationBatchInput(batch.batchId, CANONICAL_VERSION, task.lineId, task.workflowVersion,
        task.environmentId, operation.baseDraftVersion,
        BasicInformationOperationInput(operation.operationId, original.canonicalizationVersion, operation.kind, operation.field,
            operation.sourceWorkContextId, operation.contributorUserId, operation.baseDraftVersion,
            original.originLocalEditVersion, operation.value, operation.predecessor),
        UploadSourceProof(source.source.contextId, source.proof))
    return Json.encodeToString(input).toRequestBody("application/json".toMediaType())
}

internal class BasicInformationBatchResult(
    val receipt: BasicInformationBatchReceipt?, val operation: BasicInformationReceipt, val replayOnly: Boolean,
)

internal suspend fun WorkContextApi.sendBasicInformationBatch(
    bearer: String, task: WorkTask, body: RequestBody, guard: GuardedUploadExchange,
): UploadExchange<BasicInformationBatchResult> =
    uploadExchange({ uploadBasicInformationBatch(bearer, task.taskId, body, guard) },
        { it.decodeBasicInformationUpload(task) }, ::basicInformationRejection)

internal fun JsonObject.decodeBasicInformationUpload(task: WorkTask): BasicInformationBatchResult {
    exactKeys("batchReceipt", "operation", "replayOnly", "canExecute", "canSubmit", "acceptsFormalInspection")
    for (flag in listOf("canExecute", "canSubmit", "acceptsFormalInspection")) requireFalse(flag)
    val replay = this["replayOnly"]
    ensureResponse(replay == JsonPrimitive(true) || replay == JsonPrimitive(false))
    val replayOnly = replay == JsonPrimitive(true)
    val operation = this["operation"].asObject().decodeBasicInformationReceipt(task)
    val receipt = this["batchReceipt"].let { if (it == JsonNull) null else it.asObject().decodeBasicInformationBatch(task) }
    ensureResponse(if (receipt == null) replayOnly else receipt.operation == operation)
    return BasicInformationBatchResult(receipt, operation, replayOnly)
}

internal fun JsonObject.decodeBasicInformationBatch(task: WorkTask): BasicInformationBatchReceipt {
    exactKeys("taskId", "batchId", "requestFingerprint", "uploadedByUserId", "uploadedPersonalSessionId",
        "acceptedAtUtc", "previousDraftVersion", "draftVersion", "operations", "receiptVersion", "headerOperations")
    ensureResponse(text("taskId") == task.taskId && number("receiptVersion") == 6L && array("operations").isEmpty())
    val version = number("draftVersion")
    val operation = array("headerOperations").singleOrNull().asObject().decodeBasicInformationReceipt(task, version)
    val batchId = text("batchId")
    val previous = number("previousDraftVersion")
    val fingerprint = text("requestFingerprint")
    ensureResponse(batchId == operation.batchId && version == operation.draftVersion &&
        previous == operation.operation.baseDraftVersion && text("acceptedAtUtc") == operation.acceptedAtUtc &&
        text("uploadedByUserId") == operation.actorUserId &&
        text("uploadedPersonalSessionId") == operation.actorPersonalSessionId &&
        fingerprint == BasicInformationCanonical.fingerprint(task, batchId, previous, operation.reference()))
    return BasicInformationBatchReceipt(batchId, fingerprint, previous, operation)
}

internal suspend fun WorkContextApi.readBasicInformationReceipt(target: ReceiptTarget, bearer: String):
    BasicInformationReceiptReply = try {
    val response = when (target.lookup.kind) {
        ReceiptKind.Batch -> batchReceipt(bearer, target.task.taskId, target.lookup.id)
        ReceiptKind.Operation -> basicInformationOperationReceipt(bearer, target.task.taskId, target.lookup.id)
    }
    when (response.code()) {
        404 -> {
            response.errorBody()?.close()
            BasicInformationReceiptReply.Missing
        }
        409 -> {
            response.errorBody()?.close()
            BasicInformationReceiptReply.Conflict
        }
        else -> responseBody { response }.use { body ->
            ensureResponse(response.code() == 200 && body.contentType()?.subtype == "json")
            withContext(Dispatchers.IO) {
                val json = body.readWorkJson()
                when (target.lookup.kind) {
                    ReceiptKind.Batch -> BasicInformationReceiptReply.Batch(json.decodeBasicInformationBatch(target.task))
                    ReceiptKind.Operation ->
                        BasicInformationReceiptReply.Operation(json.decodeBasicInformationReceipt(target.task))
                }
            }
        }
    }
} catch (failure: PatrolFailure) {
    if (failure.issue == Issue.UNAUTHENTICATED || failure.issue == Issue.FORBIDDEN) throw failure
    BasicInformationReceiptReply.Unavailable(failure.issue)
} catch (_: CharacterCodingException) {
    BasicInformationReceiptReply.Unavailable(Issue.RESPONSE)
} catch (_: IOException) {
    BasicInformationReceiptReply.Unavailable(Issue.CONNECTION)
} catch (_: SerializationException) {
    BasicInformationReceiptReply.Unavailable(Issue.RESPONSE)
} catch (_: IllegalArgumentException) {
    BasicInformationReceiptReply.Unavailable(Issue.RESPONSE)
}

private fun basicInformationRejection(status: Int, body: JsonObject): UploadRejection? {
    val code = (body["code"] as? JsonPrimitive)?.takeIf { it.isString }?.content ?: return null
    if (status == 400 && code in setOf("InvalidBasicInformationBatch", "InvalidBasicInformationOperation",
            "InvalidDraftJson", "SourceManifestConflict", "UnsupportedOperationVersion"))
        return UploadRejection(WorkConflict.BatchInputRejected)
    if (status != 409) return null
    return when (code) {
        "DraftVersionConflict" -> UploadRejection(WorkConflict.DraftVersionConflict)
        "DraftScopeMismatch", "DraftAcceptanceUnavailable", "BasicInformationFieldAlreadyCaptured",
        "BasicInformationPredecessorConflict" -> UploadRejection(WorkConflict.TaskScopeConflict)
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
