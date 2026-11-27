package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.api.ensureResponse
import com.jaxpower.ipqc.patrol.api.problemCode
import com.jaxpower.ipqc.patrol.api.responseBody
import java.io.IOException
import java.nio.charset.CharacterCodingException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.longOrNull

internal data class BasicInformationQuery(
    val limit: Int = 20,
    val beforeDraftVersion: Long? = null,
    val expectedDraftVersion: Long? = null,
) {
    init {
        require(limit in 1..100 && (beforeDraftVersion == null || beforeDraftVersion > 0) &&
            (expectedDraftVersion == null || expectedDraftVersion >= 0))
    }
}

internal class BasicInformationVersionConflict : Exception()

internal suspend fun WorkContextApi.readBasicInformation(
    bearer: String, task: WorkTask, query: BasicInformationQuery,
): BasicInformationRead = try {
    val response = basicInformation(bearer, task.taskId, task.lineId,
        query.expectedDraftVersion, query.beforeDraftVersion, query.limit)
    if (response.code() == 409 && query.expectedDraftVersion != null) {
        if (response.problemCode()?.code == "BasicInformationVersionConflict") throw BasicInformationVersionConflict()
        throw PatrolFailure(Issue.CONFLICT)
    }
    responseBody { response }.use { body ->
        ensureResponse(response.code() == 200)
        withContext(Dispatchers.IO) { body.readWorkJson().decodeBasicInformation(task, query) }
    }
} catch (_: SerializationException) {
    throw PatrolFailure(Issue.RESPONSE)
} catch (_: CharacterCodingException) {
    throw PatrolFailure(Issue.RESPONSE)
} catch (_: IllegalArgumentException) {
    throw PatrolFailure(Issue.RESPONSE)
} catch (_: IOException) {
    throw PatrolFailure(Issue.CONNECTION)
}

internal fun JsonObject.decodeBasicInformation(task: WorkTask, query: BasicInformationQuery): BasicInformationRead {
    exactKeys("taskId", "lineId", "workflowVersion", "draftVersion", "availability", "values", "fields",
        "acceptedFieldCount", "nonblankFieldCount", "history", "historyBeforeDraftVersion",
        "canExecute", "canSubmit", "acceptsFormalInspection", "completeness", "observation")
    ensureResponse(text("taskId") == task.taskId && text("lineId") == task.lineId &&
        number("workflowVersion") == task.workflowVersion.toLong() &&
        text("completeness") == "OptionalHeaderFactsOnly" && text("observation") == "ReadOnlyPointInTime")
    for (flag in listOf("canExecute", "canSubmit", "acceptsFormalInspection")) requireFalse(flag)
    val version = number("draftVersion")
    ensureResponse(version >= 0 && (query.expectedDraftVersion == null || version == query.expectedDraftVersion))
    val availability = named<BasicInformationAvailability>("availability")
    ensureResponse((availability != BasicInformationAvailability.FoundationOnly || version == 0L) &&
        (availability != BasicInformationAvailability.DraftFoundation || version > 0L))

    val values = this["values"].asObject()
    values.exactKeys("customer", "production", "productMo")
    val fields = array("fields")
    ensureResponse(fields.size == BasicInformationField.entries.size)
    val facts = fields.map { element ->
        val field = element.asObject()
        field.exactKeys("field", "value", "valueState", "hasAcceptedFact", "lastOperation", "isRequired", "blocksSubmit")
        field.requireFalse("isRequired")
        field.requireFalse("blocksSubmit")
        val name = field.named<BasicInformationField>("field")
        val receipt = field["lastOperation"].let { if (it == JsonNull) null else it.asObject().decodeBasicInformationReceipt(task, version) }
        val fact = BasicInformationFact(name, receipt)
        ensureResponse((receipt == null || receipt.operation.field == name) &&
            field["hasAcceptedFact"] == JsonPrimitive(receipt != null) &&
            field.text("valueState") == fact.presence.name && field.nullableText("value") == fact.value &&
            values.nullableText(name.name.replaceFirstChar { it.lowercaseChar() }) == fact.value)
        fact
    }.sortedBy { it.field.ordinal }
    ensureResponse(facts.map { it.field } == BasicInformationField.entries &&
        number("acceptedFieldCount") == facts.count { it.accepted != null }.toLong() &&
        number("nonblankFieldCount") == facts.count { it.presence == BasicInformationPresence.Provided }.toLong())

    val entries = array("history")
    ensureResponse(entries.size <= query.limit)
    val history = entries.map { it.asObject().decodeBasicInformationReceipt(task, version) }
    val cursor = if (this["historyBeforeDraftVersion"] == JsonNull) null else number("historyBeforeDraftVersion")
    validateBasicInformationHistory(facts, history, cursor, query)
    return BasicInformationRead(BasicInformationBasis(task, version, availability, facts), history, cursor)
}

