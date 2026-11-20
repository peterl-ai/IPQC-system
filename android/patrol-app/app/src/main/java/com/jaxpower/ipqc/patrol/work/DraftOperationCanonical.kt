package com.jaxpower.ipqc.patrol.work

import java.math.BigDecimal
import java.security.MessageDigest
import java.util.Locale
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

// Matches DraftCanonical.Operation and Batch for the sample V1 domain.
internal object DraftOperationCanonical {
    private val maxDecimal = BigDecimal("79228162514264337593543950335")
    private val decimal = Regex("[+-]?[0-9]+(\\.[0-9]+)?([eE][+-]?[0-9]+)?")

    fun normalize(op: SampleOperation): SampleOperation {
        val data = op.data?.let {
            require(it.barcode == null || (it.barcode.length <= 200 && it.barcode.none(Char::isISOControl)))
            require(if (it.inspectionValue == null) (it.judgment == "OK" || it.judgment == "NG") && it.barcode == null
                else it.judgment == null)
            it.copy(inspectionValue = it.inspectionValue?.let(::decimalText))
        }
        return op.copy(data = data, dependencies = op.dependencies.toList())
    }

    fun operation(source: WorkSource, op: SampleOperation, localEditVersion: Long): String = encode(buildJsonObject {
        put("v", CANONICAL_VERSION)
        put("environment", source.task.environmentId)
        put("task", source.task.taskId)
        put("line", source.task.lineId)
        put("workflow", source.task.workflowVersion)
        put("operation", op.operationId)
        put("kind", op.kind.name)
        put("item", op.attempt.taskItemId)
        put("attempt", op.attempt.inspectionAttemptId)
        put("clientSample", op.clientSampleId)
        put("source", source.contextId)
        put("contributorAssertion", source.contributorUserId)
        put("base", op.baseDraftVersion)
        put("local", localEditVersion)
        put("data", op.data?.let {
            buildJsonObject {
                put("judgment", it.judgment)
                put("value", it.inspectionValue)
                put("barcode", it.barcode)
            }
        } ?: kotlinx.serialization.json.JsonNull)
        put("dependencies", references(op.dependencies))
    })

    fun batch(task: WorkTask, batchId: String, expected: Long, members: List<OperationReference>,
              accepted: List<OperationReference>): String = workHash(encode(buildJsonObject {
        put("v", CANONICAL_VERSION)
        put("task", task.taskId)
        put("batch", batchId)
        put("environment", task.environmentId)
        put("line", task.lineId)
        put("workflow", task.workflowVersion)
        put("expected", expected)
        put("operations", references(members))
        put("references", references(accepted))
    }))

    private fun references(values: List<OperationReference>) = buildJsonArray {
        values.sortedBy { it.operationId }.forEach {
            add(JsonArray(listOf(JsonPrimitive(it.operationId), JsonPrimitive(it.contentHash))))
        }
    }

    private fun decimalText(text: String): String {
        require(text.length <= 64 && decimal.matches(text))
        val parsed = requireNotNull(text.toBigDecimalOrNull())
        if (parsed.signum() == 0) return "0"
        val value = parsed.stripTrailingZeros()
        val exponent = value.precision().toLong() - value.scale() - 1
        require(exponent in -28L..28L && value.scale() <= 28 && value.abs() <= maxDecimal)
        // Decimal fits only if the exact unsigned coefficient fits in 96 bits.
        require(value.setScale(maxOf(0, value.scale())).unscaledValue().abs().bitLength() <= 96)
        if (exponent >= -4) return value.toPlainString()
        val coefficient = value.movePointRight(-exponent.toInt()).toPlainString()
        return coefficient + "E-" + (-exponent).toString().padStart(2, '0')
    }

    private fun encode(value: JsonElement): String = when (value) {
        is JsonObject -> value.entries.joinToString(",", "{", "}") { quote(it.key) + ":" + encode(it.value) }
        is JsonArray -> value.joinToString(",", "[", "]", transform = ::encode)
        is JsonPrimitive -> if (value.isString) quote(value.content) else value.content
    }

    // Utf8JsonWriter's default encoder escapes non-ASCII code units and these ASCII characters.
    private fun quote(value: String) = buildString {
        append('"')
        value.forEachIndexed { index, ch ->
            if (ch.isHighSurrogate()) require(value.getOrNull(index + 1)?.isLowSurrogate() == true)
            if (ch.isLowSurrogate()) require(value.getOrNull(index - 1)?.isHighSurrogate() == true)
            when (ch) {
                '\\' -> append("\\\\")
                '\n' -> append("\\n")
                '\r' -> append("\\r")
                '\t' -> append("\\t")
                '\b' -> append("\\b")
                '\u000C' -> append("\\f")
                else -> if (ch in ' '..'~' && ch !in "\"&'+<>\u0060") append(ch)
                    else append("\\u").append(ch.code.toString(16).uppercase(Locale.ROOT).padStart(4, '0'))
            }
        }
        append('"')
    }
}

internal fun workHash(value: String): String = MessageDigest.getInstance("SHA-256")
    .digest(value.encodeToByteArray()).joinToString("") { "%02X".format(Locale.ROOT, it) }
