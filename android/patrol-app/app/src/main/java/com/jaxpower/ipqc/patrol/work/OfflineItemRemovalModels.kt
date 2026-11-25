package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import java.time.Instant
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonElement

@Serializable
internal enum class ItemDisposition { Applicable, Removed, UnableToMeasureNG }

@Serializable
internal enum class RemovalConfirmation { Production, Equipment }

@Serializable
internal data class ItemRemovalCommand(
    val task: WorkTask,
    val attempt: WorkAttempt,
    val operationId: String,
    val owner: SessionIdentity,
    val contextId: String,
    val expectedDraftVersion: Long,
    val confirmedAbsentOrNotApplicable: Boolean,
    val confirmation: RemovalConfirmation,
    val reason: String?,
) {
    fun isValid() = isWorkId(operationId) && isWorkId(contextId) &&
        expectedDraftVersion in 0 until Long.MAX_VALUE &&
        confirmedAbsentOrNotApplicable && validRemovalReason(reason)
}

internal fun validRemovalReason(reason: String?): Boolean =
    reason == null || (reason.length <= 500 && reason.none(Char::isISOControl) &&
        reason.indices.all { index ->
            val ch = reason[index]
            (!ch.isHighSurrogate() || reason.getOrNull(index + 1)?.isLowSurrogate() == true) &&
                (!ch.isLowSurrogate() || reason.getOrNull(index - 1)?.isHighSurrogate() == true)
        })

@Serializable
internal data class ItemRemovalEnvelope(
    val workspaceId: String,
    val command: ItemRemovalCommand,
    val source: WorkSource,
    val localEditVersion: Long,
    val minimumSampleCount: Int,
    val canonicalizationVersion: Int,
    val canonicalPayload: String,
    val contentHash: String,
    val capture: OfflineCaptureEvidence,
) {
    fun reference() = OperationReference(command.operationId, contentHash)
}

@Serializable
internal data class ItemRemovalBatch(
    val batchId: String,
    val expectedDraftVersion: Long,
    val operation: OperationReference,
    val canonicalizationVersion: Int,
    val fingerprint: String,
)

@Serializable
internal data class ItemRemovalEntry(
    val removal: ItemRemovalEnvelope,
    val batch: ItemRemovalBatch? = null,
    val state: WorkState = WorkState.Queued,
    val individualReceipt: ItemRemovalReceipt? = null,
    val batchReceipt: ItemRemovalBatchReceipt? = null,
) {
    val serverEvidence get() = batchReceipt?.itemOperations?.single() ?: individualReceipt
}

@Serializable
internal data class ItemRemovalReceipt(
    val taskId: String,
    val operationId: String,
    val batchId: String,
    val kind: String,
    val canonicalizationVersion: Int,
    val contentHash: String,
    val taskItemId: String,
    val inspectionAttemptId: String,
    val sourceWorkContextId: String,
    val contributorUserId: String,
    val uploadedByUserId: String,
    val uploadedPersonalSessionId: String,
    val environmentId: String,
    val lineId: String,
    val workflowVersion: Int,
    val acceptedAtUtc: String,
    val draftVersion: Long,
    val disposition: ItemDisposition,
    val confirmedAbsentOrNotApplicable: Boolean,
    val confirmationSource: RemovalConfirmation,
    val reason: String?,
    val minimumSampleCount: Int,
    val remainingApplicableItemCount: Int,
    val warnsLastApplicableItemRemoval: Boolean,
) {
    fun matches(task: WorkTask, attempt: WorkAttempt, minimum: Int): Boolean =
        taskId == task.taskId && lineId == task.lineId && environmentId == task.environmentId &&
            workflowVersion == task.workflowVersion && taskItemId == attempt.taskItemId &&
            inspectionAttemptId == attempt.inspectionAttemptId && minimumSampleCount == minimum &&
            kind == "RemoveItem" && canonicalizationVersion == CANONICAL_VERSION &&
            contentHash.length == 64 && contentHash.all { it in '0'..'9' || it in 'A'..'F' } &&
            listOf(operationId, batchId, sourceWorkContextId, contributorUserId, uploadedByUserId,
                uploadedPersonalSessionId).all(::isWorkId) &&
            acceptedAtUtc.endsWith("Z") && runCatching { Instant.parse(acceptedAtUtc) }.isSuccess &&
            draftVersion > 0 && disposition == ItemDisposition.Removed &&
            confirmedAbsentOrNotApplicable && validRemovalReason(reason) &&
            minimum in 1..50 && remainingApplicableItemCount >= 0 &&
            warnsLastApplicableItemRemoval == (remainingApplicableItemCount == 0)
}

