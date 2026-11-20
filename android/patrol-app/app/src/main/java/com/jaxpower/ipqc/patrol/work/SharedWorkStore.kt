package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.auth.SessionRepository
import java.time.Instant
import java.util.Base64

// Internal substrate. A03-01 has no production producer of WorkAccess, source proof, or receipt evidence.
internal class SharedWorkStore(private val rows: ProtectedWorkRows, private val sessions: SessionRepository) {
    suspend fun storeSource(access: WorkAccess, source: WorkSource, proof: String) {
        workCheck(source.task == access.task, WorkProblem.ScopeMismatch)
        workCheck(access.owns(source), WorkProblem.SourceUnavailable)
        workCheck(validProof(proof), WorkProblem.SourceUnavailable)
        transact(access, create = true) {
            val old = source(source.contextId)
            workCheck(old == null || (old.source == source && old.proof == proof), WorkProblem.OperationIdentityConflict)
            if (old == null) save(StoredWorkSource(source, proof))
        }
    }

    suspend fun enqueue(access: WorkAccess, input: SampleOperation): WorkOperation {
        val op = DraftOperationCanonical.normalize(input)
        return requireNotNull(transact(access, create = true) {
            val context = requireSource(op.sourceWorkContextId, access.task)
            workCheck(access.owns(context.source), WorkProblem.SourceUnavailable)
            val catalog = catalog()
            val old = operation(op.operationId)
            if (old != null) {
                val original = old.envelope
                workCheck(original.source == context.source &&
                    original.canonicalPayload == DraftOperationCanonical.operation(context.source, op, original.originLocalEditVersion),
                    WorkProblem.OperationIdentityConflict)
                return@transact describe(old)
            }

            workCheck(op.attempt in access.currentAttempts && op.baseDraftVersion == access.serverDraftVersion,
                WorkProblem.ScopeMismatch)
            for (ref in op.dependencies) {
                val prior = requireOperation(ref.operationId)
                val previous = prior.envelope.operation
                val state = describe(prior).state
                workCheck(state == WorkState.Accepted || access.mayRead(prior.envelope.source), WorkProblem.SourceUnavailable)
                workCheck(prior.envelope.reference() == ref && previous.attempt == op.attempt &&
                    previous.clientSampleId == op.clientSampleId && previous.kind != SampleKind.RemoveSample &&
                    state !in setOf(WorkState.Conflict, WorkState.Quarantined),
                    WorkProblem.DependencyMismatch)
            }
            val next = Math.addExact(catalog.localEditVersion, 1L)
            val payload = DraftOperationCanonical.operation(context.source, op, next)
            val entry = OperationEntry(OperationEnvelope(workspaceId, op, context.source, next,
                CANONICAL_VERSION, payload, workHash(payload)))
            save(entry)
            save(catalog.copy(localEditVersion = next, operationIds = catalog.operationIds + op.operationId))
            describe(entry)
        })
    }

    suspend fun operation(access: WorkAccess, id: String): WorkOperation? = transact(access) {
        val entry = operation(id) ?: return@transact null
        visible(access, entry)
    }

    suspend fun operations(access: WorkAccess): List<WorkOperation> = transact(access) {
        catalog().operationIds.mapNotNull { id -> visible(access, requireOperation(id)) }
    }.orEmpty()

    // A trusted coordinator uses this narrow path to bootstrap source preflight.
    // Ordinary operation projections never carry a proof, and A03-01 performs no request.
    suspend fun withSourceForPreflight(access: WorkAccess, operationId: String, use: (StoredWorkSource) -> Unit) {
        transact(access) {
            val entry = requireOperation(operationId)
            workCheck(describe(entry).state == WorkState.Queued, WorkProblem.InvalidState)
            val stored = requireSource(entry.envelope.source.contextId, access.task)
            workCheck(stored.source == entry.envelope.source, WorkProblem.SourceUnavailable)
            use(stored)
        }
    }

