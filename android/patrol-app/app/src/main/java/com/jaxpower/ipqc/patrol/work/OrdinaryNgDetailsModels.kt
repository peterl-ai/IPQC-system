package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.time.Instant

@Serializable
internal enum class OrdinaryNgKind { SetOrdinaryNgDetails, CorrectOrdinaryNgDetails }

@Serializable
internal data class OrdinaryNgText(val ngType: String?, val ngReason: String?, val remarks: String?) {
    fun isValid(): Boolean = listOf(ngType, ngReason, remarks).all(::validOrdinaryNgText)
    override fun toString() = "OrdinaryNgText"
}

internal fun validOrdinaryNgText(text: String?): Boolean {
    if (text == null) return true
    if (text.length > 2_000) return false
    var index = 0
    while (index < text.length) {
        val ch = text[index++]
        if (ch.isISOControl() || ch.isLowSurrogate()) return false
        if (ch.isHighSurrogate() && text.getOrNull(index++)?.isLowSurrogate() != true) return false
    }
    return true
}

@Serializable
internal data class OrdinaryNgOperation(
    val operationId: String,
    val kind: OrdinaryNgKind,
    val attempt: WorkAttempt,
    val sourceWorkContextId: String,
    val contributorUserId: String,
    val baseDraftVersion: Long,
    val text: OrdinaryNgText,
    val predecessor: OperationReference?,
) {
    fun isValid() = isWorkId(operationId) && isWorkId(sourceWorkContextId) && isWorkId(contributorUserId) &&
        baseDraftVersion in 0 until Long.MAX_VALUE && text.isValid() &&
        (predecessor == null) == (kind == OrdinaryNgKind.SetOrdinaryNgDetails) && predecessor?.operationId != operationId
    override fun toString() = "OrdinaryNgOperation"
}

internal object OrdinaryNgCanonical {
    fun operation(task: WorkTask, op: OrdinaryNgOperation, localVersion: Long): String =
        encodeDraftCanonical(buildJsonObject {
            put("domain", "ordinary-ng-details")
            put("v", CANONICAL_VERSION)
            put("environment", task.environmentId)
            put("task", task.taskId)
            put("line", task.lineId)
            put("workflow", task.workflowVersion)
            put("operation", op.operationId)
            put("kind", op.kind.name)
            put("item", op.attempt.taskItemId)
            put("attempt", op.attempt.inspectionAttemptId)
            put("source", op.sourceWorkContextId)
            put("contributorAssertion", op.contributorUserId)
            put("base", op.baseDraftVersion)
            put("local", localVersion)
            put("ngType", op.text.ngType)
            put("ngReason", op.text.ngReason)
            put("remarks", op.text.remarks)
            put("predecessor", op.predecessor?.let { prior ->
                buildJsonObject {
                    put("operation", prior.operationId)
                    put("hash", prior.contentHash)
                }
            } ?: JsonNull)
        })

    fun fingerprint(task: WorkTask, batchId: String, expected: Long, operation: OperationReference): String {
        require(isWorkId(batchId) && expected >= 0)
        return workHash(encodeDraftCanonical(buildJsonObject {
            put("domain", "ordinary-ng-details-batch")
            put("v", CANONICAL_VERSION)
            put("task", task.taskId)
            put("batch", batchId)
            put("environment", task.environmentId)
            put("line", task.lineId)
            put("workflow", task.workflowVersion)
            put("expected", expected)
            put("operationHash", operation.contentHash)
        }))
    }
}

