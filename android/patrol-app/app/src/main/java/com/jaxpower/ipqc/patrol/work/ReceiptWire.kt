package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.api.ensureResponse
import com.jaxpower.ipqc.patrol.api.responseBody
import java.io.IOException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.decodeFromJsonElement
import kotlinx.serialization.json.longOrNull
import okhttp3.ResponseBody

internal sealed interface ReceiptReply {
    data class Batch(val receipt: ServerBatchReceipt) : ReceiptReply
    data class Operation(val receipt: ServerOperationReceipt) : ReceiptReply
    data object Missing : ReceiptReply
    data object Conflict : ReceiptReply
    data class Unavailable(val issue: Issue) : ReceiptReply
}

private const val MAX_RECEIPT_BYTES = 512L * 1024
private val receiptJson = Json
private val integerToken = Regex("""-?(0|[1-9][0-9]*)""")
private val decimalToken = Regex("""-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?""")

internal suspend fun WorkContextApi.readReceipt(target: ReceiptTarget, bearer: String): ReceiptReply = try {
    val id = target.lookup.id
    val task = target.task.taskId
    val response = when (target.lookup.kind) {
        ReceiptKind.Batch -> batchReceipt(bearer, task, id)
        ReceiptKind.Operation -> operationReceipt(bearer, task, id)
    }
    when (response.code()) {
        404 -> {
            response.errorBody()?.close()
            ReceiptReply.Missing
        }
        409 -> {
            response.errorBody()?.close()
            ReceiptReply.Conflict
        }
        else -> responseBody { response }.use { body ->
            ensureResponse(response.code() == 200)
            withContext(Dispatchers.IO) { body.readTypedReceipt(target.lookup.kind) }
        }
    }
} catch (failure: PatrolFailure) {
    if (failure.issue == Issue.UNAUTHENTICATED || failure.issue == Issue.FORBIDDEN) throw failure
    ReceiptReply.Unavailable(failure.issue)
} catch (_: IOException) {
    ReceiptReply.Unavailable(Issue.CONNECTION)
} catch (_: SerializationException) {
    ReceiptReply.Unavailable(Issue.RESPONSE)
} catch (_: IllegalArgumentException) {
    ReceiptReply.Unavailable(Issue.RESPONSE)
}

private fun ResponseBody.readTypedReceipt(kind: ReceiptKind): ReceiptReply {
    val bytes = source()
    ensureResponse(!bytes.request(MAX_RECEIPT_BYTES + 1))
    val body = bytes.readByteArray().decodeToString(throwOnInvalidSequence = true)
    validateObjectKeys(body)
    val json = receiptJson.parseToJsonElement(body) as? JsonObject ?: throw PatrolFailure(Issue.RESPONSE)
    return when (kind) {
        ReceiptKind.Operation -> ReceiptReply.Operation(receiptJson.decodeFromJsonElement(operationJson(json)))
        ReceiptKind.Batch -> {
            for (field in listOf("previousDraftVersion", "draftVersion")) json.requireInteger(field)
            val operations = json["operations"] as? JsonArray ?: throw PatrolFailure(Issue.RESPONSE)
            ensureResponse(operations.size <= 150)
            val normalized = JsonArray(operations.map {
                operationJson(it as? JsonObject ?: throw PatrolFailure(Issue.RESPONSE))
            })
            ReceiptReply.Batch(receiptJson.decodeFromJsonElement(JsonObject(json + ("operations" to normalized))))
        }
    }
}

// The API sends a JSON decimal number. The encrypted local record stores its exact
// decimal value as text, avoiding floating point conversion on either path.
private fun operationJson(json: JsonObject): JsonObject {
    for (field in listOf("canonicalizationVersion", "workflowVersion", "draftVersion")) json.requireInteger(field)
    val removed = json["isRemoved"] as? JsonPrimitive
    ensureResponse(removed != null && !removed.isString && removed.booleanOrNull != null)
    val value = json["value"]
    if (value == JsonNull) return json
    val fields = value as? JsonObject ?: throw PatrolFailure(Issue.RESPONSE)
    val number = fields["inspectionValue"]
    if (number == JsonNull) return json
    val decimal = number as? JsonPrimitive ?: throw PatrolFailure(Issue.RESPONSE)
    ensureResponse(!decimal.isString && decimalToken.matches(decimal.content))
    val normalized = JsonPrimitive(DraftOperationCanonical.decimalText(decimal.content))
    return JsonObject(json + ("value" to JsonObject(fields + ("inspectionValue" to normalized))))
}

private fun JsonObject.requireInteger(field: String) {
    val value = get(field) as? JsonPrimitive
    ensureResponse(value != null && !value.isString && value.longOrNull != null && integerToken.matches(value.content))
}

// Kotlin's tree decoder retains the last duplicate key. Check key uniqueness and
// depth before decoding, including keys written with JSON escapes.
private fun validateObjectKeys(body: String) {
    val scopes = mutableListOf<MutableSet<String>?>()
    var index = 0
    while (index < body.length) {
        when (body[index]) {
            '{', '[' -> {
                ensureResponse(scopes.size < 16)
                scopes.add(if (body[index] == '{') mutableSetOf() else null)
            }
            '}', ']' -> {
                ensureResponse(scopes.isNotEmpty())
                scopes.removeAt(scopes.lastIndex)
            }
            '"' -> {
                val start = index++
                while (index < body.length && body[index] != '"') index += if (body[index] == '\\') 2 else 1
                ensureResponse(index < body.length)
                var next = index + 1
                while (next < body.length && body[next].isWhitespace()) next++
                if (body.getOrNull(next) == ':') {
                    val key = receiptJson.decodeFromString<String>(body.substring(start, index + 1))
                    ensureResponse(scopes.lastOrNull()?.add(key) == true)
                }
            }
        }
        index++
    }
}
