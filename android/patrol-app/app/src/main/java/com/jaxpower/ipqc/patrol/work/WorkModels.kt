package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.api.validGuid
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import java.time.Instant
import java.util.Locale
import kotlinx.serialization.Serializable

internal const val CANONICAL_VERSION = 1

internal enum class WorkProblem {
    ScopeMismatch, SourceUnavailable, OperationIdentityConflict, BatchIdentityConflict,
    DependencyMismatch, InvalidState, ReceiptMismatch, StorageUnavailable,
    CaptureTimeUnavailable, CaptureWindowClosed, StaleGrant, IssuanceUncertain,
    SampleInputMismatch, SampleLimit,
}

internal class WorkFailure(val problem: WorkProblem) : Exception(problem.name)

internal fun workCheck(valid: Boolean, problem: WorkProblem) {
    if (!valid) throw WorkFailure(problem)
}

internal fun isWorkId(value: String) = validGuid(value) && value == value.lowercase(Locale.ROOT)

@Serializable
internal data class WorkTask(val environmentId: String, val taskId: String, val lineId: String, val workflowVersion: Int) {
    init {
        require(environmentId.length in 1..200 && environmentId == environmentId.trim() &&
            isWorkId(taskId) && isWorkId(lineId) && workflowVersion == 2)
    }
}

@Serializable
internal data class WorkAttempt(val taskItemId: String, val inspectionAttemptId: String) {
    init { require(isWorkId(taskItemId) && isWorkId(inspectionAttemptId)) }
}

// The future online adapter must construct these from matched task reads and source preflight responses.
// The session stamp is issued only by SessionRepository and becomes stale on rotation or logout.
internal class WorkAccess(
    val session: SessionRepository.WorkSession,
    val task: WorkTask,
    val serverDraftVersion: Long,
    currentAttempts: Set<WorkAttempt>,
    validatedSources: Set<ValidatedWorkSource> = emptySet(),
    securityRevokedSources: Set<String> = emptySet(),
    val requiresCaptureGrant: Boolean = false,
    val captureGrant: CaptureGrant? = null,
    private val current: () -> Boolean = { true },
) {
    fun requireCurrent() = workCheck(current(), WorkProblem.StaleGrant)

    val currentAttempts = currentAttempts.toSet()
    val validatedSources = validatedSources.toSet()
    val securityRevokedSources = securityRevokedSources.toSet()

    init {
        workCheck(task.environmentId == session.identity.environment, WorkProblem.ScopeMismatch)
        require(serverDraftVersion >= 0 && this.currentAttempts.map { it.taskItemId }.distinct().size == this.currentAttempts.size)
    }
}

internal class CaptureGrant(val contextId: String, val until: Instant)

internal data class ValidatedWorkSource(val contextId: String, val contributorUserId: String, val captureUntilUtc: String) {
    init { require(isWorkId(contextId) && isWorkId(contributorUserId)) }
}

@Serializable
internal data class WorkSource(
    val task: WorkTask,
    val contextId: String,
    val contributorUserId: String,
    val originPersonalSessionId: String,
    val issuedAtUtc: String,
    val captureUntilUtc: String,
    val personalSessionExpiresAtUtc: String,
    val contractVersion: Int,
) {
    init {
        val issued = Instant.parse(issuedAtUtc)
        val until = Instant.parse(captureUntilUtc)
        val expires = Instant.parse(personalSessionExpiresAtUtc)
        require(contractVersion == 1 && isWorkId(contextId) && isWorkId(contributorUserId) &&
            isWorkId(originPersonalSessionId) && issued.isBefore(until) &&
            !until.isAfter(expires) && !until.isAfter(issued.plusSeconds(72 * 3600L)))
    }
}

// This type is confined to the protected store and a coordinator preflight callback.
@Serializable
internal class StoredWorkSource(
    val source: WorkSource, val proof: String,
    val issuedAuthVersion: Int? = null, val issuedRevision: Long? = null,
)

