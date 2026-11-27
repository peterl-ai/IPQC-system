package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.api.Issue
import java.time.Instant
import java.time.temporal.ChronoUnit
import kotlinx.serialization.Serializable

@Serializable
internal data class BasicInformationBatch(
    val batchId: String, val operation: OperationReference, val expectedDraftVersion: Long, val fingerprint: String,
) {
    companion object {
        fun seal(original: BasicInformationEnvelope, id: String): BasicInformationBatch {
            val version = original.command.operation.baseDraftVersion
            val ref = original.reference()
            return BasicInformationBatch(id, ref, version,
                BasicInformationCanonical.fingerprint(original.command.task, id, version, ref))
        }
    }
}

// This typed record is constructed only from a complete ReceiptVersion 6 response.
@Serializable
internal data class BasicInformationBatchReceipt(
    val batchId: String, val fingerprint: String, val previousDraftVersion: Long, val operation: BasicInformationReceipt,
)

@Serializable
internal data class BasicInformationTransport(
    val batch: BasicInformationBatch,
    val state: WorkState = WorkState.Queued,
    val individualReceipt: BasicInformationReceipt? = null,
    val batchReceipt: BasicInformationBatchReceipt? = null,
    val conflict: WorkConflict? = null,
) {
    val confirmed get() = batchReceipt?.operation ?: individualReceipt
}

internal class BasicInformationUploadCandidate(
    val target: ReceiptTarget,
    val original: BasicInformationEnvelope,
    val transport: BasicInformationTransport,
    val source: StoredWorkSource,
) {
    fun preflightInput(): WorkContextPreflightInputWire {
        val task = target.task
        return WorkContextPreflightInputWire(task.lineId, task.workflowVersion, task.environmentId,
            listOf(WorkContextSourceWire(source.source.contextId, source.proof, source.source.contributorUserId)))
    }
}

internal data class WorkbenchBasicInformation(
    val observation: BasicInformationRead,
    val proposals: List<Row>,
    val canCapture: Boolean,
) {
    data class Row(
        val operation: OperationReference,
        val field: BasicInformationField,
        val value: String?,
        val baseDraftVersion: Long,
        val contributorUserId: String,
        val state: WorkState,
        val batchId: String?,
        val confirmed: BasicInformationReceipt?,
        val conflict: WorkConflict?,
        val stale: Boolean,
        val canUpload: Boolean,
    ) {
        override fun toString() = "BasicInformationRow"
    }

    fun canSave(field: BasicInformationField): Boolean = canCapture && proposals.none {
        it.field == field && it.baseDraftVersion == observation.basis.draftVersion &&
            it.state !in setOf(WorkState.Quarantined, WorkState.Conflict)
    }
}

internal sealed interface BasicInformationReceiptReply {
    data class Batch(val receipt: BasicInformationBatchReceipt) : BasicInformationReceiptReply
    data class Operation(val receipt: BasicInformationReceipt) : BasicInformationReceiptReply
    data object Missing : BasicInformationReceiptReply
    data object Conflict : BasicInformationReceiptReply
    data class Unavailable(val issue: Issue) : BasicInformationReceiptReply
}

internal sealed interface BasicInformationReceiptRecovery {
    data class BatchConfirmed(val receipt: BasicInformationBatchReceipt) : BasicInformationReceiptRecovery
    data class OperationObserved(val receipt: BasicInformationReceipt) : BasicInformationReceiptRecovery
    data class Unresolved(val reason: ReceiptUncertainty) : BasicInformationReceiptRecovery
    data class Conflict(val reason: ReceiptConflictReason) : BasicInformationReceiptRecovery
    data class Unavailable(val issue: Issue) : BasicInformationReceiptRecovery
}

internal sealed interface BasicInformationUploadResult {
    data class Accepted(val receipt: BasicInformationBatchReceipt, val replayOnly: Boolean) : BasicInformationUploadResult
    data class NeedsBatchReceipt(val operation: BasicInformationReceipt) : BasicInformationUploadResult
    data class Rejected(val reason: WorkConflict, val submitted: Boolean) : BasicInformationUploadResult
    data class NotSent(val issue: Issue) : BasicInformationUploadResult
    data class OutcomeUnknown(val issue: Issue) : BasicInformationUploadResult
}

internal fun WorkState.unresolvedAttempt() = this == WorkState.InFlight || this == WorkState.OutcomeUnknown

internal fun BasicInformationBasis.hasOriginalPredecessor(op: BasicInformationOperation): Boolean =
    op.predecessor == field(op.field).accepted?.reference()

internal fun BasicInformationEnvelope.matches(receipt: BasicInformationReceipt): Boolean =
    receipt.matches(command.task, Long.MAX_VALUE) && receipt.operation == command.operation &&
        receipt.contentHash == contentHash && receipt.originLocalEditVersion == originLocalEditVersion &&
        Instant.parse(receipt.acceptedAtUtc) >= Instant.parse(source.issuedAtUtc).truncatedTo(ChronoUnit.MICROS)

internal fun BasicInformationTransport.matches(original: BasicInformationEnvelope, receipt: BasicInformationBatchReceipt): Boolean =
    original.matches(receipt.operation) && receipt.batchId == batch.batchId &&
        receipt.operation.batchId == batch.batchId && receipt.fingerprint == batch.fingerprint &&
        receipt.previousDraftVersion == batch.expectedDraftVersion &&
        (confirmed == null || confirmed == receipt.operation)

internal suspend fun WorkTransaction.checkedBasicInformationTransport(original: BasicInformationEnvelope):
    BasicInformationTransport? {
    val transport = basicInformationTransport(original.reference().operationId) ?: return null
    val batchId = transport.batch.batchId
    workCheck(isWorkId(batchId) && transport.batch == BasicInformationBatch.seal(original, batchId) &&
        (transport.state == WorkState.Accepted) == (transport.batchReceipt != null) &&
        (transport.state in setOf(WorkState.Conflict, WorkState.Quarantined)) == (transport.conflict != null) &&
        (transport.state.unresolvedAttempt() || transport.state == WorkState.Accepted || transport.confirmed == null),
        WorkProblem.StorageUnavailable)
    transport.individualReceipt?.let { workCheck(original.matches(it), WorkProblem.StorageUnavailable) }
    transport.batchReceipt?.let {
        workCheck(transport.matches(original, it) &&
            (transport.individualReceipt == null || transport.individualReceipt == it.operation), WorkProblem.StorageUnavailable)
    }
    return transport
}

internal suspend fun WorkTransaction.basicInformationTransports(task: WorkTask):
    List<Pair<BasicInformationEnvelope, BasicInformationTransport>> = catalog().basicInformationIds.mapNotNull { id ->
    val original = checkedBasicInformation(task, id) ?: throw WorkFailure(WorkProblem.StorageUnavailable)
    checkedBasicInformationTransport(original)?.let { original to it }
}

internal suspend fun WorkTransaction.checkBasicInformationBarrier(task: WorkTask) =
    workCheck(basicInformationTransports(task).none { (_, transport) -> transport.state.unresolvedAttempt() },
        WorkProblem.InvalidState)
