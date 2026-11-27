package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.NOW
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.work.*
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.ResponseBody.Companion.toResponseBody
import retrofit2.Response

internal fun JsonObject.jsonResponse() = toString().toResponseBody("application/json".toMediaType())

internal fun headerAccepted(original: BasicInformationEnvelope, batch: BasicInformationBatch, uploader: SessionIdentity):
    BasicInformationBatchReceipt {
    val accepted = BasicInformationReceipt(original.command.task, original.command.operation, batch.batchId, original.contentHash,
        uploader.userId, uploader.familyId, original.originLocalEditVersion, NOW, batch.expectedDraftVersion + 1)
    return BasicInformationBatchReceipt(batch.batchId, batch.fingerprint, batch.expectedDraftVersion, accepted)
}

internal fun headerBatchJson(full: BasicInformationBatchReceipt) = buildJsonObject {
    val operation = full.operation
    put("receiptVersion", 6)
    put("taskId", operation.task.taskId)
    put("batchId", full.batchId)
    put("requestFingerprint", full.fingerprint)
    put("uploadedByUserId", operation.actorUserId)
    put("uploadedPersonalSessionId", operation.actorPersonalSessionId)
    put("acceptedAtUtc", operation.acceptedAtUtc)
    put("previousDraftVersion", full.previousDraftVersion)
    put("draftVersion", operation.draftVersion)
    put("operations", JsonArray(emptyList()))
    put("headerOperations", JsonArray(listOf(wireBasicInformationReceipt(operation))))
}

internal fun headerUploadJson(full: BasicInformationBatchReceipt, replay: Boolean = false, batch: Boolean = true) = buildJsonObject {
    put("batchReceipt", if (batch) headerBatchJson(full) else JsonNull)
    put("operation", wireBasicInformationReceipt(full.operation))
    put("replayOnly", replay)
    put("canExecute", false)
    put("canSubmit", false)
    put("acceptsFormalInspection", false)
}

internal fun WorkbenchDeviceFixture.respondBasicInformation(
    version: Long, vararg accepted: BasicInformationReceipt, history: List<BasicInformationReceipt> = accepted.toList(),
    beforeRead: (Long?) -> Unit = {},
) {
    val task = scope()
    val facts = BasicInformationField.entries.map { name -> BasicInformationFact(name, accepted.singleOrNull { it.operation.field == name }) }
    val snapshot = buildJsonObject {
        put("taskId", task.taskId); put("lineId", task.lineId); put("workflowVersion", task.workflowVersion)
        put("draftVersion", version); put("availability", if (version == 0L) "FoundationOnly" else "DraftFoundation")
        put("values", buildJsonObject { facts.forEach { put(it.field.name.replaceFirstChar(Char::lowercaseChar), it.value) } })
        put("fields", JsonArray(facts.map {
            buildJsonObject {
                put("field", it.field.name); put("value", it.value); put("valueState", it.presence.name)
                put("hasAcceptedFact", it.accepted != null); put("lastOperation", it.accepted?.let(::wireBasicInformationReceipt) ?: JsonNull)
                put("isRequired", false); put("blocksSubmit", false)
            }
        }))
        put("acceptedFieldCount", accepted.size)
        put("nonblankFieldCount", facts.count { it.presence == BasicInformationPresence.Provided })
        put("canExecute", false); put("canSubmit", false); put("acceptsFormalInspection", false)
        put("completeness", "OptionalHeaderFactsOnly"); put("observation", "ReadOnlyPointInTime")
    }
    wire.headerReply = { expected, before, limit ->
        check(expected == null || expected == version)
        beforeRead(before)
        val eligible = history.filter { before == null || it.draftVersion < before }.sortedByDescending { it.draftVersion }
        val page = eligible.take(limit)
        val body = JsonObject(snapshot + buildJsonObject {
            put("history", JsonArray(page.map(::wireBasicInformationReceipt)))
            put("historyBeforeDraftVersion", page.lastOrNull()?.draftVersion.takeIf { page.size < eligible.size })
        })
        Response.success(body.jsonResponse())
    }
}

internal fun wireBasicInformationReceipt(r: BasicInformationReceipt) = buildJsonObject {
    val op = r.operation
    put("taskId", r.task.taskId); put("operationId", op.operationId); put("batchId", r.batchId)
    put("canonicalizationVersion", 1); put("contentHash", r.contentHash); put("kind", op.kind.name)
    put("field", op.field.name); put("value", op.value); put("sourceWorkContextId", op.sourceWorkContextId)
    put("contributorUserId", op.contributorUserId); put("actorUserId", r.actorUserId)
    put("actorPersonalSessionId", r.actorPersonalSessionId); put("environmentId", r.task.environmentId)
    put("lineId", r.task.lineId); put("workflowVersion", r.task.workflowVersion); put("baseDraftVersion", op.baseDraftVersion)
    put("originLocalEditVersion", r.originLocalEditVersion); put("acceptedAtUtc", r.acceptedAtUtc)
    put("draftVersion", r.draftVersion)
    put("predecessor", op.predecessor?.let {
        buildJsonObject { put("operationId", it.operationId); put("contentHash", it.contentHash) }
    } ?: JsonNull)
}
