package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import java.util.Locale
import java.util.UUID
import kotlinx.serialization.Serializable

internal const val MAX_CURRENT_SAMPLES = 50

@Serializable
internal enum class FrozenSampleType {
    Qualitative, Quantitative;

    companion object {
        fun fromDefinition(value: String): FrozenSampleType? = when (value.trim().lowercase(Locale.ROOT)) {
            "qualitative", "visual", "定性" -> Qualitative
            "quantitative", "numeric", "计量", "定量" -> Quantitative
            else -> null
        }
    }
}

@Serializable
internal data class FrozenSampleAttempt(
    val attempt: WorkAttempt,
    val required: Int,
    val type: FrozenSampleType?,
    val applicable: Boolean,
    val serverDisposition: ItemDisposition? = null,
    val serverRemoval: ItemRemovalReceipt? = null,
)

@Serializable
internal data class ObservedSample(
    val sampleId: String,
    val key: LocalSampleKey,
    val value: ServerSampleValue,
    val valueContributorId: String,
    val valueOperationId: String,
    val lastOperationId: String,
    val removed: Boolean,
    val current: Boolean,
)

@Serializable
internal data class OfflineSampleBasis(
    val task: WorkTask,
    val draftVersion: Long,
    val editable: Boolean,
    val attempts: List<FrozenSampleAttempt>,
    val samples: List<ObservedSample>,
    val historicalItemRemovals: List<ItemRemovalReceipt> = emptyList(),
)

@Serializable
internal data class OfflineSampleProfile(
    val owner: SessionIdentity,
    val contextId: String,
    val captureUntilUtc: String,
    val basis: OfflineSampleBasis,
)

internal enum class QualitativeJudgment { OK, NG }

internal sealed interface SampleInput {
    fun payload(): SamplePayload

    data class Qualitative(val judgment: QualitativeJudgment) : SampleInput {
        override fun payload() = SamplePayload(judgment.name, null, null)
    }

    class Quantitative(val decimalText: String, val barcode: String? = null) : SampleInput {
        override fun payload() = SamplePayload(null, decimalText, barcode)
    }
}

@Serializable
internal data class LocalSampleKey(val attempt: WorkAttempt, val clientSampleId: String) {
    init { require(isWorkId(clientSampleId)) }
}

// Keep the returned command through the save acknowledgement. Retrying the
// same command is an exact ledger lookup, including after process recreation.
internal sealed interface OfflineSampleEdit {
    val operationId: String
    val key: LocalSampleKey
    val kind: SampleKind
    val input: SampleInput?
    val predecessor: OperationReference?

    data class Capture(
        override val key: LocalSampleKey,
        override val input: SampleInput,
        override val operationId: String = UUID.randomUUID().toString(),
    ) : OfflineSampleEdit {
        override val kind = SampleKind.CaptureSample
        override val predecessor = null
    }

    data class Correct(
        override val key: LocalSampleKey,
        override val input: SampleInput,
        override val predecessor: OperationReference,
        override val operationId: String = UUID.randomUUID().toString(),
    ) : OfflineSampleEdit {
        override val kind = SampleKind.CorrectSample
    }

    data class Remove(
        override val key: LocalSampleKey,
        override val predecessor: OperationReference,
        override val operationId: String = UUID.randomUUID().toString(),
    ) : OfflineSampleEdit {
        override val kind = SampleKind.RemoveSample
        override val input = null
    }

    fun operation(contextId: String, version: Long): SampleOperation = try {
        DraftOperationCanonical.normalize(SampleOperation(
            operationId, key.attempt, key.clientSampleId, contextId, version, kind, input?.payload(), listOfNotNull(predecessor),
        ))
    } catch (_: IllegalArgumentException) {
        throw WorkFailure(WorkProblem.SampleInputMismatch)
    }
}

internal data class LocalSampleValue(
    val key: LocalSampleKey,
    val value: SamplePayload?,
    val removed: Boolean,
    val contributorUserId: String,
    val source: WorkSource?,
    val lastOperationId: String,
    val editablePredecessor: OperationReference?,
    val status: WorkState,
)

internal data class LocalSampleHistory(
    val operation: WorkOperation,
    val currentAttempt: Boolean,
    val matchesObservedDraftVersion: Boolean,
    val acceptedReceipt: ServerOperationReceipt?,
    val individualEvidence: ServerOperationReceipt?,
    val offlineCapturedAtUtc: String?,
    val mayReferencePending: Boolean,
)

internal data class LocalSampleProgress(
    val attempt: WorkAttempt,
    val required: Int,
    val applicableSamples: Int,
    val type: FrozenSampleType?,
) {
    val meetsCount: Boolean get() = applicableSamples >= required
}

internal data class LocalSampleDraft(
    val observedServerDraftVersion: Long,
    val localEditVersion: Long,
    val confirmed: List<ObservedSample>,
    val effective: List<LocalSampleValue>,
    val history: List<LocalSampleHistory>,
    val progress: List<LocalSampleProgress>,
    val captureProblem: WorkProblem? = null,
)
