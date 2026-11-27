package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.api.ensureResponse
import com.jaxpower.ipqc.patrol.api.problemCode
import com.jaxpower.ipqc.patrol.api.responseBody
import java.io.IOException
import java.nio.charset.CharacterCodingException
import java.time.Instant
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

// 120 complete receipts can include three JSON-escaped 2,000-unit fields
// and 50 sample references apiece. Keep that valid page within a finite cap.
internal const val MAX_ORDINARY_NG_READ_BYTES = 8L * 1024 * 1024

internal data class OrdinaryNgQuery(
    val page: Int = 1,
    val pageSize: Int = 20,
    val historyLimit: Int = 20,
    val beforeDraftVersion: Long? = null,
) {
    init {
        require(page >= 1 && pageSize in 1..20 && (page - 1L) * pageSize <= Int.MAX_VALUE &&
            historyLimit in 1..100 && (beforeDraftVersion == null || beforeDraftVersion > 0))
    }
}

internal class OrdinaryNgVersionConflict : Exception()

internal suspend fun WorkContextApi.readOrdinaryNgDetails(
    bearer: String, draft: OnlineDraft, query: OrdinaryNgQuery,
): OrdinaryNgRead = readOrdinaryNgResponse {
    val task = draft.task
    val response = ordinaryNgDetails(bearer, task.taskId, task.lineId, draft.version,
        query.page, query.pageSize, query.historyLimit, query.beforeDraftVersion)
    if (response.code() == 409) {
        when (response.problemCode()?.code) {
            "DraftVersionConflict" -> throw OrdinaryNgVersionConflict()
            "TaskLineMismatch" -> throw PatrolFailure(Issue.LINE_MISMATCH)
            else -> throw PatrolFailure(Issue.CONFLICT)
        }
    }
    responseBody { response }.use { body ->
        ensureResponse(response.code() == 200)
        withContext(Dispatchers.IO) {
            body.readWorkJson(MAX_ORDINARY_NG_READ_BYTES).decodeOrdinaryNgDetails(draft, query)
        }
    }
}

internal suspend fun WorkContextApi.readOrdinaryNgSampleReference(
    bearer: String, draft: OnlineDraft, sample: ObservedSample,
): OperationReference = readOrdinaryNgResponse {
    val task = draft.task
    val response = operationReceipt(bearer, task.taskId, sample.valueOperationId)
    responseBody { response }.use { body ->
        ensureResponse(response.code() == 200)
        withContext(Dispatchers.IO) {
            val receipt = body.readWorkJson().decodeSampleOperationReceipt()
            ensureResponse(receipt.taskId == task.taskId && receipt.lineId == task.lineId &&
                receipt.environmentId == task.environmentId && receipt.workflowVersion == task.workflowVersion &&
                receipt.canonicalizationVersion == CANONICAL_VERSION && receipt.draftVersion in 1..draft.version &&
                receipt.taskItemId == sample.key.attempt.taskItemId && receipt.inspectionAttemptId == sample.key.attempt.inspectionAttemptId &&
                receipt.operationId == sample.valueOperationId && receipt.valueOperationId == sample.valueOperationId &&
                receipt.sampleId == sample.sampleId && receipt.clientSampleId == sample.key.clientSampleId &&
                receipt.contributorUserId == sample.valueContributorId && receipt.value == sample.value &&
                receipt.value?.judgment == "NG" && !receipt.isRemoved && receipt.kind != SampleKind.RemoveSample &&
                isWorkId(receipt.batchId) && isWorkId(receipt.sourceWorkContextId) &&
                isWorkId(receipt.uploadedByUserId) && isWorkId(receipt.uploadedPersonalSessionId) &&
                receipt.acceptedAtUtc.endsWith("Z") && runCatching { Instant.parse(receipt.acceptedAtUtc) }.isSuccess)
            OperationReference(receipt.operationId, receipt.contentHash)
        }
    }
}

private suspend fun <T> readOrdinaryNgResponse(block: suspend () -> T): T = try {
    block()
} catch (_: SerializationException) {
    throw PatrolFailure(Issue.RESPONSE)
} catch (_: CharacterCodingException) {
    throw PatrolFailure(Issue.RESPONSE)
} catch (_: IllegalArgumentException) {
    throw PatrolFailure(Issue.RESPONSE)
} catch (_: IOException) {
    throw PatrolFailure(Issue.CONNECTION)
}