@Serializable
internal data class OrdinaryNgReceipt(
    val task: WorkTask,
    val operation: OrdinaryNgOperation,
    val batchId: String,
    val contentHash: String,
    val actorUserId: String,
    val actorPersonalSessionId: String,
    val originLocalEditVersion: Long,
    val acceptedAtUtc: String,
    val draftVersion: Long,
    val acceptedNgBasis: List<OperationReference>,
) {
    fun reference() = OperationReference(operation.operationId, contentHash)

    fun matches(scope: WorkTask, version: Long): Boolean =
        task == scope && operation.isValid() && operation.baseDraftVersion > 0 &&
            isWorkId(batchId) && isWorkId(actorUserId) && isWorkId(actorPersonalSessionId) &&
            originLocalEditVersion >= 0 && draftVersion == operation.baseDraftVersion + 1 && draftVersion <= version &&
            acceptedAtUtc.endsWith("Z") && runCatching { Instant.parse(acceptedAtUtc) }.isSuccess &&
            acceptedNgBasis.isSortedSampleBasis() &&
            acceptedNgBasis.none { it.operationId == operation.operationId || it.operationId == operation.predecessor?.operationId } &&
            contentHash == workHash(OrdinaryNgCanonical.operation(task, operation, originLocalEditVersion))

    override fun toString() = "OrdinaryNgReceipt"
}

internal fun List<OperationReference>.isSortedSampleBasis() =
    size in 1..MAX_CURRENT_SAMPLES && zipWithNext().all { (first, next) -> first.operationId < next.operationId }

internal enum class OrdinaryNgState {
    OrdinaryNG, Removed, UnableToMeasureNG, NotCurrentlyNG, AttemptNotCurrent, CurrentScopeUnavailable,
}

internal data class OrdinaryNgObservation(
    val receipt: OrdinaryNgReceipt,
    val currentAttempt: Boolean,
    val latestForAttempt: Boolean,
    val state: OrdinaryNgState,
    val matchesAcceptedNgBasis: Boolean,
    val currentlyApplicable: Boolean,
)

internal data class OrdinaryNgItem(
    val taskItemId: String,
    val attempt: WorkAttempt?,
    val state: OrdinaryNgState,
    val latest: OrdinaryNgObservation?,
)

internal data class OrdinaryNgRead(
    val task: WorkTask,
    val draftVersion: Long,
    val page: Int,
    val pageSize: Int,
    val totalItems: Int,
    val items: List<OrdinaryNgItem>,
    val history: List<OrdinaryNgObservation>,
    val historyBeforeDraftVersion: Long?,
)

@Serializable
internal data class OrdinaryNgCaptureBasis(
    val task: WorkTask,
    val draftVersion: Long,
    val attempt: WorkAttempt,
    val currentNgReferences: List<OperationReference>,
    val latest: OrdinaryNgReceipt?,
) {
    fun matches(samples: OfflineSampleBasis): Boolean =
        task == samples.task && draftVersion == samples.draftVersion && draftVersion in 1 until Long.MAX_VALUE &&
            samples.editable && currentNgReferences.isSortedSampleBasis() &&
            samples.attempts.any { it.attempt == attempt && it.applicable && it.serverDisposition == ItemDisposition.Applicable } &&
            samples.currentNgSamples(attempt).map { it.valueOperationId }.sorted() == currentNgReferences.map { it.operationId } &&
            (latest == null || (latest.operation.attempt == attempt && latest.matches(task, draftVersion)))
}

internal fun OfflineSampleBasis.currentNgSamples(attempt: WorkAttempt): List<ObservedSample> =
    samples.filter { it.key.attempt == attempt && it.current && !it.removed && it.value.judgment == "NG" }

@Serializable
internal data class OrdinaryNgCommand(val task: WorkTask, val owner: SessionIdentity, val operation: OrdinaryNgOperation)

@Serializable
internal data class OrdinaryNgEnvelope(
    val workspaceId: String,
    val command: OrdinaryNgCommand,
    val source: WorkSource,
    val originLocalEditVersion: Long,
    val canonicalizationVersion: Int,
    val canonicalPayload: String,
    val contentHash: String,
    val capture: OfflineCaptureEvidence,
    val currentNgReferences: List<OperationReference>,
) {
    fun reference() = OperationReference(command.operation.operationId, contentHash)
    override fun toString() = "OrdinaryNgEnvelope"
}

internal data class LocalOrdinaryNgDraft(
    val accepted: List<OrdinaryNgReceipt>,
    val proposals: List<OrdinaryNgEnvelope>,
    val localEditVersion: Long,
    val captureProblem: WorkProblem?,
)
