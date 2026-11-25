package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.api.GuardedUploadExchange
import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.MAX_UPLOAD_ERROR_BYTES
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.api.ensureResponse
import java.io.IOException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.ExperimentalSerializationApi
import kotlinx.serialization.Serializable
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.JsonUnquotedLiteral
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.decodeFromJsonElement
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.ResponseBody
import retrofit2.Response

internal sealed interface SampleUploadResult {
    data class Accepted(val receipt: ServerBatchReceipt, val replayOnly: Boolean) : SampleUploadResult
    data class Rejected(val reason: WorkConflict) : SampleUploadResult
    data class NotSent(val issue: Issue) : SampleUploadResult
    data class OutcomeUnknown(val issue: Issue) : SampleUploadResult
    data object NeedsBatchReceipt : SampleUploadResult
}

internal class UploadRejection(
    val reason: WorkConflict,
    val contexts: Set<String> = emptySet(),
    val operations: Set<String> = emptySet(),
)

internal sealed interface UploadExchange<out T> {
    data class Success<T>(val body: T) : UploadExchange<T>
    data class Rejected(val rejection: UploadRejection) : UploadExchange<Nothing>
    data class Unknown(val issue: Issue) : UploadExchange<Nothing>
}

internal class SampleBatchResult(val receipt: ServerBatchReceipt?, val replayOnly: Boolean)

@Serializable
private class SampleBatchInput(
    val batchId: String,
    val canonicalizationVersion: Int,
    val expectedTaskLineId: String,
    val expectedWorkflowVersion: Int,
    val expectedEnvironmentId: String,
    val expectedDraftVersion: Long,
    val operations: List<SampleOperationInput>,
    val acceptedReferences: List<OperationReference>,
    val sourceProofs: List<UploadSourceProof>,
)

@Serializable
private class SampleOperationInput(
    val operationId: String,
    val canonicalizationVersion: Int,
    val kind: SampleKind,
    val taskItemId: String,
    val inspectionAttemptId: String,
    val clientSampleId: String,
    val sourceWorkContextId: String,
    val contributorUserIdAssertion: String,
    val baseDraftVersion: Long,
    val originLocalEditVersion: Long,
    val data: SampleValueInput?,
    val dependencies: List<OperationReference>,
)

@Serializable
private class SampleValueInput(val judgment: String?, val inspectionValue: JsonPrimitive?, val barcode: String?)

@Serializable
internal class UploadSourceProof(val workContextId: String, val proofSecret: String)

// The ledger has already validated these exact canonical decimal tokens.
// JsonPrimitive(Number) can pass large decimals through binary floating point on encode.
@OptIn(ExperimentalSerializationApi::class)
internal fun UploadCandidate.encodeBatch(): RequestBody {
    val task = target.task
    val input = SampleBatchInput(batch.batchId, batch.canonicalizationVersion, task.lineId, task.workflowVersion,
        task.environmentId, batch.expectedDraftVersion, members.map { entry ->
            val op = entry.operation
            val data = op.data?.let { SampleValueInput(it.judgment,
                it.inspectionValue?.let(::JsonUnquotedLiteral), it.barcode) }
            SampleOperationInput(op.operationId, entry.canonicalizationVersion, op.kind, op.attempt.taskItemId,
                op.attempt.inspectionAttemptId, op.clientSampleId, entry.source.contextId, entry.source.contributorUserId,
                op.baseDraftVersion, entry.originLocalEditVersion, data, op.dependencies)
        }, batch.acceptedReferences, sources.map { UploadSourceProof(it.source.contextId, it.proof) })
    val bytes = Json.encodeToString(input).encodeToByteArray()
    workCheck(bytes.size <= 131_072, WorkProblem.InvalidState)
    return bytes.toRequestBody("application/json".toMediaType())
}

internal fun UploadCandidate.preflightInput(): WorkContextPreflightInputWire {
    val task = target.task
    return WorkContextPreflightInputWire(task.lineId, task.workflowVersion, task.environmentId, sources.map {
        WorkContextSourceWire(it.source.contextId, it.proof, it.source.contributorUserId)
    })
}

internal suspend fun WorkContextApi.sendSampleBatch(bearer: String, taskId: String, body: RequestBody,
                                                    guard: GuardedUploadExchange): UploadExchange<SampleBatchResult> =
    uploadExchange({ uploadSampleBatch(bearer, taskId, body, guard) }, JsonObject::decodeUploadResult, ::batchRejection)

internal suspend fun WorkContextApi.checkUploadSources(bearer: String, taskId: String,
    input: WorkContextPreflightInputWire, guard: GuardedUploadExchange): UploadExchange<WorkContextPreflightWire> =
    uploadExchange({ preflightUploadSources(bearer, taskId, input, guard) },
        { Json.decodeFromJsonElement(it) }, ::preflightRejection)