internal fun JsonObject.decodeOrdinaryNgDetails(draft: OnlineDraft, query: OrdinaryNgQuery): OrdinaryNgRead {
    val task = draft.task
    exactKeys("taskId", "lineId", "workflowVersion", "draftVersion", "page", "pageSize", "totalItems", "items",
        "history", "historyBeforeDraftVersion", "canExecute", "canSubmit", "acceptsFormalInspection", "observation")
    ensureResponse(text("taskId") == task.taskId && text("lineId") == task.lineId &&
        number("workflowVersion") == task.workflowVersion.toLong() && number("draftVersion") == draft.version &&
        number("page") == query.page.toLong() && number("pageSize") == query.pageSize.toLong() &&
        number("totalItems") == draft.itemIds.size.toLong() && text("observation") == "ReadOnlyPointInTime")
    for (flag in listOf("canExecute", "canSubmit", "acceptsFormalInspection")) requireFalse(flag)

    val first = ((query.page - 1L) * query.pageSize).toInt()
    val itemIds = draft.itemIds.drop(first).take(query.pageSize)
    val attempts = draft.sampleBasis.attempts.associateBy { it.attempt.taskItemId }
    val items = array("items")
    ensureResponse(items.size == itemIds.size)
    val current = items.zip(itemIds) { element, itemId ->
        val node = element.asObject()
        node.exactKeys("taskItemId", "inspectionAttemptId", "currentState", "latest", "isRequired", "blocksSubmit")
        node.requireFalse("isRequired")
        node.requireFalse("blocksSubmit")
        val attempt = attempts[itemId]?.attempt
        val state = draft.ordinaryNgState(attempts[itemId])
        ensureResponse(node.text("taskItemId") == itemId &&
            node.nullableOrdinaryNgText("inspectionAttemptId") == attempt?.inspectionAttemptId &&
            node.text("currentState") == state.name)
        val latest = node["latest"].takeUnless { it == JsonNull }?.asObject()?.decodeOrdinaryNgObservation(draft, attempts)
        ensureResponse(latest == null || (latest.receipt.operation.attempt == attempt &&
            latest.currentAttempt && latest.latestForAttempt && latest.state == state))
        OrdinaryNgItem(itemId, attempt, state, latest)
    }
    val rawHistory = array("history")
    ensureResponse(rawHistory.size <= query.historyLimit)
    val history = rawHistory.map { it.asObject().decodeOrdinaryNgObservation(draft, attempts) }
    val cursor = if (this["historyBeforeDraftVersion"] == JsonNull) null else number("historyBeforeDraftVersion")
    validateOrdinaryNgHistory(current, history, query, cursor)
    return OrdinaryNgRead(task, draft.version, query.page, query.pageSize, draft.itemIds.size, current, history, cursor)
}

private fun OnlineDraft.ordinaryNgState(attempt: FrozenSampleAttempt?): OrdinaryNgState {
    if (attempt == null) return OrdinaryNgState.AttemptNotCurrent
    if (!validPlanning || availability !in setOf(DraftAvailability.AlreadyInitialized, DraftAvailability.SharedDraftInitialized))
        return OrdinaryNgState.CurrentScopeUnavailable
    return when (attempt.serverDisposition) {
        ItemDisposition.Removed -> OrdinaryNgState.Removed
        ItemDisposition.UnableToMeasureNG -> OrdinaryNgState.UnableToMeasureNG
        ItemDisposition.Applicable -> if (sampleBasis.currentNgSamples(attempt.attempt).isNotEmpty()) OrdinaryNgState.OrdinaryNG
            else OrdinaryNgState.NotCurrentlyNG
        else -> throw PatrolFailure(Issue.RESPONSE)
    }
}

private fun JsonObject.decodeOrdinaryNgObservation(
    draft: OnlineDraft, attempts: Map<String, FrozenSampleAttempt>,
): OrdinaryNgObservation {
    exactKeys("receipt", "isCurrentAttempt", "isLatestForAttempt", "currentState", "hasCurrentOrdinaryNg",
        "matchesAcceptedNgBasis", "isCurrentlyApplicable")
    val receipt = this["receipt"].asObject().decodeOrdinaryNgReceipt(draft.task, draft.version)
    val attempt = receipt.operation.attempt
    ensureResponse(attempt.taskItemId in draft.itemIds && attempt.inspectionAttemptId !in draft.itemIds &&
        draft.sampleBasis.attempts.none { it.attempt.inspectionAttemptId == attempt.inspectionAttemptId && it.attempt != attempt })
    val frozen = attempts[attempt.taskItemId]?.takeIf { it.attempt == attempt }
    val state = draft.ordinaryNgState(frozen)
    val hasOrdinaryNg = state == OrdinaryNgState.OrdinaryNG
    val matches = hasOrdinaryNg && draft.sampleBasis.currentNgSamples(attempt).map { it.valueOperationId }.sorted() ==
        receipt.acceptedNgBasis.map { it.operationId }
    val latest = boolean("isLatestForAttempt")
    val applicable = frozen != null && latest && matches
    ensureResponse(text("currentState") == state.name && boolean("isCurrentAttempt") == (frozen != null) &&
        boolean("hasCurrentOrdinaryNg") == hasOrdinaryNg && boolean("matchesAcceptedNgBasis") == matches &&
        boolean("isCurrentlyApplicable") == applicable)
    return OrdinaryNgObservation(receipt, frozen != null, latest, state, matches, applicable)
}

