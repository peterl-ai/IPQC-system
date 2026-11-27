package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.IDENTITY
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.NOW
import com.jaxpower.ipqc.patrol.work.*
import kotlinx.serialization.json.*
import okhttp3.ResponseBody.Companion.toResponseBody
import retrofit2.Response

internal fun WorkbenchDeviceFixture.answerNg(
    target: WorkAttempt,
    version: Long = 7,
    valueOperationId: String = benchId(131),
    prior: OrdinaryNgReceipt? = null,
    state: OrdinaryNgState = OrdinaryNgState.OrdinaryNG,
): OperationReference {
    val task = scope()
    val reference = OperationReference(valueOperationId, (if (valueOperationId == benchId(131)) "A" else "B").repeat(64))
    val effective = if (state == OrdinaryNgState.AttemptNotCurrent)
        WorkAttempt(target.taskItemId, benchId(144)) else target
    val sample = DraftSampleWire(benchId(133), target.taskItemId, target.inspectionAttemptId, benchId(134), 1,
        DraftSampleValueWire("Qualitative", null, "NG", null), false, valueOperationId, IDENTITY.userId, valueOperationId, true)
    wire.sharedDraftVersionOverride = version
    wire.ngDraftTransform = { draft ->
        val chosen = draft.currentAttempts.map {
            if (it.taskItemId == target.taskItemId)
                DraftAttemptWire(effective.taskItemId, effective.inspectionAttemptId, it.minimumSampleCount) else it
        }
        val items = draft.items?.map { item ->
            if (item.taskItemId == target.taskItemId) {
                val disposition = when (state) {
                    OrdinaryNgState.Removed -> "Removed"
                    OrdinaryNgState.UnableToMeasureNG -> "UnableToMeasureNG"
                    else -> "Applicable"
                }
                DraftItemWire(item.taskItemId, effective.inspectionAttemptId, item.minimumSampleCount, disposition, null)
            } else item
        }
        if (draft.taskId == task.taskId) SharedDraftWire(draft.taskId, draft.lineId, draft.workflowVersion,
            draft.draftVersion, if (state == OrdinaryNgState.CurrentScopeUnavailable) "HistoricalInspectionData"
                else draft.availability, chosen,
            draft.samples + listOfNotNull(sample.takeIf { state == OrdinaryNgState.OrdinaryNG }),
            draft.canExecute, draft.completeness, items, draft.allItemsRemoved) else draft
    }
    wire.ngSampleReply = { id ->
        check(id == valueOperationId && state == OrdinaryNgState.OrdinaryNG)
        val evidence = ServerOperationReceipt(task.taskId, id, benchId(135), 1, reference.contentHash, SampleKind.CaptureSample,
            target.taskItemId, target.inspectionAttemptId, sample.sampleId, sample.clientSampleId, benchId(136),
            IDENTITY.userId, IDENTITY.userId, IDENTITY.familyId, task.environmentId, task.lineId, task.workflowVersion,
            NOW, 3, ServerSampleValue("Qualitative", null, "NG", null), false, id)
        Response.success(Json.encodeToString(evidence).toResponseBody())
    }
    wire.ngReply = { incomingTask, line, pin, page, pageSize, limit, before ->
        check(incomingTask == task.taskId && line == task.lineId && pin == version)
        val payload = buildJsonObject {
            put("taskId", task.taskId); put("lineId", task.lineId); put("workflowVersion", task.workflowVersion)
            put("draftVersion", version); put("page", page); put("pageSize", pageSize); put("totalItems", definitions.size)
            val current = definitions.sortedBy { it.sequenceNo }.mapIndexed { i, definition ->
                val chosen = definition.id == target.taskItemId
                val ordinaryState = when {
                    state == OrdinaryNgState.CurrentScopeUnavailable -> state
                    !chosen || state == OrdinaryNgState.AttemptNotCurrent -> OrdinaryNgState.NotCurrentlyNG
                    else -> state
                }
                val at = if (chosen) effective else attempt(task.taskId, i)
                buildJsonObject {
                    put("taskItemId", at.taskItemId); put("inspectionAttemptId", at.inspectionAttemptId)
                    put("currentState", ordinaryState.name); put("isRequired", false); put("blocksSubmit", false)
                    put("latest", prior?.takeIf { chosen && effective == target }?.let { ngObservation(it, state, reference) } ?: JsonNull)
                }
            }.drop((page - 1) * pageSize).take(pageSize)
            put("items", JsonArray(current))
            val history = listOfNotNull(prior?.takeIf { before == null || it.draftVersion < before })
                .take(limit).map { ngObservation(it, state, reference) }
            put("history", JsonArray(history)); put("historyBeforeDraftVersion", JsonNull)
            put("canExecute", false); put("canSubmit", false); put("acceptsFormalInspection", false)
            put("observation", "ReadOnlyPointInTime")
        }
        Response.success(payload.jsonResponse())
    }
    return reference
}

private fun ngObservation(receipt: OrdinaryNgReceipt, state: OrdinaryNgState, reference: OperationReference) = buildJsonObject {
    val matches = state == OrdinaryNgState.OrdinaryNG && receipt.acceptedNgBasis == listOf(reference)
    put("receipt", ngReceiptJson(receipt)); put("isCurrentAttempt", state != OrdinaryNgState.AttemptNotCurrent)
    put("isLatestForAttempt", true); put("currentState", state.name)
    put("hasCurrentOrdinaryNg", state == OrdinaryNgState.OrdinaryNG)
    put("matchesAcceptedNgBasis", matches); put("isCurrentlyApplicable", matches)
}

private fun ngReceiptJson(receipt: OrdinaryNgReceipt) = buildJsonObject {
    val op = receipt.operation
    val task = receipt.task
    put("taskId", task.taskId); put("operationId", op.operationId); put("batchId", receipt.batchId)
    put("canonicalizationVersion", 1); put("contentHash", receipt.contentHash); put("kind", op.kind.name)
    put("taskItemId", op.attempt.taskItemId); put("inspectionAttemptId", op.attempt.inspectionAttemptId)
    put("sourceWorkContextId", op.sourceWorkContextId); put("contributorUserId", op.contributorUserId)
    put("actorUserId", receipt.actorUserId); put("actorPersonalSessionId", receipt.actorPersonalSessionId)
    put("environmentId", task.environmentId); put("lineId", task.lineId); put("workflowVersion", task.workflowVersion)
    put("baseDraftVersion", op.baseDraftVersion); put("originLocalEditVersion", receipt.originLocalEditVersion)
    put("acceptedAtUtc", receipt.acceptedAtUtc); put("draftVersion", receipt.draftVersion)
    put("predecessor", op.predecessor?.let(::ngReference) ?: JsonNull)
    put("ngType", op.text.ngType); put("ngReason", op.text.ngReason); put("remarks", op.text.remarks)
    put("acceptedNgBasis", JsonArray(receipt.acceptedNgBasis.map(::ngReference)))
}

private fun ngReference(item: OperationReference) = buildJsonObject {
    put("operationId", item.operationId); put("contentHash", item.contentHash)
}