internal suspend fun <T> uploadExchange(call: suspend () -> Response<ResponseBody>, decode: (JsonObject) -> T,
                                       classify: (Int, JsonObject) -> UploadRejection?): UploadExchange<T> = try {
    val reply = call()
    if (reply.isSuccessful) {
        val body = reply.body() ?: throw PatrolFailure(Issue.RESPONSE)
        body.use {
            ensureResponse(reply.code() == 200 && it.contentType()?.subtype == "json")
            UploadExchange.Success(withContext(Dispatchers.IO) { decode(it.readWorkJson()) })
        }
    } else {
        val status = reply.code()
        reply.errorBody().use { body ->
            when (status) {
                401 -> throw PatrolFailure(Issue.UNAUTHENTICATED)
                403 -> throw PatrolFailure(Issue.FORBIDDEN)
            }
            val rejection = if (status in setOf(400, 409) && body?.contentType()?.subtype == "problem+json") {
                val problem = body.readWorkJson(MAX_UPLOAD_ERROR_BYTES)
                val code = problem["code"] as? JsonPrimitive
                if (problem["status"] == JsonPrimitive(status) && code?.isString == true && problem["title"] == code)
                    classify(status, problem)
                else null
            } else null
            if (rejection != null) UploadExchange.Rejected(rejection) else UploadExchange.Unknown(when (status) {
                400 -> Issue.BAD_REQUEST
                409 -> Issue.CONFLICT
                429 -> Issue.RATE_LIMITED
                in 500..599 -> Issue.UNAVAILABLE
                else -> Issue.RESPONSE
            })
        }
    }
} catch (failure: PatrolFailure) {
    if (failure.issue in setOf(Issue.UNAUTHENTICATED, Issue.FORBIDDEN)) throw failure
    UploadExchange.Unknown(failure.issue)
} catch (_: IOException) {
    UploadExchange.Unknown(Issue.CONNECTION)
} catch (_: SerializationException) {
    UploadExchange.Unknown(Issue.RESPONSE)
} catch (_: IllegalArgumentException) {
    UploadExchange.Unknown(Issue.RESPONSE)
}

private fun JsonObject.decodeUploadResult(): SampleBatchResult {
    ensureResponse(keys == setOf("batchReceipt", "operations", "replayOnly", "canExecute", "acceptsFormalInspection") &&
        get("canExecute") == JsonPrimitive(false) && get("acceptsFormalInspection") == JsonPrimitive(false))
    val replay = get("replayOnly") as? JsonPrimitive
    ensureResponse(replay != null && !replay.isString && replay.booleanOrNull != null)
    val replayOnly = requireNotNull(replay?.booleanOrNull)
    val operations = decodeOperationReceipts(get("operations"))
    ensureResponse(operations.isNotEmpty() && operations.map { it.operationId }.toSet().size == operations.size)
    val receipt = when (val nested = get("batchReceipt")) {
        JsonNull -> null
        is JsonObject -> nested.decodeBatchReceipt()
        else -> throw PatrolFailure(Issue.RESPONSE)
    }
    if (receipt == null) ensureResponse(replayOnly)
    else ensureResponse(receipt.operations.sortedBy { it.operationId } == operations.sortedBy { it.operationId })
    return SampleBatchResult(receipt, replayOnly)
}

private fun preflightRejection(status: Int, body: JsonObject): UploadRejection? {
    if (status != 409 || body["code"] != JsonPrimitive("SourceSecurityRevoked")) return null
    val contexts = body.ids("sourceContextIds")
    return if (contexts?.size == 1) UploadRejection(WorkConflict.SourceSecurityRevoked, contexts) else null
}

private fun batchRejection(status: Int, body: JsonObject): UploadRejection? {
    val code = body["code"] as? JsonPrimitive ?: return null
    if (!code.isString) return null
    if (status == 400 && code.content in setOf("InvalidDraftBatch", "InvalidDraftJson", "InvalidSampleValue",
            "InvalidSampleRemoval", "CyclicDependency", "SourceManifestConflict", "UnsupportedOperationVersion",
            "UnsupportedSampleOperation", "SharedSampleLimitExceeded")) return UploadRejection(WorkConflict.BatchInputRejected)
    if (status != 409) return null
    return when (code.content) {
        "DraftVersionConflict" -> UploadRejection(WorkConflict.DraftVersionConflict)
        "DraftScopeMismatch", "DraftAttemptConflict", "DraftAcceptanceUnavailable",
        "AcceptedAttemptNotApplicable", "DraftItemRemoved" -> UploadRejection(WorkConflict.TaskScopeConflict)
        "SourceSecurityRevoked", "SourceProvenanceConflict" -> {
            if (body["preserveOriginals"] != JsonPrimitive(true) || body["quarantineRequired"] != JsonPrimitive(true))
                return null
            val contexts = body.ids("sourceContextIds") ?: return null
            val operations = body.ids("operationIds") ?: return null
            val reason = if (code.content == "SourceSecurityRevoked") WorkConflict.SourceSecurityRevoked
                else WorkConflict.SourceProvenanceConflict
            UploadRejection(reason, contexts, operations)
        }
        else -> null
    }
}

internal fun JsonObject.ids(key: String): Set<String>? {
    val values = get(key) as? JsonArray ?: return null
    if (values.size !in 1..50) return null
    val distinct = mutableSetOf<String>()
    for (field in values) {
        val id = field as? JsonPrimitive ?: return null
        if (!id.isString || !isWorkId(id.content) || !distinct.add(id.content)) return null
    }
    return distinct
}