private fun validateOrdinaryNgHistory(
    items: List<OrdinaryNgItem>, history: List<OrdinaryNgObservation>, query: OrdinaryNgQuery, cursor: Long?,
) {
    val versions = history.map { it.receipt.draftVersion }
    ensureResponse(versions.zipWithNext().all { (newer, older) -> newer > older } &&
        (query.beforeDraftVersion == null || versions.all { it < query.beforeDraftVersion }) &&
        (cursor == null || (cursor > 1 && cursor == versions.lastOrNull() && history.size == query.historyLimit)))
    val all = (items.mapNotNull { it.latest } + history).distinct()
    val receipts = all.associateBy { it.receipt.operation.operationId }
    ensureResponse(receipts.size == all.size && all.map { it.receipt.batchId }.distinct().size == all.size &&
        all.map { it.receipt.draftVersion }.distinct().size == all.size)
    val samples = mutableMapOf<String, Pair<WorkAttempt, String>>()
    for (entry in all) {
        for (sample in entry.receipt.acceptedNgBasis) {
            val scope = entry.receipt.operation.attempt to sample.contentHash
            val existing = samples.put(sample.operationId, scope)
            ensureResponse(sample.operationId !in receipts && (existing == null || existing == scope))
        }
    }
    val byAttempt = history.groupBy { it.receipt.operation.attempt }
    if (query.beforeDraftVersion == null) ensureResponse(byAttempt.values.all { it.first().latestForAttempt })
    for (item in items) {
        val observed = byAttempt[item.attempt].orEmpty()
        val latest = item.latest
        ensureResponse(observed.isEmpty() || (latest != null && observed.first().receipt.draftVersion <= latest.receipt.draftVersion &&
            observed.all { it.latestForAttempt == (it == latest) }))
        if (latest != null && (query.beforeDraftVersion == null || latest.receipt.draftVersion < query.beforeDraftVersion) &&
            (cursor == null || latest.receipt.draftVersion >= cursor)) ensureResponse(observed.firstOrNull() == latest)
    }
    for (entries in byAttempt.values) {
        ensureResponse(entries.drop(1).none { it.latestForAttempt } && entries.zipWithNext().all { (newer, older) ->
            newer.receipt.operation.predecessor == older.receipt.reference()
        } && (cursor != null || entries.last().receipt.operation.predecessor == null))
    }
    for (entry in all) {
        val receipt = entry.receipt
        val prior = receipt.operation.predecessor ?: continue
        receipts[prior.operationId]?.receipt?.let {
            ensureResponse(it.reference() == prior && it.operation.attempt == receipt.operation.attempt &&
                it.draftVersion <= receipt.operation.baseDraftVersion)
        }
    }
}

private fun JsonObject.decodeOrdinaryNgReceipt(task: WorkTask, version: Long): OrdinaryNgReceipt {
    exactKeys("taskId", "operationId", "batchId", "canonicalizationVersion", "contentHash", "kind", "taskItemId",
        "inspectionAttemptId", "sourceWorkContextId", "contributorUserId", "actorUserId", "actorPersonalSessionId",
        "environmentId", "lineId", "workflowVersion", "baseDraftVersion", "originLocalEditVersion", "acceptedAtUtc",
        "draftVersion", "predecessor", "ngType", "ngReason", "remarks", "acceptedNgBasis")
    ensureResponse(text("taskId") == task.taskId && text("lineId") == task.lineId && text("environmentId") == task.environmentId &&
        number("workflowVersion") == task.workflowVersion.toLong() && number("canonicalizationVersion") == CANONICAL_VERSION.toLong())
    val kind = OrdinaryNgKind.entries.firstOrNull { it.name == text("kind") } ?: throw PatrolFailure(Issue.RESPONSE)
    val prior = if (this["predecessor"] == JsonNull) null else this["predecessor"].asObject().ordinaryNgReference()
    val references = array("acceptedNgBasis")
    ensureResponse(references.size in 1..MAX_CURRENT_SAMPLES)
    val operation = OrdinaryNgOperation(text("operationId"), kind, WorkAttempt(text("taskItemId"), text("inspectionAttemptId")),
        text("sourceWorkContextId"), text("contributorUserId"), number("baseDraftVersion"),
        OrdinaryNgText(nullableOrdinaryNgText("ngType"), nullableOrdinaryNgText("ngReason"), nullableOrdinaryNgText("remarks")), prior)
    val receipt = OrdinaryNgReceipt(task, operation, text("batchId"), text("contentHash"), text("actorUserId"),
        text("actorPersonalSessionId"), number("originLocalEditVersion"), text("acceptedAtUtc"), number("draftVersion"),
        references.map { it.asObject().ordinaryNgReference() })
    ensureResponse(receipt.matches(task, version))
    return receipt
}

private fun JsonObject.ordinaryNgReference(): OperationReference {
    exactKeys("operationId", "contentHash")
    return OperationReference(text("operationId"), text("contentHash"))
}

private fun JsonObject.nullableOrdinaryNgText(name: String) = if (this[name] == JsonNull) null else text(name)

private fun JsonObject.boolean(name: String): Boolean = when (this[name]) {
    JsonPrimitive(true) -> true
    JsonPrimitive(false) -> false
    else -> throw PatrolFailure(Issue.RESPONSE)
}
