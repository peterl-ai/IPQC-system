package com.jaxpower.ipqc.patrol.work

// Single version of the sample graph for projection, save and batch subset validation.
// A server observation can anchor a predecessor only with a matching verified receipt.
internal class LocalSampleBoard(
    private val basis: OfflineSampleBasis,
    verified: Map<String, LocalSampleHistory>,
) {
    private val samples = mutableMapOf<LocalSampleKey, LocalSampleValue>()
    private val sealedLinks = mutableMapOf<LocalSampleKey, Pair<String, OperationReference>>()
    private val applicable = basis.attempts.filter { it.applicable }.associateBy { it.attempt }

    init {
        for (sample in basis.samples) {
            if (sample.key.attempt !in applicable || (!sample.current && !sample.removed)) continue
            val history = verified[sample.lastOperationId]
            val receipt = history?.acceptedReceipt
            val envelope = history?.operation?.envelope
            val matches = receipt != null && envelope != null && receipt.draftVersion <= basis.draftVersion &&
                receipt.sampleId == sample.sampleId && receipt.clientSampleId == sample.key.clientSampleId &&
                envelope.operation.attempt == sample.key.attempt &&
                receipt.valueOperationId == sample.valueOperationId && receipt.isRemoved == sample.removed &&
                (receipt.isRemoved || (receipt.value == sample.value && receipt.contributorUserId == sample.valueContributorId))
            samples[sample.key] = LocalSampleValue(sample.key, if (sample.removed) null else with(sample.value) {
                SamplePayload(judgment, inspectionValue, barcode)
            }, sample.removed, sample.valueContributorId, envelope?.source?.takeIf {
                matches && sample.lastOperationId == sample.valueOperationId
            }, sample.lastOperationId, envelope?.reference()?.takeIf { matches && !sample.removed }, WorkState.Accepted)
        }
    }

    fun validate(key: LocalSampleKey, op: SampleOperation, batchId: String? = null, checkCapacity: Boolean = true) {
        val definition = applicable[key.attempt]
        workCheck(basis.editable && definition?.type != null, WorkProblem.ScopeMismatch)
        if (op.kind != SampleKind.RemoveSample) {
            val quantitative = op.data?.inspectionValue != null
            workCheck(quantitative == (definition?.type == FrozenSampleType.Quantitative), WorkProblem.SampleInputMismatch)
        }
        val prior = samples[key]
        if (op.kind == SampleKind.CaptureSample) {
            workCheck(prior == null && basis.samples.none { it.key == key }, WorkProblem.OperationIdentityConflict)
            if (checkCapacity) workCheck(count(key.attempt) < MAX_CURRENT_SAMPLES, WorkProblem.SampleLimit)
        } else {
            val reference = op.dependencies.single()
            val sealedPredecessor = batchId != null && sealedLinks[key] == (batchId to reference)
            workCheck(prior != null && !prior.removed && (prior.editablePredecessor == reference || sealedPredecessor),
                WorkProblem.DependencyMismatch)
        }
    }

    fun overlay(op: WorkOperation, suppliesPredecessor: Boolean) {
        val envelope = op.envelope
        val sample = envelope.operation
        val key = LocalSampleKey(sample.attempt, sample.clientSampleId)
        val batchId = op.batchId
        if (batchId == null) sealedLinks.remove(key)
        else sealedLinks[key] = batchId to envelope.reference()
        samples[key] = LocalSampleValue(key, sample.data, sample.kind == SampleKind.RemoveSample,
            envelope.source.contributorUserId, envelope.source, sample.operationId,
            envelope.reference().takeIf { suppliesPredecessor && sample.kind != SampleKind.RemoveSample }, op.state)
    }

    fun applyPending(history: LocalSampleHistory) {
        val op = history.operation
        if (!history.currentAttempt || !history.matchesObservedDraftVersion ||
            op.state !in pendingStates || history.acceptedReceipt != null) return
        val sample = op.envelope.operation
        val key = LocalSampleKey(sample.attempt, sample.clientSampleId)
        try {
            validate(key, sample, op.batchId)
        } catch (_: WorkFailure) {
            // Leave an invalidated, competing or unverifiable edit in historical state only.
            return
        }
        overlay(op, suppliesPredecessor = op.state == WorkState.Queued && op.batchId == null &&
            history.mayReferencePending)
    }

    fun values(): List<LocalSampleValue> = samples.values.toList()

    fun progress(): List<LocalSampleProgress> = applicable.values.map {
        LocalSampleProgress(it.attempt, it.required, count(it.attempt))
    }

    private fun count(attempt: WorkAttempt) = samples.values.count { it.key.attempt == attempt && !it.removed }

    private companion object {
        val pendingStates = setOf(WorkState.Queued, WorkState.InFlight, WorkState.OutcomeUnknown)
    }
}
