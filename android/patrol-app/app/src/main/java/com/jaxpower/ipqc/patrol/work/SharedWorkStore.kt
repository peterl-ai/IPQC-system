package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import java.time.Instant
import java.util.Base64
import java.util.UUID

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

    internal class IssuanceSnapshot(val state: WorkIssuanceState, val source: StoredWorkSource?, val revoked: Boolean)

    suspend fun issuanceSnapshot(access: WorkAccess): IssuanceSnapshot {
        val found = transact(access) {
            val state = issuance(access.session.identity.userId)
            workCheck(state.pendingId == null, WorkProblem.IssuanceUncertain)
            val stored = state.contextId?.let { requireSource(it, access.task) }
            if (stored != null) workCheck(stored.source.contributorUserId == access.session.identity.userId &&
                validProof(stored.proof), WorkProblem.SourceUnavailable)
            IssuanceSnapshot(state, stored, stored != null && sourceRevoked(stored.source.contextId))
        }
        return found ?: IssuanceSnapshot(WorkIssuanceState(), null, false)
    }

    suspend fun reserveIssuance(access: WorkAccess, expected: WorkIssuanceState): String =
        requireNotNull(transact(access, create = true) {
            val user = access.session.identity.userId
            val state = issuance(user)
            workCheck(state.pendingId == null, WorkProblem.IssuanceUncertain)
            workCheck(state == expected, WorkProblem.StaleGrant)
            val reservation = UUID.randomUUID().toString()
            saveIssuance(user, state.copy(pendingId = reservation))
            reservation
        })

    suspend fun finishIssuance(access: WorkAccess, reservation: String, source: StoredWorkSource) {
        workCheck(source.source.task == access.task && access.owns(source.source) &&
            validProof(source.proof), WorkProblem.SourceUnavailable)
        workCheck(transact(access) {
            val user = access.session.identity.userId
            val state = issuance(user)
            workCheck(state.pendingId == reservation, WorkProblem.IssuanceUncertain)
            workCheck(this.source(source.source.contextId) == null && !sourceRevoked(source.source.contextId),
                WorkProblem.OperationIdentityConflict)
            save(source)
            saveIssuance(user, WorkIssuanceState(contextId = source.source.contextId))
            true
        } == true, WorkProblem.StorageUnavailable)
    }

    // A definite no-send or negative HTTP result can retire only this exact reservation.
    // Cleanup can run after HTTP auth rejection has already revoked the old session.
    suspend fun releaseUnsentIssuance(task: WorkTask, userId: String, reservation: String) {
        rows.transaction(task) {
            val state = issuance(userId)
            if (state.pendingId == reservation) saveIssuance(userId, state.copy(pendingId = null))
        }
    }

    suspend fun recordSourceRevoked(access: WorkAccess, contextId: String) {
        workCheck(transact(access) {
            requireSource(contextId, access.task)
            rememberSourceRevoked(contextId)
            true
        } == true, WorkProblem.StorageUnavailable)
    }

    suspend fun enqueue(access: WorkAccess, input: SampleOperation): WorkOperation {
        val op = DraftOperationCanonical.normalize(input)
        return sessions.withWorkCaptureClock(access.session, rows.origin) { captureTime ->
            access.requireCurrent()
            requireNotNull(rows.transaction(access.task, create = true) {
                access.requireCurrent()
                val context = requireSource(op.sourceWorkContextId, access.task)
                workCheck(access.owns(context.source), WorkProblem.SourceUnavailable)
                val old = operation(op.operationId)
                if (old != null) {
                    val original = old.envelope
                    workCheck(original.source == context.source &&
                        original.canonicalPayload == DraftOperationCanonical.operation(context.source, op, original.originLocalEditVersion),
                        WorkProblem.OperationIdentityConflict)
                    return@transaction describe(old).also { access.requireCurrent() }
                }

                workCheck(op.attempt in access.currentAttempts && op.baseDraftVersion == access.serverDraftVersion,
                    WorkProblem.ScopeMismatch)
                workCheck(context.source.contextId !in access.securityRevokedSources &&
                    !sourceRevoked(context.source.contextId), WorkProblem.SourceUnavailable)
                if (access.requiresCaptureGrant)
                    workCheck(access.captureGrant?.contextId == context.source.contextId, WorkProblem.SourceUnavailable)
                requireCaptureTime(context.source, access.captureGrant, captureTime)
                val catalog = catalog()
                for (ref in op.dependencies) {
                    val prior = requireOperation(ref.operationId)
                    val previous = prior.envelope.operation
                    val state = describe(prior).state
                    workCheck(state == WorkState.Accepted || mayRead(access, prior.envelope.source), WorkProblem.SourceUnavailable)
                    workCheck(prior.envelope.reference() == ref && previous.attempt == op.attempt &&
                        previous.clientSampleId == op.clientSampleId && previous.kind != SampleKind.RemoveSample &&
                        state !in setOf(WorkState.Conflict, WorkState.Quarantined),
                        WorkProblem.DependencyMismatch)
                }
                val next = Math.addExact(catalog.localEditVersion, 1L)
                val payload = DraftOperationCanonical.operation(context.source, op, next)
                val entry = OperationEntry(OperationEnvelope(workspaceId, op, context.source, next,
                    CANONICAL_VERSION, payload, workHash(payload)))
                reserveOperationId(op.operationId)
                save(entry)
                save(catalog.copy(localEditVersion = next, operationIds = catalog.operationIds + op.operationId))
                val result = describe(entry)
                requireCaptureTime(context.source, access.captureGrant, captureTime)
                access.requireCurrent()
                result
            })
        }
    }

    private suspend fun requireCaptureTime(source: WorkSource, grant: CaptureGrant?,
                                           captureTime: suspend () -> Instant?) {
        val now = try {
            captureTime()
        } catch (_: PatrolFailure) {
            null
        } ?: throw WorkFailure(WorkProblem.CaptureTimeUnavailable)
        workCheck(!now.isBefore(Instant.parse(source.issuedAtUtc)), WorkProblem.CaptureTimeUnavailable)
        val original = Instant.parse(source.captureUntilUtc)
        val deadline = grant?.let { minOf(original, it.until) } ?: original
        workCheck(now.isBefore(deadline), WorkProblem.CaptureWindowClosed)
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
    suspend fun <T : Any> withSourceForPreflight(access: WorkAccess, operationId: String, use: (StoredWorkSource) -> T): T =
        transact(access) {
            val entry = requireOperation(operationId)
            workCheck(describe(entry).state == WorkState.Queued, WorkProblem.InvalidState)
            val stored = requireSource(entry.envelope.source.contextId, access.task)
            workCheck(stored.source == entry.envelope.source && validProof(stored.proof) &&
                !sourceRevoked(stored.source.contextId), WorkProblem.SourceUnavailable)
            use(stored)
        } ?: throw WorkFailure(WorkProblem.SourceUnavailable)

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
            workCheck(entries.all { mayRead(access, it.envelope.source) }, WorkProblem.SourceUnavailable)
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
                !sourceRevoked(op.source.contextId) && access.approves(op.source)
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

    suspend fun locateReceipt(access: WorkAccess, lookup: ReceiptLookup): ReceiptTarget? =
        transact(access) { locateReceipt(access.task, lookup) }

    suspend fun reconcileReceipt(access: WorkAccess, target: ReceiptTarget, reply: ReceiptReply): ReceiptRecovery {
        return transact(access) {
            workCheck(locateReceipt(access.task, target.lookup) == target, WorkProblem.StaleGrant)
            val batch = requireBatch(target.batchId)
            try {
                when (reply) {
                    is ReceiptReply.Batch -> {
                        workCheck(target.lookup.kind == ReceiptKind.Batch, WorkProblem.ReceiptMismatch)
                        val accepted = verifyAcceptance(access.task, batch, reply.receipt)
                        if (accepted != batch) save(accepted)
                        ReceiptRecovery.BatchConfirmed(requireNotNull(accepted.receipt))
                    }
                    is ReceiptReply.Operation -> {
                        workCheck(target.lookup.kind == ReceiptKind.Operation, WorkProblem.ReceiptMismatch)
                        reconcileOperation(access.task, target.lookup.id, reply.receipt)
                    }
                    ReceiptReply.Missing -> ReceiptRecovery.Unresolved(ReceiptUncertainty.NotObserved)
                    ReceiptReply.Conflict -> recordReceiptConflict(target.lookup, ReceiptConflictReason.QueryConflict)
                    is ReceiptReply.Unavailable -> ReceiptRecovery.TransientFailure(reply.issue)
                }
            } catch (failure: WorkFailure) {
                if (failure.problem != WorkProblem.ReceiptMismatch) throw failure
                recordReceiptConflict(target.lookup, ReceiptConflictReason.ResponseMismatch)
            }
        } ?: throw WorkFailure(WorkProblem.StorageUnavailable)
    }

    private suspend fun WorkTransaction.recordReceiptConflict(lookup: ReceiptLookup, reason: ReceiptConflictReason):
        ReceiptRecovery.Conflict {
        rememberReceiptConflict(lookup, reason)
        return ReceiptRecovery.Conflict(reason)
    }

    private suspend fun WorkTransaction.locateReceipt(task: WorkTask, lookup: ReceiptLookup): ReceiptTarget? {
        if (!isWorkId(lookup.id)) return null
        val id = when (lookup.kind) {
            ReceiptKind.Batch -> lookup.id
            ReceiptKind.Operation -> operation(lookup.id)?.batchId ?: return null
        }
        val batch = batch(id) ?: return null
        if (batch.state !in receiptStates) return null
        workCheck(isWorkId(id) && batch.canonicalizationVersion == CANONICAL_VERSION &&
            batch.expectedDraftVersion in 0 until Long.MAX_VALUE && batch.operations.size in 1..50 &&
            batch.acceptedReferences.size <= 100 && (batch.receipt != null) == (batch.state == WorkState.Accepted),
            WorkProblem.StorageUnavailable)
        val members = batch.operations.map { it.operationId }.toSet()
        workCheck(members.size == batch.operations.size &&
            (lookup.kind != ReceiptKind.Operation || lookup.id in members), WorkProblem.StorageUnavailable)
        val dependencies = batch.operations.flatMap { ref ->
            val entry = checkedEntry(task, ref)
            workCheck(entry.batchId == id && entry.envelope.operation.baseDraftVersion == batch.expectedDraftVersion,
                WorkProblem.StorageUnavailable)
            entry.envelope.operation.dependencies
        }
        val frontier = dependencies.filter { it.operationId !in members }.distinct().sortedBy { it.operationId }
        workCheck(frontier == batch.acceptedReferences &&
            batch.fingerprint == DraftOperationCanonical.batch(task, id, batch.expectedDraftVersion,
                batch.operations, batch.acceptedReferences), WorkProblem.StorageUnavailable)
        for (dependency in dependencies.distinct()) {
            val entry = checkedEntry(task, dependency)
            if (dependency.operationId !in members) {
                workCheck(acceptedOperation(entry) != null, WorkProblem.StorageUnavailable)
            }
        }
        return ReceiptTarget(lookup, task, workspaceId, id, batch.fingerprint)
    }

    private suspend fun WorkTransaction.checkedEntry(task: WorkTask, ref: OperationReference): OperationEntry {
        val entry = requireOperation(ref.operationId)
        val envelope = entry.envelope
        val op = envelope.operation
        workCheck(envelope.source.task == task && envelope.source.contextId == op.sourceWorkContextId &&
            envelope.canonicalizationVersion == CANONICAL_VERSION && envelope.originLocalEditVersion > 0 &&
            envelope.canonicalPayload == DraftOperationCanonical.operation(envelope.source, op, envelope.originLocalEditVersion) &&
            workHash(envelope.canonicalPayload) == ref.contentHash && envelope.contentHash == ref.contentHash,
            WorkProblem.StorageUnavailable)
        return entry
    }

    private suspend fun WorkTransaction.reconcileOperation(task: WorkTask, id: String,
                                                            receipt: ServerOperationReceipt): ReceiptRecovery {
        val entry = requireOperation(id)
        verifyOperationReceipt(task, entry.envelope, receipt)
        val saved = individualReceipt(id)
        val accepted = acceptedOperation(entry)
        workCheck((accepted == null || accepted == receipt) && (saved == null || saved == receipt),
            WorkProblem.ReceiptMismatch)
        val dependency = entry.envelope.operation.dependencies.singleOrNull()
        if (dependency != null) {
            val prior = checkedEntry(task, dependency)
            val predecessor = acceptedOperation(prior) ?: individualReceipt(dependency.operationId)
                ?: return ReceiptRecovery.Unresolved(ReceiptUncertainty.MissingPredecessor)
            verifyPredecessor(entry.envelope, receipt, predecessor)
        }
        if (accepted == null && saved == null) saveIndividualReceipt(receipt)
        return ReceiptRecovery.OperationConfirmed(accepted ?: saved ?: receipt)
    }

    private suspend fun WorkTransaction.acceptedOperation(entry: OperationEntry): ServerOperationReceipt? {
        val batch = entry.batchId?.let { requireBatch(it) } ?: return null
        if (batch.state != WorkState.Accepted) return null
        return batch.receipt?.operations?.singleOrNull { it.operationId == entry.envelope.operation.operationId }
            ?: throw WorkFailure(WorkProblem.StorageUnavailable)
    }

    suspend fun recordReceipt(access: WorkAccess, response: ServerBatchReceipt) {
        transition(access, response.batchId) { batch -> verifyAcceptance(access.task, batch, response) }
    }

    private suspend fun WorkTransaction.verifyAcceptance(task: WorkTask, batch: BatchRecord,
                                                          response: ServerBatchReceipt): BatchRecord {
        workCheck(batch.state in receiptStates, WorkProblem.InvalidState)
        val receipt = response.copy(operations = response.operations.sortedBy { it.operationId })
        if (batch.state == WorkState.Accepted) {
            workCheck(batch.receipt == receipt, WorkProblem.ReceiptMismatch)
            return batch
        }
        verifyBatchReceipt(task, batch, receipt)
        return batch.copy(state = WorkState.Accepted, receipt = receipt)
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
        val samples = mutableMapOf<String, Pair<WorkAttempt, String>>()
        val captures = mutableSetOf<Pair<WorkAttempt, String>>()
        val consumedPredecessors = mutableSetOf<String>()
        for (result in receipt.operations) {
            val entry = requireOperation(result.operationId)
            val op = entry.envelope.operation
            val sample = op.attempt to op.clientSampleId
            val earlier = samples.put(result.sampleId, sample)
            workCheck((earlier == null || earlier == sample) &&
                (op.kind != SampleKind.CaptureSample || captures.add(sample)), WorkProblem.ReceiptMismatch)
            if (batch.operations.any { it.operationId == result.operationId }) {
                verifyOperationReceipt(task, entry.envelope, result)
                workCheck(result.batchId == receipt.batchId && result.draftVersion == receipt.draftVersion &&
                    result.uploadedByUserId == receipt.uploadedByUserId &&
                    result.uploadedPersonalSessionId == receipt.uploadedPersonalSessionId &&
                    result.acceptedAtUtc == receipt.acceptedAtUtc, WorkProblem.ReceiptMismatch)
                op.dependencies.singleOrNull()?.let { dependency ->
                    workCheck(consumedPredecessors.add(dependency.operationId), WorkProblem.ReceiptMismatch)
                    val predecessor = outcomes[dependency.operationId] ?: throw WorkFailure(WorkProblem.ReceiptMismatch)
                    verifyPredecessor(entry.envelope, result, predecessor)
                }
                val saved = individualReceipt(result.operationId)
                workCheck(saved == null || saved == result, WorkProblem.ReceiptMismatch)
            } else {
                workCheck(acceptedOperation(entry) == result, WorkProblem.ReceiptMismatch)
            }
        }
    }

    private fun verifyPredecessor(envelope: OperationEnvelope, result: ServerOperationReceipt,
                                  predecessor: ServerOperationReceipt) {
        val op = envelope.operation
        val dependency = op.dependencies.single()
        workCheck(predecessor.operationId == dependency.operationId && predecessor.contentHash == dependency.contentHash &&
            !predecessor.isRemoved && predecessor.valueOperationId == predecessor.operationId &&
            predecessor.taskItemId == op.attempt.taskItemId && predecessor.inspectionAttemptId == op.attempt.inspectionAttemptId &&
            predecessor.clientSampleId == op.clientSampleId && predecessor.sampleId == result.sampleId &&
            (predecessor.draftVersion < result.draftVersion ||
                (predecessor.draftVersion == result.draftVersion && predecessor.batchId == result.batchId &&
                    predecessor.uploadedByUserId == result.uploadedByUserId &&
                    predecessor.uploadedPersonalSessionId == result.uploadedPersonalSessionId &&
                    predecessor.acceptedAtUtc == result.acceptedAtUtc)) &&
            (!result.isRemoved || result.valueOperationId == predecessor.valueOperationId), WorkProblem.ReceiptMismatch)
    }

    private fun verifyOperationReceipt(task: WorkTask, envelope: OperationEnvelope, result: ServerOperationReceipt) {
        val op = envelope.operation
        val removed = op.kind == SampleKind.RemoveSample
        workCheck(result.taskId == task.taskId && result.environmentId == task.environmentId && result.lineId == task.lineId &&
            result.workflowVersion == task.workflowVersion && result.operationId == op.operationId && isWorkId(result.batchId) &&
            result.canonicalizationVersion == envelope.canonicalizationVersion && result.contentHash == envelope.contentHash &&
            result.kind == op.kind && result.taskItemId == op.attempt.taskItemId &&
            result.inspectionAttemptId == op.attempt.inspectionAttemptId && result.clientSampleId == op.clientSampleId &&
            result.sourceWorkContextId == envelope.source.contextId && result.contributorUserId == envelope.source.contributorUserId &&
            isWorkId(result.uploadedByUserId) && isWorkId(result.uploadedPersonalSessionId) &&
            op.baseDraftVersion < Long.MAX_VALUE && result.draftVersion == op.baseDraftVersion + 1 &&
            isTimestamp(result.acceptedAtUtc) &&
            result.isRemoved == removed && isWorkId(result.sampleId) && isWorkId(result.valueOperationId) &&
            (if (removed) result.value == null else result.value != null && result.valueOperationId == op.operationId),
            WorkProblem.ReceiptMismatch)

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
        workCheck(record.operations.all { mayRead(access, requireOperation(it.operationId).envelope.source) },
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
        return result.takeIf { it.state == WorkState.Accepted || mayRead(access, entry.envelope.source) }
    }

    private suspend fun WorkTransaction.requireSource(id: String, task: WorkTask): StoredWorkSource =
        source(id)?.takeIf { it.source.task == task } ?: throw WorkFailure(WorkProblem.SourceUnavailable)

    private suspend fun WorkTransaction.requireOperation(id: String): OperationEntry =
        operation(id) ?: throw WorkFailure(WorkProblem.InvalidState)

    private suspend fun WorkTransaction.requireBatch(id: String): BatchRecord =
        batch(id) ?: throw WorkFailure(WorkProblem.InvalidState)

    private suspend fun <T> transact(access: WorkAccess, create: Boolean = false,
                                     action: suspend WorkTransaction.() -> T): T? =
        sessions.withWorkSession(access.session, rows.origin) { requireCurrentSession ->
            access.requireCurrent()
            rows.transaction(access.task, create) {
                requireCurrentSession()
                access.requireCurrent()
                action().also {
                    requireCurrentSession()
                    access.requireCurrent()
                }
            }
        }

    private fun WorkAccess.owns(source: WorkSource) = session.identity.userId == source.contributorUserId &&
        session.identity.familyId == source.originPersonalSessionId

    private fun WorkAccess.approves(source: WorkSource) =
        source.contextId !in securityRevokedSources && validatedSources.any {
            it.contextId == source.contextId && it.contributorUserId == source.contributorUserId &&
                Instant.parse(it.captureUntilUtc) == Instant.parse(source.captureUntilUtc)
        }

    private suspend fun WorkTransaction.mayRead(access: WorkAccess, source: WorkSource) =
        access.owns(source) || (!sourceRevoked(source.contextId) && access.approves(source))

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

    private companion object {
        val receiptStates = setOf(WorkState.InFlight, WorkState.OutcomeUnknown, WorkState.Accepted)
    }
}