private fun validateBasicInformationHistory(
    fields: List<BasicInformationFact>, history: List<BasicInformationReceipt>, cursor: Long?, query: BasicInformationQuery,
) {
    ensureResponse(history.zipWithNext().all { (newer, older) -> newer.draftVersion > older.draftVersion } &&
        (query.beforeDraftVersion == null || history.all { it.draftVersion < query.beforeDraftVersion }) &&
        (cursor == null || (history.size == query.limit && cursor == history.lastOrNull()?.draftVersion && cursor > 1L)))
    val current = fields.mapNotNull { it.accepted }
    val known = (current + history).distinct()
    val byId = known.associateBy { it.operation.operationId }
    ensureResponse(byId.size == known.size && known.map { it.batchId }.distinct().size == known.size &&
        known.map { it.draftVersion }.distinct().size == known.size)
    for (field in fields) {
        val accepted = field.accepted
        val page = history.filter { it.operation.field == field.field }
        ensureResponse(page.isEmpty() || accepted != null && accepted.draftVersion >= page.first().draftVersion)
        if (accepted != null && (query.beforeDraftVersion == null || accepted.draftVersion < query.beforeDraftVersion) &&
            (cursor == null || accepted.draftVersion >= cursor)) ensureResponse(page.firstOrNull() == accepted)
        for ((newer, older) in page.zipWithNext()) ensureResponse(newer.operation.predecessor == older.reference())
        if (cursor == null) ensureResponse(page.lastOrNull()?.operation?.predecessor == null)
    }
    for (receipt in known) {
        val previous = receipt.operation.predecessor ?: continue
        byId[previous.operationId]?.let { prior ->
            ensureResponse(prior.reference() == previous && prior.operation.field == receipt.operation.field &&
                prior.draftVersion <= receipt.operation.baseDraftVersion)
        }
    }
}

internal fun JsonObject.decodeBasicInformationReceipt(task: WorkTask, version: Long = Long.MAX_VALUE): BasicInformationReceipt {
    exactKeys("taskId", "operationId", "batchId", "canonicalizationVersion", "contentHash", "kind", "field", "value",
        "sourceWorkContextId", "contributorUserId", "actorUserId", "actorPersonalSessionId", "environmentId", "lineId",
        "workflowVersion", "baseDraftVersion", "originLocalEditVersion", "acceptedAtUtc", "draftVersion", "predecessor")
    ensureResponse(text("taskId") == task.taskId && text("lineId") == task.lineId &&
        text("environmentId") == task.environmentId && number("workflowVersion") == task.workflowVersion.toLong() &&
        number("canonicalizationVersion") == CANONICAL_VERSION.toLong())
    val predecessor = this["predecessor"].let {
        if (it == JsonNull) null else it.asObject().let { ref ->
            ref.exactKeys("operationId", "contentHash")
            val hash = ref.text("contentHash")
            ensureResponse(isWorkId(ref.text("operationId")) && hash.length == 64 && hash.all { c -> c in "0123456789ABCDEF" })
            OperationReference(ref.text("operationId"), hash)
        }
    }
    val op = BasicInformationOperation(text("operationId"), named("kind"), named("field"), nullableText("value"),
        text("sourceWorkContextId"), text("contributorUserId"), number("baseDraftVersion"), predecessor)
    val receipt = BasicInformationReceipt(task, op, text("batchId"), text("contentHash"), text("actorUserId"),
        text("actorPersonalSessionId"), number("originLocalEditVersion"), text("acceptedAtUtc"), number("draftVersion"))
    ensureResponse(receipt.matches(task, version))
    return receipt
}

internal fun JsonElement?.asObject() = this as? JsonObject ?: throw PatrolFailure(Issue.RESPONSE)
internal fun JsonObject.exactKeys(vararg names: String) = ensureResponse(keys == names.toSet())
internal fun JsonObject.array(name: String) = this[name] as? JsonArray ?: throw PatrolFailure(Issue.RESPONSE)
internal fun JsonObject.requireFalse(name: String) = ensureResponse(this[name] == JsonPrimitive(false))
internal fun JsonObject.text(name: String) = (this[name] as? JsonPrimitive)?.takeIf { it.isString }?.content
    ?: throw PatrolFailure(Issue.RESPONSE)
private fun JsonObject.nullableText(name: String) = if (this[name] == JsonNull) null else text(name)
private val integerText = Regex("""-?(0|[1-9][0-9]*)""")
internal fun JsonObject.number(name: String) = (this[name] as? JsonPrimitive)
    ?.takeIf { !it.isString && integerText.matches(it.content) }?.longOrNull
    ?: throw PatrolFailure(Issue.RESPONSE)
private inline fun <reified T : Enum<T>> JsonObject.named(name: String): T {
    val value = text(name)
    return enumValues<T>().firstOrNull { it.name == value } ?: throw PatrolFailure(Issue.RESPONSE)
}