    suspend fun prepareBatch(access: WorkAccess, id: String, operationIds: List<String>): BatchRecord {
        val ids = operationIds.sorted()
        require(isWorkId(id) && ids.size in 1..50 && ids.distinct().size == ids.size)
        return requireNotNull(transact(access) {
            batch(id)?.let { saved ->
                workCheck(saved.operations.map { it.operationId } == ids, WorkProblem.BatchIdentityConflict)
                ensureBatchRead(access, saved)
                return@transact saved
            }

            val entries = ids.map { requireOperation(it) }
            workCheck(entries.all { it.batchId == null && it.quarantine == null }, WorkProblem.BatchIdentityConflict)
            workCheck(entries.all { access.mayRead(it.envelope.source) }, WorkProblem.SourceUnavailable)
            val expected = entries.first().envelope.operation.baseDraftVersion
            workCheck(entries.all { it.envelope.operation.baseDraftVersion == expected }, WorkProblem.ScopeMismatch)
            val dependencies = entries.flatMap { it.envelope.operation.dependencies }.distinct()
            val members = entries.map { it.envelope.reference() }
            val accepted = dependencies.filter { it.operationId !in ids }.sortedBy { it.operationId }
            workCheck(accepted.size <= 100 && accepted.all { ref ->
                val entry = requireOperation(ref.operationId)
                entry.envelope.reference() == ref && describe(entry).state == WorkState.Accepted
            }, WorkProblem.DependencyMismatch)
            val prepared = BatchRecord(id, expected, members, accepted, CANONICAL_VERSION,
                DraftOperationCanonical.batch(access.task, id, expected, members, accepted))
            save(prepared)
            entries.forEach { save(it.copy(batchId = id)) }
            val catalog = catalog()
            save(catalog.copy(batchIds = catalog.batchIds + id))
            prepared
        }) { "Work task unavailable" }
    }

    suspend fun batch(access: WorkAccess, id: String): BatchRecord? = transact(access) {
        val result = batch(id) ?: return@transact null
        ensureBatchRead(access, result)
        result
    }

    suspend fun startAttempt(access: WorkAccess, id: String) {
        transition(access, id) { batch ->
            workCheck(batch.state == WorkState.Queued && catalog().batchIds.none { other ->
                other != id && requireBatch(other).state in setOf(WorkState.InFlight, WorkState.OutcomeUnknown)
            }, WorkProblem.InvalidState)
            workCheck(access.serverDraftVersion == batch.expectedDraftVersion, WorkProblem.ScopeMismatch)
            workCheck(batch.operations.all { ref ->
                val op = requireOperation(ref.operationId).envelope
                workCheck(op.operation.attempt in access.currentAttempts, WorkProblem.ScopeMismatch)
                access.approves(op.source)
            }, WorkProblem.SourceUnavailable)
            batch.copy(state = WorkState.InFlight)
        }
    }

    // Called explicitly when the coordinator reopens the task after an interrupted process.
    suspend fun recoverInterrupted(access: WorkAccess) {
        transact(access) {
            for (id in catalog().batchIds) {
                val batch = requireBatch(id)
                if (batch.state == WorkState.InFlight) save(batch.copy(state = WorkState.OutcomeUnknown))
            }
        }
    }

    // The timeout and receipt-404 paths both preserve an uncertain attempt.
    suspend fun recordOutcomeUnknown(access: WorkAccess, id: String) {
        transition(access, id) { batch ->
            workCheck(batch.state in setOf(WorkState.InFlight, WorkState.OutcomeUnknown), WorkProblem.InvalidState)
            batch.copy(state = WorkState.OutcomeUnknown)
        }
    }

    // Applies to a queued preflight or a live attempt with a definitive rejection.
    // A later preflight cannot settle whether an earlier unknown attempt committed.
    suspend fun recordRejection(access: WorkAccess, batchId: String, reason: WorkConflict) {
        transition(access, batchId) { batch ->
            val evidence = ConflictEvidence(reason, access.serverDraftVersion)
            val state = if (reason.isSourceRejection()) WorkState.Quarantined else WorkState.Conflict
            if (batch.state == state) {
                workCheck(batch.conflict == evidence, WorkProblem.InvalidState)
                return@transition batch
            }
            workCheck(batch.state in setOf(WorkState.Queued, WorkState.InFlight), WorkProblem.InvalidState)
            batch.copy(state = state, conflict = evidence)
        }
    }