@Serializable
internal data class OperationReference(val operationId: String, val contentHash: String) {
    init {
        require(isWorkId(operationId) && contentHash.length == 64 &&
            contentHash.all { it in '0'..'9' || it in 'A'..'F' })
    }
}

@Serializable
internal enum class SampleKind { CaptureSample, CorrectSample, RemoveSample }

@Serializable
internal data class SamplePayload(val judgment: String?, val inspectionValue: String?, val barcode: String?) {
    override fun toString() = "SamplePayload"
}

@Serializable
internal data class SampleOperation(
    val operationId: String,
    val attempt: WorkAttempt,
    val clientSampleId: String,
    val sourceWorkContextId: String,
    val baseDraftVersion: Long,
    val kind: SampleKind,
    val data: SamplePayload?,
    val dependencies: List<OperationReference>,
) {
    init {
        require(isWorkId(operationId) && isWorkId(clientSampleId) && isWorkId(sourceWorkContextId) && baseDraftVersion >= 0)
        require(dependencies.none { it.operationId == operationId })
        require(when (kind) {
            SampleKind.CaptureSample -> data != null && dependencies.isEmpty()
            SampleKind.CorrectSample -> data != null && dependencies.size == 1
            SampleKind.RemoveSample -> data == null && dependencies.size == 1
        })
    }
}

@Serializable
internal class OperationEnvelope(
    val workspaceId: String,
    val operation: SampleOperation,
    val source: WorkSource,
    val originLocalEditVersion: Long,
    val canonicalizationVersion: Int,
    val canonicalPayload: String,
    val contentHash: String,
) {
    fun reference() = OperationReference(operation.operationId, contentHash)
}

@Serializable
internal enum class WorkState { Queued, InFlight, OutcomeUnknown, Accepted, Conflict, Quarantined }

@Serializable
internal enum class WorkConflict {
    DraftVersionConflict, TaskScopeConflict, SourceSecurityRevoked, SourceProvenanceConflict, BatchInputRejected,
}

@Serializable
internal data class ConflictEvidence(val reason: WorkConflict, val observedDraftVersion: Long)

@Serializable
internal data class BatchRecord(
    val batchId: String,
    val expectedDraftVersion: Long,
    val operations: List<OperationReference>,
    val acceptedReferences: List<OperationReference>,
    val canonicalizationVersion: Int,
    val fingerprint: String,
    val state: WorkState = WorkState.Queued,
    val conflict: ConflictEvidence? = null,
    val receipt: ServerBatchReceipt? = null,
)

internal class WorkOperation(
    val envelope: OperationEnvelope, val batchId: String?, val state: WorkState, val conflict: ConflictEvidence?,
)

@Serializable
internal data class ServerSampleValue(
    val inspectionType: String, val inspectionValue: String?, val judgment: String, val barcode: String?,
) {
    override fun toString() = "ServerSampleValue"
}

// The adapter supplies these from authenticated responses. Decimal strings preserve the wire numbers exactly.
@Serializable
internal data class ServerOperationReceipt(
    val taskId: String, val operationId: String, val batchId: String, val canonicalizationVersion: Int,
    val contentHash: String, val kind: SampleKind, val taskItemId: String, val inspectionAttemptId: String,
    val sampleId: String, val clientSampleId: String, val sourceWorkContextId: String, val contributorUserId: String,
    val uploadedByUserId: String, val uploadedPersonalSessionId: String, val environmentId: String,
    val lineId: String, val workflowVersion: Int, val acceptedAtUtc: String, val draftVersion: Long,
    val value: ServerSampleValue?, val isRemoved: Boolean, val valueOperationId: String,
)

@Serializable
internal data class ServerBatchReceipt(
    val taskId: String,
    val batchId: String,
    val requestFingerprint: String,
    val uploadedByUserId: String,
    val uploadedPersonalSessionId: String,
    val acceptedAtUtc: String,
    val previousDraftVersion: Long,
    val draftVersion: Long,
    val operations: List<ServerOperationReceipt>,
)
