package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

@Serializable
internal enum class BasicInformationField { Customer, Production, ProductMo }

@Serializable
internal enum class BasicInformationKind { CaptureBasicInformationField, CorrectBasicInformationField }

internal enum class BasicInformationPresence { Unrecorded, ExplicitNull, Empty, Whitespace, Provided }

@Serializable
internal enum class BasicInformationAvailability(val editable: Boolean) {
    FoundationOnly(true), DraftFoundation(true), UnsupportedSnapshot(false), UnsupportedState(false),
}

@Serializable
internal data class BasicInformationOperation(
    val operationId: String,
    val kind: BasicInformationKind,
    val field: BasicInformationField,
    val value: String?,
    val sourceWorkContextId: String,
    val contributorUserId: String,
    val baseDraftVersion: Long,
    val predecessor: OperationReference?,
) {
    fun isValid() = isWorkId(operationId) && isWorkId(sourceWorkContextId) && isWorkId(contributorUserId) &&
        baseDraftVersion in 0 until Long.MAX_VALUE && validBasicInformationValue(value) &&
        (predecessor == null) == (kind == BasicInformationKind.CaptureBasicInformationField) &&
        predecessor?.operationId != operationId

    override fun toString() = "BasicInformationOperation"
}

@Serializable
internal data class BasicInformationReceipt(
    val task: WorkTask,
    val operation: BasicInformationOperation,
    val batchId: String,
    val contentHash: String,
    val actorUserId: String,
    val actorPersonalSessionId: String,
    val originLocalEditVersion: Long,
    val acceptedAtUtc: String,
    val draftVersion: Long,
) {
    fun reference() = OperationReference(operation.operationId, contentHash)

    fun matches(scope: WorkTask, version: Long) = task == scope && isWorkId(batchId) && isWorkId(actorUserId) &&
        isWorkId(actorPersonalSessionId) && originLocalEditVersion >= 0 && operation.isValid() &&
        draftVersion == operation.baseDraftVersion + 1 && draftVersion <= version &&
        acceptedAtUtc.endsWith("Z") && runCatching { java.time.Instant.parse(acceptedAtUtc) }.isSuccess &&
        contentHash == workHash(BasicInformationCanonical.operation(task, operation, originLocalEditVersion))

    override fun toString() = "BasicInformationReceipt"
}

@Serializable
internal data class BasicInformationFact(val field: BasicInformationField, val accepted: BasicInformationReceipt?) {
    val value get() = accepted?.operation?.value
    val presence: BasicInformationPresence
        get() {
            val text = value
            return when {
                accepted == null -> BasicInformationPresence.Unrecorded
                text == null -> BasicInformationPresence.ExplicitNull
                text.isEmpty() -> BasicInformationPresence.Empty
                // .NET's other whitespace characters are controls, which the contract rejects.
                text.all { Character.isSpaceChar(it) } -> BasicInformationPresence.Whitespace
                else -> BasicInformationPresence.Provided
            }
        }
}

@Serializable
internal data class BasicInformationBasis(
    val task: WorkTask,
    val draftVersion: Long,
    val availability: BasicInformationAvailability,
    val fields: List<BasicInformationFact>,
) {
    fun field(field: BasicInformationField) = fields.single { it.field == field }
}

internal data class BasicInformationRead(
    val basis: BasicInformationBasis,
    val history: List<BasicInformationReceipt>,
    val historyBeforeDraftVersion: Long?,
)

@Serializable
internal data class BasicInformationCommand(
    val task: WorkTask,
    val owner: SessionIdentity,
    val operation: BasicInformationOperation,
)

@Serializable
internal data class BasicInformationEnvelope(
    val workspaceId: String,
    val command: BasicInformationCommand,
    val source: WorkSource,
    val originLocalEditVersion: Long,
    val canonicalizationVersion: Int,
    val canonicalPayload: String,
    val contentHash: String,
    val capture: OfflineCaptureEvidence,
) {
    fun reference() = OperationReference(command.operation.operationId, contentHash)
    override fun toString() = "BasicInformationEnvelope"
}

internal data class LocalBasicInformationProposal(
    val original: BasicInformationEnvelope,
    val atCurrentDraftVersion: Boolean,
    val sourceRevoked: Boolean,
)

internal data class LocalBasicInformationDraft(
    val accepted: BasicInformationBasis?,
    val proposals: List<LocalBasicInformationProposal>,
    val localEditVersion: Long,
    val captureProblem: WorkProblem?,
)

internal fun validBasicInformationValue(value: String?): Boolean {
    if (value == null) return true
    if (value.length > 400) return false
    var index = 0
    var count = 0
    while (index < value.length) {
        val ch = value[index++]
        if (ch.isISOControl() || ch.isLowSurrogate() || ++count > 200) return false
        if (ch.isHighSurrogate() && value.getOrNull(index++)?.isLowSurrogate() != true) return false
    }
    return true
}

// V1 property order and domains are pinned by the Backend golden vectors.
internal object BasicInformationCanonical {
    fun operation(task: WorkTask, op: BasicInformationOperation, localVersion: Long): String =
        encodeDraftCanonical(buildJsonObject {
            put("domain", "basic-information-field")
            put("v", CANONICAL_VERSION)
            put("environment", task.environmentId)
            put("task", task.taskId)
            put("line", task.lineId)
            put("workflow", task.workflowVersion)
            put("operation", op.operationId)
            put("kind", op.kind.name)
            put("field", op.field.name)
            put("value", op.value)
            put("source", op.sourceWorkContextId)
            put("contributorAssertion", op.contributorUserId)
            put("base", op.baseDraftVersion)
            put("local", localVersion)
            put("predecessor", op.predecessor?.let { ref ->
                buildJsonObject {
                    put("operation", ref.operationId)
                    put("hash", ref.contentHash)
                }
            } ?: kotlinx.serialization.json.JsonNull)
        })

    fun fingerprint(task: WorkTask, batchId: String, expected: Long, operation: OperationReference): String {
        require(isWorkId(batchId) && expected >= 0)
        return workHash(encodeDraftCanonical(buildJsonObject {
            put("domain", "basic-information-batch")
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