    suspend fun quarantineUnbatched(access: WorkAccess, operationId: String, reason: WorkConflict) {
        workCheck(reason.isSourceRejection(), WorkProblem.InvalidState)
        transact(access) {
            val entry = requireOperation(operationId)
            workCheck(entry.batchId == null, WorkProblem.InvalidState)
            if (entry.quarantine == null) save(entry.copy(quarantine = ConflictEvidence(reason, access.serverDraftVersion)))
        }
    }

    suspend fun recordReceipt(access: WorkAccess, response: ServerBatchReceipt) {
        val receipt = response.copy(operations = response.operations.sortedBy { it.operationId })
        transition(access, receipt.batchId) { batch ->
            if (batch.state == WorkState.Accepted) {
                workCheck(batch.receipt == receipt, WorkProblem.ReceiptMismatch)
                return@transition batch
            }
            workCheck(batch.state in setOf(WorkState.InFlight, WorkState.OutcomeUnknown), WorkProblem.InvalidState)
            verifyBatchReceipt(access.task, batch, receipt)
            batch.copy(state = WorkState.Accepted, receipt = receipt)
        }
    }

    private suspend fun WorkTransaction.verifyBatchReceipt(task: WorkTask, batch: BatchRecord, receipt: ServerBatchReceipt) {
        workCheck(receipt.taskId == task.taskId && receipt.batchId == batch.batchId &&
            receipt.requestFingerprint == batch.fingerprint && receipt.previousDraftVersion == batch.expectedDraftVersion &&
            receipt.previousDraftVersion < Long.MAX_VALUE && receipt.draftVersion == receipt.previousDraftVersion + 1 &&
            isWorkId(receipt.uploadedByUserId) && isWorkId(receipt.uploadedPersonalSessionId) &&
            isTimestamp(receipt.acceptedAtUtc), WorkProblem.ReceiptMismatch)
        val expected = (batch.operations + batch.acceptedReferences).map { it.operationId }.sorted()
        workCheck(receipt.operations.map { it.operationId } == expected, WorkProblem.ReceiptMismatch)
        val outcomes = receipt.operations.associateBy { it.operationId }
        for (result in receipt.operations) {
            val entry = requireOperation(result.operationId)
            if (batch.operations.any { it.operationId == result.operationId }) {
                val predecessor = entry.envelope.operation.dependencies.singleOrNull()?.let { outcomes[it.operationId] }
                verifyOperationReceipt(task, entry.envelope, receipt, result, predecessor)
            } else {
                val prior = requireBatch(requireNotNull(entry.batchId)).receipt
                workCheck(prior?.operations?.singleOrNull { it.operationId == result.operationId } == result,
                    WorkProblem.ReceiptMismatch)
            }
        }
    }

    private fun verifyOperationReceipt(task: WorkTask, envelope: OperationEnvelope, batch: ServerBatchReceipt,
                                       result: ServerOperationReceipt, predecessor: ServerOperationReceipt?) {
        val op = envelope.operation
        val removed = op.kind == SampleKind.RemoveSample
        workCheck(result.taskId == task.taskId && result.environmentId == task.environmentId && result.lineId == task.lineId &&
            result.workflowVersion == task.workflowVersion && result.batchId == batch.batchId &&
            result.canonicalizationVersion == envelope.canonicalizationVersion && result.contentHash == envelope.contentHash &&
            result.kind == op.kind && result.taskItemId == op.attempt.taskItemId &&
            result.inspectionAttemptId == op.attempt.inspectionAttemptId && result.clientSampleId == op.clientSampleId &&
            result.sourceWorkContextId == envelope.source.contextId && result.contributorUserId == envelope.source.contributorUserId &&
            result.uploadedByUserId == batch.uploadedByUserId && result.uploadedPersonalSessionId == batch.uploadedPersonalSessionId &&
            result.draftVersion == batch.draftVersion && result.acceptedAtUtc == batch.acceptedAtUtc &&
            result.isRemoved == removed && isWorkId(result.sampleId) && isWorkId(result.valueOperationId) &&
            (if (removed) result.value == null else result.value != null && result.valueOperationId == op.operationId),
            WorkProblem.ReceiptMismatch)
        if (op.dependencies.isNotEmpty()) {
            workCheck(predecessor != null && predecessor.sampleId == result.sampleId &&
                (!removed || result.valueOperationId == predecessor.valueOperationId), WorkProblem.ReceiptMismatch)
        }
        val input = op.data
        val output = result.value
        if (input != null && output != null) {
            val expected = input.inspectionValue?.toBigDecimal()
            val actual = output.inspectionValue?.takeIf { it.length <= 64 }?.toBigDecimalOrNull()
            val valueMatches = if (expected == null)
                output.inspectionType == "Qualitative" && output.inspectionValue == null && output.judgment == input.judgment
            else output.inspectionType == "Quantitative" && actual?.compareTo(expected) == 0 &&
                output.judgment in setOf("OK", "NG")
            workCheck(valueMatches && output.barcode == input.barcode, WorkProblem.ReceiptMismatch)
        }
    }

