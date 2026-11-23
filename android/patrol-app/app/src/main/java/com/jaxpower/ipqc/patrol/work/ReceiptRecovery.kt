package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.api.Issue
import kotlinx.serialization.Serializable

internal enum class ReceiptKind { Batch, Operation }

internal data class ReceiptLookup(val kind: ReceiptKind, val id: String)

internal data class ReceiptTarget(
    val lookup: ReceiptLookup, val task: WorkTask, val workspaceId: String, val batchId: String, val fingerprint: String,
)

internal enum class ReceiptUncertainty { NotObserved, MissingPredecessor, NoLocalAttempt }

@Serializable
internal enum class ReceiptConflictReason { ResponseMismatch, QueryConflict }

internal sealed interface ReceiptRecovery {
    data class BatchConfirmed(val receipt: ServerBatchReceipt) : ReceiptRecovery
    data class OperationConfirmed(val receipt: ServerOperationReceipt) : ReceiptRecovery
    data class Unresolved(val reason: ReceiptUncertainty) : ReceiptRecovery
    data class Conflict(val reason: ReceiptConflictReason) : ReceiptRecovery
    data class AuthenticationFailure(val issue: Issue) : ReceiptRecovery
    data class TransientFailure(val issue: Issue) : ReceiptRecovery
}
