package com.jaxpower.ipqc.patrol.work

import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

internal object ItemRemovalCanonical {
    fun operation(command: ItemRemovalCommand, localEditVersion: Long): String =
        encodeDraftCanonical(buildJsonObject {
            put("domain", "item-removal")
            put("v", CANONICAL_VERSION)
            put("environment", command.task.environmentId)
            put("task", command.task.taskId)
            put("line", command.task.lineId)
            put("workflow", command.task.workflowVersion)
            put("operation", command.operationId)
            put("kind", "RemoveItem")
            put("item", command.attempt.taskItemId)
            put("attempt", command.attempt.inspectionAttemptId)
            put("source", command.contextId)
            put("contributorAssertion", command.owner.userId)
            put("base", command.expectedDraftVersion)
            put("local", localEditVersion)
            put("confirmedAbsentOrNotApplicable", command.confirmedAbsentOrNotApplicable)
            put("confirmationSource", command.confirmation.name)
            put("reason", command.reason)
        })

    fun batch(removal: ItemRemovalEnvelope, id: String): ItemRemovalBatch {
        val command = removal.command
        val fingerprint = workHash(encodeDraftCanonical(buildJsonObject {
            put("domain", "item-removal-batch")
            put("v", CANONICAL_VERSION)
            put("task", command.task.taskId)
            put("batch", id)
            put("environment", command.task.environmentId)
            put("line", command.task.lineId)
            put("workflow", command.task.workflowVersion)
            put("expected", command.expectedDraftVersion)
            put("operationHash", removal.contentHash)
        }))
        return ItemRemovalBatch(id, command.expectedDraftVersion, removal.reference(), CANONICAL_VERSION, fingerprint)
    }
}