    private suspend fun transition(access: WorkAccess, id: String, change: suspend WorkTransaction.(BatchRecord) -> BatchRecord) {
        val changed = transact(access) {
            save(change(requireBatch(id)))
            true
        }
        workCheck(changed == true, WorkProblem.InvalidState)
    }

    private suspend fun WorkTransaction.ensureBatchRead(access: WorkAccess, record: BatchRecord) {
        if (record.state == WorkState.Accepted) return
        workCheck(record.operations.all { access.mayRead(requireOperation(it.operationId).envelope.source) },
            WorkProblem.SourceUnavailable)
    }

    private suspend fun WorkTransaction.describe(entry: OperationEntry): WorkOperation {
        val batch = entry.batchId?.let { requireBatch(it) }
        val error = batch?.conflict ?: entry.quarantine
        val state = batch?.state ?: if (error == null) WorkState.Queued else WorkState.Quarantined
        return WorkOperation(entry.envelope, entry.batchId, state, error)
    }

    private suspend fun WorkTransaction.visible(access: WorkAccess, entry: OperationEntry): WorkOperation? {
        val result = describe(entry)
        return result.takeIf { it.state == WorkState.Accepted || access.mayRead(entry.envelope.source) }
    }

    private suspend fun WorkTransaction.requireSource(id: String, task: WorkTask): StoredWorkSource =
        source(id)?.takeIf { it.source.task == task } ?: throw WorkFailure(WorkProblem.SourceUnavailable)

    private suspend fun WorkTransaction.requireOperation(id: String): OperationEntry =
        operation(id) ?: throw WorkFailure(WorkProblem.InvalidState)

    private suspend fun WorkTransaction.requireBatch(id: String): BatchRecord =
        batch(id) ?: throw WorkFailure(WorkProblem.InvalidState)

    private suspend fun <T> transact(access: WorkAccess, create: Boolean = false,
                                     action: suspend WorkTransaction.() -> T): T? =
        sessions.withWorkSession(access.session, rows.origin) { rows.transaction(access.task, create, action) }

    private fun WorkAccess.owns(source: WorkSource) = session.identity.userId == source.contributorUserId &&
        session.identity.familyId == source.originPersonalSessionId

    private fun WorkAccess.approves(source: WorkSource) = validatedSources.any {
        it.contextId == source.contextId && it.contributorUserId == source.contributorUserId &&
            Instant.parse(it.captureUntilUtc) == Instant.parse(source.captureUntilUtc)
    }

    private fun WorkAccess.mayRead(source: WorkSource) = owns(source) || approves(source)

    private fun WorkConflict.isSourceRejection() =
        this == WorkConflict.SourceSecurityRevoked || this == WorkConflict.SourceProvenanceConflict

    private fun validProof(proof: String): Boolean = proof.length == 43 && try {
        val decoded = Base64.getUrlDecoder().decode(proof)
        try {
            decoded.size == 32 && Base64.getUrlEncoder().withoutPadding().encodeToString(decoded) == proof
        } finally {
            decoded.fill(0)
        }
    } catch (_: IllegalArgumentException) {
        false
    }

    private fun isTimestamp(value: String) = value.endsWith("Z") && runCatching { Instant.parse(value) }.isSuccess
}