@Serializable
internal data class ItemRemovalBatchReceipt(
    val taskId: String,
    val batchId: String,
    val requestFingerprint: String,
    val uploadedByUserId: String,
    val uploadedPersonalSessionId: String,
    val acceptedAtUtc: String,
    val previousDraftVersion: Long,
    val draftVersion: Long,
    val operations: List<JsonElement>,
    val itemOperations: List<ItemRemovalReceipt>,
    val receiptVersion: Int,
)

internal sealed interface ItemRemovalReceiptRecovery {
    data class BatchConfirmed(val receipt: ItemRemovalBatchReceipt) : ItemRemovalReceiptRecovery
    data class OperationObserved(val receipt: ItemRemovalReceipt) : ItemRemovalReceiptRecovery
    data class Unresolved(val reason: ReceiptUncertainty) : ItemRemovalReceiptRecovery
    data class Conflict(val reason: ReceiptConflictReason) : ItemRemovalReceiptRecovery
    data class AuthenticationFailure(val issue: Issue) : ItemRemovalReceiptRecovery
    data class TransientFailure(val issue: Issue) : ItemRemovalReceiptRecovery
}

internal data class ProjectedItemDisposition(
    val attempt: WorkAttempt,
    val minimumSampleCount: Int,
    val server: ItemDisposition?,
    val serverRemoval: ItemRemovalReceipt?,
    val provisionalRemoval: OperationReference?,
) {
    val serverSampleRequirement get() = minimumSampleCount.takeIf { server == ItemDisposition.Applicable }
    val localSampleRequirement get() = serverSampleRequirement.takeIf { provisionalRemoval == null }
}

internal data class LocalItemRemovalHistory(
    val removal: ItemRemovalEnvelope,
    val state: WorkState,
    val currentAttempt: Boolean,
    val batch: ItemRemovalBatch?,
    val receipt: ItemRemovalReceipt?,
)

internal data class LocalItemDispositionDraft(
    val serverDraftVersion: Long,
    val localEditVersion: Long,
    val items: List<ProjectedItemDisposition>,
    val samples: List<ObservedSample>,
    val history: List<LocalItemRemovalHistory>,
    val historicalServerRemovals: List<ItemRemovalReceipt>,
    val requiresFreshServerDraft: Boolean,
    val sampleBlocks: Set<WorkAttempt> = emptySet(),
    val blocksNewRemoval: Boolean = false,
) {
    val serverAllItemsRemoved get() = items.isNotEmpty() && items.all { it.server == ItemDisposition.Removed }
    val provisionallyAllItemsRemoved get() = items.any { it.provisionalRemoval != null } &&
        items.all { it.server == ItemDisposition.Removed || it.provisionalRemoval != null }
}

internal data class WorkbenchRemoval(
    val items: List<Item>,
    val rows: List<Row>,
    val sampleBlocks: Set<WorkAttempt>,
    val blocksNewRemoval: Boolean,
    val requiresRefresh: Boolean,
    val serverAllItemsRemoved: Boolean,
    val provisionallyAllItemsRemoved: Boolean,
) {
    data class Item(val attempt: WorkAttempt, val server: ItemDisposition?, val warnedLast: Boolean)
    data class Row(
        val attempt: WorkAttempt,
        val operation: OperationReference,
        val batchId: String?,
        val state: WorkState,
        val currentAttempt: Boolean,
        val operationConfirmed: Boolean,
        val warnedLast: Boolean,
    )
}

internal fun LocalItemDispositionDraft.forWorkbench() = WorkbenchRemoval(
    items.map { WorkbenchRemoval.Item(it.attempt, it.server, it.serverRemoval?.warnsLastApplicableItemRemoval == true) },
    history.map {
        WorkbenchRemoval.Row(it.removal.command.attempt, it.removal.reference(), it.batch?.batchId, it.state,
            it.currentAttempt, it.receipt != null, it.receipt?.warnsLastApplicableItemRemoval == true)
    },
    sampleBlocks, blocksNewRemoval, requiresFreshServerDraft, serverAllItemsRemoved, provisionallyAllItemsRemoved,
)
