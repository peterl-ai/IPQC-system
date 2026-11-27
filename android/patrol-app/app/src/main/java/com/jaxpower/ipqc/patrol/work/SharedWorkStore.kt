package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import com.jaxpower.ipqc.patrol.offline.OfflineSession
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import java.time.Instant
import java.util.Base64
import java.util.UUID
import okhttp3.RequestBody

internal class UploadCandidate(
    val target: ReceiptTarget,
    val batch: BatchRecord,
    val members: List<OperationEnvelope>,
    val sources: List<StoredWorkSource>,
)
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

    suspend fun observeOfflineSamples(session: SessionRepository.WorkSession, basis: OfflineSampleBasis) {
        sessions.withWorkSession(session, rows.origin) { requireCurrent ->
            rows.transaction(basis.task) {
                rememberSharedDraftVersion(basis)
                val profile = offlineProfile(session.identity.userId)?.takeIf { it.owner == session.identity }
                if (profile != null) {
                    val observed = updateBasis(profile.basis, basis.copy(editable = basis.editable && session.canContribute))
                    if (observed != profile.basis) saveOfflineProfile(profile.copy(basis = observed))
                }
                requireCurrent()
            }
        }
    }

    suspend fun invalidateOfflineSamples(session: SessionRepository.WorkSession, task: WorkTask) {
        sessions.withWorkSession(session, rows.origin) { requireCurrent ->
            rows.transaction(task) {
                val profile = offlineProfile(session.identity.userId) ?: return@transaction
                if (profile.owner != session.identity) return@transaction
                val blocked = profile.basis.copy(editable = false,
                    attempts = profile.basis.attempts.map { it.copy(applicable = false) },
                    samples = profile.basis.samples.map { it.copy(current = false) })
                if (blocked != profile.basis) saveOfflineProfile(profile.copy(basis = blocked))
                requireCurrent()
            }
        }
    }

    suspend fun armOfflineSamples(access: WorkAccess, source: WorkSource, capture: CaptureGrant, basis: OfflineSampleBasis) {
        val owner = access.session.identity
        workCheck(access.session.canContribute && access.owns(source) && capture.contextId == source.contextId,
            WorkProblem.SourceUnavailable)
        workCheck(source.task == access.task && basis.task == access.task && basis.editable, WorkProblem.ScopeMismatch)
        sessions.withWorkCaptureClock(access.session, rows.origin, requireSampleContribution = true) { now ->
            workCheck(rows.transaction(access.task) {
                access.requireCurrent()
                val old = offlineProfile(owner.userId)?.takeIf { it.owner == owner }
                val deadline = minOf(capture.until, Instant.parse(source.captureUntilUtc),
                    old?.takeIf { it.contextId == capture.contextId }?.let { Instant.parse(it.captureUntilUtc) } ?: capture.until)
                val latest = old?.let { updateBasis(it.basis, basis) } ?: basis
                workCheck(latest.sameObservation(basis), WorkProblem.StaleGrant)
                val profile = OfflineSampleProfile(owner, source.contextId, deadline.toString(), latest)
                workCheck(requireOfflineSource(profile) == source, WorkProblem.SourceUnavailable)
                rememberSharedDraftVersion(latest)
                requireCaptureTime(source, CaptureGrant(source.contextId, deadline), now)
                saveOfflineProfile(profile)
                requireCaptureTime(source, CaptureGrant(source.contextId, deadline), now)
                access.requireCurrent()
                true
            } == true, WorkProblem.StorageUnavailable)
        }
    }

    suspend fun observeBasicInformation(access: WorkAccess, basis: BasicInformationBasis) {
        workCheck(access.task == basis.task, WorkProblem.ScopeMismatch)
        transact(access, create = true) {
            requireCurrentTaskDraftVersion(itemRemovals(basis.task), basis.draftVersion)
            val owner = access.session.identity
            if (access.session.canContribute) {
                basicInformationBasis(owner)?.let { old ->
                    workCheck(old.task == basis.task && old.draftVersion <= basis.draftVersion, WorkProblem.StaleGrant)
                    for (oldField in old.fields) {
                        val observed = basis.field(oldField.field)
                        workCheck(oldField == observed ||
                            (observed.accepted?.draftVersion ?: 0) > old.draftVersion, WorkProblem.ReceiptMismatch)
                    }
                }
                saveBasicInformationBasis(owner, basis)
            }
            rememberBasicInformationDraftVersion(basis.draftVersion)
        }
    }

    // A pinned 409 establishes that this version is stale, without asserting
    // the server's replacement version or discarding any accepted facts.
    suspend fun recordBasicInformationVersionConflict(access: WorkAccess, version: Long) {
        workCheck(version == access.serverDraftVersion, WorkProblem.ScopeMismatch)
        transact(access, create = true) {
            val ledger = catalog()
            if (version > (ledger.staleThroughDraftVersion ?: -1L)) {
                save(ledger.copy(staleThroughDraftVersion = version, tracksBasicInformation = true))
            }
        }
    }

    suspend fun newBasicInformationEdit(
        task: WorkTask, owner: SessionIdentity, id: String, field: BasicInformationField,
        value: String?, predecessor: OperationReference?,
    ): BasicInformationCommand = offlineTransaction(task, owner) { profile, observe ->
        val source = requireOfflineSource(profile)
        val kind = if (predecessor == null) BasicInformationKind.CaptureBasicInformationField
            else BasicInformationKind.CorrectBasicInformationField
        val op = BasicInformationOperation(id, kind, field, value, source.contextId, source.contributorUserId,
            profile.basis.draftVersion, predecessor)
        validateBasicInformationEdit(profile, op)
        val capture = CaptureGrant(source.contextId, Instant.parse(profile.captureUntilUtc))
        requireCaptureTime(source, capture) { observe().apparentNow }
        OfflineTransactionResult(BasicInformationCommand(task, owner, op), capture.until)
    }

    suspend fun saveBasicInformation(command: BasicInformationCommand): BasicInformationEnvelope =
        offlineTransaction(command.task, command.owner) { profile, observe ->
            val op = command.operation
            workCheck(op.isValid(), WorkProblem.BasicInformationInputMismatch)
            checkedBasicInformation(command.task, op.operationId)?.let { original ->
                workCheck(original.command == command, WorkProblem.OperationIdentityConflict)
                return@offlineTransaction OfflineTransactionResult(original)
            }
            val source = requireOfflineSource(profile)
            workCheck(op.sourceWorkContextId == source.contextId && op.contributorUserId == source.contributorUserId,
                WorkProblem.SourceUnavailable)
            validateBasicInformationEdit(profile, op)
            val capture = CaptureGrant(source.contextId, Instant.parse(profile.captureUntilUtc))
            val now = observe().apparentNow
            requireCaptureTime(source, capture) { now }

            val ledger = catalog()
            val next = Math.addExact(ledger.localEditVersion, 1L)
            val bytes = BasicInformationCanonical.operation(command.task, op, next)
            val entry = BasicInformationEnvelope(workspaceId, command, source, next, CANONICAL_VERSION, bytes,
                workHash(bytes), OfflineCaptureEvidence(now.toString(), capture.until.toString()))
            reserveBasicInformationId(op.operationId)
            saveBasicInformation(entry)
            save(ledger.copy(localEditVersion = next, basicInformationIds = ledger.basicInformationIds + op.operationId))
            OfflineTransactionResult(entry, capture.until)
        }

    suspend fun offlineBasicInformationDraft(task: WorkTask, owner: SessionIdentity): LocalBasicInformationDraft =
        offlineTransaction(task, owner) { profile, observe ->
            val accepted = basicInformationBasis(owner)
            val block = captureProblem(profile, observe) ?: try {
                requireBasicInformationBasis(profile)
                null
            } catch (failure: WorkFailure) {
                failure.problem
            }
            val version = profile.basis.draftVersion
            val current = accepted?.draftVersion == version && block != WorkProblem.StaleGrant
            val proposals = basicInformationHistory(task, owner).map {
                LocalBasicInformationProposal(it, current && it.command.operation.baseDraftVersion == version,
                    sourceRevoked(it.source.contextId))
            }
            OfflineTransactionResult(LocalBasicInformationDraft(accepted, proposals, catalog().localEditVersion, block))
        }

    private suspend fun WorkTransaction.requireBasicInformationBasis(profile: OfflineSampleProfile): BasicInformationBasis {
        val task = profile.basis.task
        val basis = basicInformationBasis(profile.owner) ?: throw WorkFailure(WorkProblem.SourceUnavailable)
        workCheck(basis.task == task && basis.availability.editable && profile.basis.editable, WorkProblem.ScopeMismatch)
        val version = basis.draftVersion
        workCheck(version == profile.basis.draftVersion, WorkProblem.StaleGrant)
        checkItemRemovalBarrier(task, version)
        checkSampleBatchBarrier(version)
        workCheck(catalog().operationIds.none { (individualReceipt(it)?.draftVersion ?: 0) > version }, WorkProblem.StaleGrant)
        return basis
    }

    private suspend fun WorkTransaction.validateBasicInformationEdit(profile: OfflineSampleProfile, op: BasicInformationOperation) {
        workCheck(op.isValid(), WorkProblem.BasicInformationInputMismatch)
        val basis = requireBasicInformationBasis(profile)
        val task = basis.task
        val version = basis.draftVersion
        workCheck(op.baseDraftVersion == version, WorkProblem.StaleGrant)
        val accepted = basis.field(op.field).accepted
        workCheck(op.predecessor == accepted?.reference(), WorkProblem.DependencyMismatch)
        workCheck(basis.fields.none { it.accepted?.operation?.operationId == op.operationId } &&
            profile.basis.samples.none { op.operationId == it.valueOperationId || op.operationId == it.lastOperationId } &&
            (profile.basis.historicalItemRemovals + profile.basis.attempts.mapNotNull { it.serverRemoval })
                .none { it.operationId == op.operationId }, WorkProblem.OperationIdentityConflict)
        workCheck(basicInformationHistory(task, profile.owner).none {
            val pending = it.command.operation
            pending.field == op.field && pending.baseDraftVersion == version && !sourceRevoked(pending.sourceWorkContextId)
        }, WorkProblem.InvalidState)
    }

    suspend fun newItemRemoval(task: WorkTask, owner: SessionIdentity, attempt: WorkAttempt, id: String,
                               confirmed: Boolean, confirmation: RemovalConfirmation, reason: String?): ItemRemovalCommand =
        offlineTransaction(task, owner) { profile, observe ->
            val cmd = ItemRemovalCommand(task, attempt, id, owner, profile.contextId, profile.basis.draftVersion,
                confirmed, confirmation, reason)
            workCheck(cmd.isValid(), WorkProblem.ItemRemovalInputMismatch)
            profile.basis.requireItemRemovalAttempt(attempt)
            val source = requireOfflineSource(profile)
            val capture = CaptureGrant(source.contextId, Instant.parse(profile.captureUntilUtc))
            requireCaptureTime(source, capture) { observe().apparentNow }
            OfflineTransactionResult(cmd, capture.until)
        }

    suspend fun saveItemRemoval(command: ItemRemovalCommand): ItemRemovalEnvelope =
        offlineTransaction(command.task, command.owner) { profile, observe ->
            workCheck(command.isValid(), WorkProblem.ItemRemovalInputMismatch)
            checkedItemRemoval(command.task, command.operationId)?.let { current ->
                workCheck(current.removal.command == command, WorkProblem.OperationIdentityConflict)
                return@offlineTransaction OfflineTransactionResult(current.removal)
            }

            val basis = profile.basis
            workCheck(command.expectedDraftVersion == basis.draftVersion, WorkProblem.StaleGrant)
            workCheck(command.contextId == profile.contextId, WorkProblem.SourceUnavailable)
            val frozen = basis.requireItemRemovalAttempt(command.attempt)
            val original = requireOfflineSource(profile)
            val capture = CaptureGrant(original.contextId, Instant.parse(profile.captureUntilUtc))
            val now = observe().apparentNow
            requireCaptureTime(original, capture) { now }
            val entries = itemRemovals(command.task)
            requireCurrentTaskDraftVersion(entries, basis.draftVersion)
            val turn = itemRemovalTurn(entries, basis.draftVersion)
            workCheck(turn?.removal?.command?.attempt != command.attempt && entries.none {
                it.state in uncertainItemRemovalStates && it.removal.command.attempt == command.attempt
            }, WorkProblem.ItemDispositionConflict)
            workCheck(turn == null && entries.none {
                it.state in uncertainItemRemovalStates || (it.state == WorkState.Queued &&
                    it.removal.command.expectedDraftVersion > basis.draftVersion &&
                    !sourceRevoked(it.removal.command.contextId))
            }, WorkProblem.ItemRemovalReconciliationRequired)
            requireFreshSampleBasis(basis, sampleHistory(profile))

            val ledger = catalog()
            val next = Math.addExact(ledger.localEditVersion, 1L)
            val bytes = ItemRemovalCanonical.operation(command, next)
            val result = ItemRemovalEnvelope(workspaceId, command, original, next, frozen.required,
                CANONICAL_VERSION, bytes, workHash(bytes), OfflineCaptureEvidence(now.toString(), capture.until.toString()))
            reserveItemRemovalId(command.operationId)
            saveItemRemoval(ItemRemovalEntry(result))
            save(ledger.copy(localEditVersion = next, itemRemovalIds = ledger.itemRemovalIds + command.operationId))
            OfflineTransactionResult(result, capture.until)
        }

    suspend fun itemDispositionDraft(task: WorkTask, owner: SessionIdentity): LocalItemDispositionDraft =
        offlineTransaction(task, owner) { profile, _ ->
            OfflineTransactionResult(projectItemRemovals(profile.basis, owner,
                sampleBasisProblem(profile.basis, sampleHistory(profile)) == WorkProblem.StaleGrant))
        }

    suspend fun itemRemovalWorkbench(access: WorkAccess, basis: OfflineSampleBasis): WorkbenchRemoval {
        workCheck(access.task == basis.task && access.serverDraftVersion == basis.draftVersion, WorkProblem.ScopeMismatch)
        if (access.session.canContribute) {
            transact(access) { projectItemRemovals(basis, null).forWorkbench() }?.let { return it }
        }
        return WorkbenchRemoval(
            basis.attempts.map { WorkbenchRemoval.Item(it.attempt, it.serverDisposition,
                it.serverRemoval?.warnsLastApplicableItemRemoval == true) },
            emptyList(), emptySet(), false, false,
            basis.attempts.isNotEmpty() && basis.attempts.all { it.serverDisposition == ItemDisposition.Removed }, false)
    }

    private suspend fun WorkTransaction.projectItemRemovals(basis: OfflineSampleBasis, owner: SessionIdentity?,
                                                           sampleStale: Boolean = false): LocalItemDispositionDraft {
        val byAttempt = basis.attempts.associateBy { it.attempt }
        val observed = (basis.historicalItemRemovals + basis.attempts.mapNotNull { it.serverRemoval })
            .associateBy { it.operationId }
        val stored = itemRemovals(basis.task)
        val stale = taskDraftVersionProblem(stored, basis.draftVersion) != null || sampleStale
        val turn = itemRemovalTurn(stored, basis.draftVersion)?.takeIf { candidate ->
            stored.none { it.state in uncertainItemRemovalStates && it != candidate }
        }
        // Only an authenticated contribution grant may project the shared queue.
        // The online projection exposes references and status, never originals or proofs.
        val state = stored.filter { owner == null || it.removal.command.owner == owner || it.state == WorkState.Accepted }
            .map { entry ->
                val cmd = entry.removal.command
                val current = byAttempt[cmd.attempt]
                val evidence = entry.serverEvidence ?: observed[cmd.operationId]?.takeIf { entry.removal.matches(it) }
                val status = when {
                    entry.state != WorkState.Queued || evidence != null -> entry.state
                    sourceRevoked(cmd.contextId) -> WorkState.Quarantined
                    stale || turn?.removal?.command?.operationId != cmd.operationId ||
                        current?.serverDisposition != ItemDisposition.Applicable -> WorkState.Conflict
                    else -> WorkState.Queued
                }
                LocalItemRemovalHistory(entry.removal, status, current != null, entry.batch, evidence)
            }
        val provisional = turn?.removal?.takeIf { owner == null || it.command.owner == owner }
        val projection = basis.attempts.map { item ->
            ProjectedItemDisposition(item.attempt, item.required, item.serverDisposition, item.serverRemoval,
                provisional?.takeIf {
                    it.command.attempt == item.attempt && basis.editable && !stale &&
                        item.serverDisposition == ItemDisposition.Applicable
                }?.reference())
        }
        val blocksNew = turn != null || stored.any {
            it.state in uncertainItemRemovalStates || (it.state == WorkState.Queued &&
                it.removal.command.expectedDraftVersion > basis.draftVersion && !sourceRevoked(it.removal.command.contextId))
        }
        return LocalItemDispositionDraft(basis.draftVersion, catalog().localEditVersion, projection, basis.samples, state,
            basis.historicalItemRemovals, stale, sampleItemRemovalBlocks(stored, basis.draftVersion), blocksNew)
    }

    suspend fun prepareItemRemovalBatch(task: WorkTask, owner: SessionIdentity, id: String,
                                        operation: OperationReference): ItemRemovalBatch =
        offlineTransaction(task, owner) { profile, _ ->
            val entry = checkedItemRemoval(task, operation.operationId) ?: throw WorkFailure(WorkProblem.InvalidState)
            workCheck(entry.removal.command.owner == owner, WorkProblem.SourceUnavailable)
            if (entry.batch == null) {
                workCheck(entry.removal.command.expectedDraftVersion == profile.basis.draftVersion, WorkProblem.StaleGrant)
                requireFreshSampleBasis(profile.basis, sampleHistory(profile))
            }
            OfflineTransactionResult(sealItemRemoval(profile.basis, id, operation, entry))
        }

    suspend fun prepareItemRemovalForReconciliation(access: WorkAccess, basis: OfflineSampleBasis, id: String,
                                                     operation: OperationReference): ItemRemovalBatch =
        transact(access) {
            val entry = checkedItemRemoval(access.task, operation.operationId) ?: throw WorkFailure(WorkProblem.InvalidState)
            workCheck(access.session.canContribute && access.approves(entry.removal.source) &&
                !sourceRevoked(entry.removal.source.contextId), WorkProblem.SourceUnavailable)
            workCheck(basis.task == access.task && basis.draftVersion == access.serverDraftVersion, WorkProblem.StaleGrant)
            sealItemRemoval(basis, id, operation, entry)
        } ?: throw WorkFailure(WorkProblem.StorageUnavailable)

    private suspend fun WorkTransaction.sealItemRemoval(basis: OfflineSampleBasis, id: String,
                                                        operation: OperationReference, entry: ItemRemovalEntry): ItemRemovalBatch {
        val task = basis.task
        val cmd = entry.removal.command
        workCheck(isWorkId(id) && entry.removal.reference() == operation, WorkProblem.BatchIdentityConflict)
        entry.batch?.let {
            workCheck(it.batchId == id, WorkProblem.BatchIdentityConflict)
            return it
        }
        workCheck(entry.state == WorkState.Queued && cmd.expectedDraftVersion == basis.draftVersion, WorkProblem.StaleGrant)
        workCheck(basis.requireItemRemovalAttempt(cmd.attempt).required == entry.removal.minimumSampleCount, WorkProblem.ScopeMismatch)
        checkItemRemovalBarrier(task, basis.draftVersion)
        checkSampleBatchBarrier(basis.draftVersion)
        val entries = itemRemovals(task)
        workCheck(batch(id) == null && entries.none { it.batch?.batchId == id }, WorkProblem.BatchIdentityConflict)
        val original = requireSource(cmd.contextId, task)
        workCheck(original.source == entry.removal.source && validProof(original.proof) &&
            !sourceRevoked(cmd.contextId), WorkProblem.SourceUnavailable)
        workCheck(itemRemovalTurn(entries, basis.draftVersion)?.removal?.reference() == operation,
            WorkProblem.ItemRemovalReconciliationRequired)

        val sealed = ItemRemovalCanonical.batch(entry.removal, id)
        saveItemRemoval(entry.copy(batch = sealed))
        return sealed
    }

    suspend fun startItemRemovalAttempt(access: WorkAccess, observed: OfflineSampleBasis, id: String) {
        workCheck(transact(access) {
            val candidate = itemRemovalUploadCandidate(access, observed, id)
            workCheck(access.approves(candidate.source.source), WorkProblem.SourceUnavailable)
            saveItemRemoval(candidate.entry.copy(state = WorkState.InFlight))
            true
        } == true, WorkProblem.StorageUnavailable)
    }

    suspend fun inspectItemRemovalUpload(access: WorkAccess, observed: OfflineSampleBasis, id: String): ItemRemovalUploadCandidate =
        transact(access) { itemRemovalUploadCandidate(access, observed, id) }
            ?: throw WorkFailure(WorkProblem.StorageUnavailable)

    suspend fun beginItemRemovalUpload(access: WorkAccess, observed: OfflineSampleBasis,
                                      candidate: ItemRemovalUploadCandidate): RequestBody = transact(access) {
        val latest = itemRemovalUploadCandidate(access, observed, candidate.target.batchId)
        workCheck(latest.target == candidate.target && latest.entry == candidate.entry &&
            latest.source.source == candidate.source.source && latest.source.proof == candidate.source.proof,
            WorkProblem.StaleGrant)
        workCheck(access.approves(latest.source.source), WorkProblem.SourceUnavailable)
        val body = latest.encodeBatch()
        saveItemRemoval(latest.entry.copy(state = WorkState.InFlight))
        body
    } ?: throw WorkFailure(WorkProblem.StorageUnavailable)

    private suspend fun WorkTransaction.itemRemovalUploadCandidate(access: WorkAccess, observed: OfflineSampleBasis,
                                                                   id: String): ItemRemovalUploadCandidate {
        val entries = itemRemovals(access.task)
        val entry = entries.singleOrNull { it.batch?.batchId == id } ?: throw WorkFailure(WorkProblem.InvalidState)
        val batch = requireNotNull(entry.batch)
        val cmd = entry.removal.command
        val original = requireSource(cmd.contextId, access.task)
        workCheck(access.session.canContribute && original.source == entry.removal.source && validProof(original.proof) &&
            cmd.contextId !in access.securityRevokedSources && !sourceRevoked(cmd.contextId), WorkProblem.SourceUnavailable)
        workCheck(entry.state == WorkState.Queued && cmd.expectedDraftVersion == access.serverDraftVersion &&
            observed.task == access.task && observed.draftVersion == access.serverDraftVersion &&
            cmd.attempt in access.currentAttempts, WorkProblem.InvalidState)
        workCheck(observed.requireItemRemovalAttempt(cmd.attempt).required == entry.removal.minimumSampleCount,
            WorkProblem.ScopeMismatch)
        workCheck(entries.none { it.state in uncertainItemRemovalStates }, WorkProblem.InvalidState)
        requireCurrentTaskDraftVersion(entries, access.serverDraftVersion)
        checkSampleBatchBarrier(access.serverDraftVersion)
        workCheck(itemRemovalTurn(entries, access.serverDraftVersion)?.removal?.reference() == entry.removal.reference(),
            WorkProblem.ItemRemovalReconciliationRequired)
        return ItemRemovalUploadCandidate(ReceiptTarget(ReceiptLookup(ReceiptKind.Batch, id), access.task,
            workspaceId, id, batch.fingerprint), entry, original)
    }

    // Loss of authority can only make an exact committed attempt query-only.
    suspend fun leaveItemRemovalUncertain(candidate: ItemRemovalUploadCandidate) {
        val target = candidate.target
        rows.transaction(target.task) {
            val entry = checkedItemRemoval(target.task, candidate.entry.removal.command.operationId)
            if (entry != null && workspaceId == target.workspaceId && entry.batch == candidate.entry.batch &&
                entry.removal == candidate.entry.removal && entry.state == WorkState.InFlight)
                saveItemRemoval(entry.copy(state = WorkState.OutcomeUnknown))
        }
    }

    suspend fun applyItemRemovalUploadRejection(access: WorkAccess, candidate: ItemRemovalUploadCandidate,
                                               rejection: UploadRejection, preflight: Boolean): Boolean = transact(access) {
        val command = candidate.entry.removal.command
        val entry = checkedItemRemoval(access.task, command.operationId) ?: return@transact false
        val expectedState = if (preflight) WorkState.Queued else WorkState.InFlight
        if (workspaceId != candidate.target.workspaceId || entry.batch != candidate.entry.batch ||
            entry.removal != candidate.entry.removal || entry.state != expectedState || entry.serverEvidence != null)
            return@transact false

        val reason = rejection.reason
        if (reason.isSourceRejection()) {
            if (rejection.contexts != setOf(command.contextId) ||
                rejection.operations != (if (preflight) emptySet() else setOf(command.operationId))) return@transact false
            if (reason == WorkConflict.SourceSecurityRevoked) rememberSourceRevoked(command.contextId)
        } else if (preflight) return@transact false
        val state = if (reason.isSourceRejection()) WorkState.Quarantined else WorkState.Conflict
        saveItemRemoval(entry.copy(state = state))
        true
    } == true

    suspend fun markItemRemovalOutcomeUnknown(access: WorkAccess, id: String) {
        workCheck(transact(access) {
            val entry = itemRemovals(access.task).singleOrNull { it.batch?.batchId == id }
                ?: throw WorkFailure(WorkProblem.InvalidState)
            workCheck(entry.state in uncertainItemRemovalStates, WorkProblem.InvalidState)
            if (entry.state == WorkState.InFlight) saveItemRemoval(entry.copy(state = WorkState.OutcomeUnknown))
            true
        } == true, WorkProblem.StorageUnavailable)
    }

    suspend fun locateItemRemovalReceipt(access: WorkAccess, lookup: ReceiptLookup): ReceiptTarget? =
        transact(access) { itemRemovalReceiptTarget(access.task, lookup) }

    private suspend fun WorkTransaction.itemRemovalReceiptTarget(task: WorkTask, lookup: ReceiptLookup): ReceiptTarget? {
        if (!isWorkId(lookup.id)) return null
        val entry = when (lookup.kind) {
            ReceiptKind.Batch -> itemRemovals(task).singleOrNull { it.batch?.batchId == lookup.id }
            ReceiptKind.Operation -> checkedItemRemoval(task, lookup.id)
        }
        val batch = entry?.batch ?: return null
        if (entry.state !in recoverableItemRemovalStates && entry.state != WorkState.Queued) return null
        return ReceiptTarget(lookup, task, workspaceId, batch.batchId, batch.fingerprint)
    }

    suspend fun reconcileItemRemovalReceipt(access: WorkAccess, target: ReceiptTarget,
                                            reply: ItemRemovalReceiptReply): ItemRemovalReceiptRecovery =
        transact(access) {
            workCheck(itemRemovalReceiptTarget(access.task, target.lookup) == target, WorkProblem.StaleGrant)
            val entry = itemRemovals(access.task).single { it.batch?.batchId == target.batchId }
            try {
                when (reply) {
                    is ItemRemovalReceiptReply.Batch -> {
                        workCheck(target.lookup.kind == ReceiptKind.Batch, WorkProblem.ReceiptMismatch)
                        verifyItemRemovalBatchReceipt(entry, reply.receipt)
                        workCheck(entry.batchReceipt == null || entry.batchReceipt == reply.receipt, WorkProblem.ReceiptMismatch)
                        if (entry.batchReceipt == null)
                            saveItemRemoval(entry.copy(state = WorkState.Accepted, batchReceipt = reply.receipt))
                        ItemRemovalReceiptRecovery.BatchConfirmed(reply.receipt)
                    }
                    is ItemRemovalReceiptReply.Operation -> {
                        workCheck(target.lookup.kind == ReceiptKind.Operation, WorkProblem.ReceiptMismatch)
                        verifyItemRemovalReceipt(entry, reply.receipt)
                        val prior = entry.batchReceipt?.itemOperations?.single() ?: entry.individualReceipt
                        workCheck(prior == null || prior == reply.receipt, WorkProblem.ReceiptMismatch)
                        if (prior == null) saveItemRemoval(entry.copy(individualReceipt = reply.receipt))
                        // Individual evidence cannot release the batch barrier.
                        ItemRemovalReceiptRecovery.OperationObserved(reply.receipt)
                    }
                    ItemRemovalReceiptReply.Missing -> ItemRemovalReceiptRecovery.Unresolved(ReceiptUncertainty.NotObserved)
                    ItemRemovalReceiptReply.Conflict -> itemReceiptConflict(target.lookup, ReceiptConflictReason.QueryConflict)
                    is ItemRemovalReceiptReply.Unavailable -> ItemRemovalReceiptRecovery.TransientFailure(reply.issue)
                }
            } catch (failure: WorkFailure) {
                if (failure.problem != WorkProblem.ReceiptMismatch) throw failure
                itemReceiptConflict(target.lookup, ReceiptConflictReason.ResponseMismatch)
            }
        } ?: ItemRemovalReceiptRecovery.Unresolved(ReceiptUncertainty.NoLocalAttempt)

    private suspend fun WorkTransaction.itemReceiptConflict(lookup: ReceiptLookup, reason: ReceiptConflictReason):
        ItemRemovalReceiptRecovery.Conflict {
        rememberReceiptConflict(lookup, reason)
        return ItemRemovalReceiptRecovery.Conflict(reason)
    }

    private suspend fun WorkTransaction.checkSampleBatchBarrier(observedVersion: Long) {
        val batches = catalog().batchIds.map { requireBatch(it) }
        workCheck(batches.none { it.state in uncertainItemRemovalStates }, WorkProblem.InvalidState)
        workCheck(batches.none { (it.receipt?.draftVersion ?: 0) > observedVersion }, WorkProblem.StaleGrant)
    }

    suspend fun saveOfflineSample(task: WorkTask, edit: OfflineSampleEdit, expected: SessionIdentity?): WorkOperation =
        offlineTransaction(task, expected) { profile, observe ->
            operation(edit.operationId)?.let { old ->
                val existing = checkedEntry(task, old.envelope.reference()).envelope
                workCheck(profile.owner.owns(existing.source) && old.offlineCapture != null &&
                    edit.operation(existing.source.contextId, existing.operation.baseDraftVersion) == existing.operation,
                    WorkProblem.OperationIdentityConflict)
                return@offlineTransaction OfflineTransactionResult(describe(old))
            }

            val source = requireOfflineSource(profile)
            val capture = CaptureGrant(source.contextId, Instant.parse(profile.captureUntilUtc))
            val capturedAt = observe().apparentNow
            requireCaptureTime(source, capture) { capturedAt }
            val history = sampleHistory(profile)
            requireFreshSampleBasis(profile.basis, history)
            val op = edit.operation(source.contextId, profile.basis.draftVersion)
            workCheck(itemRemoval(op.operationId) == null, WorkProblem.OperationIdentityConflict)
            val removals = itemRemovals(task)
            requireCurrentTaskDraftVersion(removals, profile.basis.draftVersion)
            checkSampleItemRemovals(removals, profile.basis.draftVersion, setOf(op.attempt))
            val board = sampleBoard(profile.basis, history)
            if (op.kind == SampleKind.CaptureSample) {
                workCheck(history.none {
                    val prior = it.operation.envelope.operation
                    prior.attempt == edit.key.attempt && prior.clientSampleId == edit.key.clientSampleId
                }, WorkProblem.OperationIdentityConflict)
            }
            board.validate(edit.key, op)
            val result = persistOperation(source, op, OfflineCaptureEvidence(capturedAt.toString(), capture.until.toString()))
            OfflineTransactionResult(result, capture.until)
        }

    suspend fun findOfflineSample(task: WorkTask, id: String, expected: SessionIdentity?): WorkOperation? =
        offlineTransaction(task, expected) { profile, _ ->
            require(isWorkId(id))
            val saved = operation(id)?.takeIf { it.offlineCapture != null && profile.owner.owns(it.envelope.source) }
            OfflineTransactionResult(saved?.let { describe(checkedEntry(task, it.envelope.reference())) })
        }

    suspend fun offlineSampleDraft(task: WorkTask, expected: SessionIdentity?): LocalSampleDraft =
        offlineTransaction(task, expected) { profile, observe ->
            val history = sampleHistory(profile)
            val board = sampleBoard(profile.basis, history)
            val block = captureProblem(profile, observe) ?: sampleBasisProblem(profile.basis, history)
            val removal = projectItemRemovals(profile.basis, profile.owner, block == WorkProblem.StaleGrant)
            OfflineTransactionResult(LocalSampleDraft(profile.basis.draftVersion, catalog().localEditVersion,
                profile.basis.samples, board.values(), history, board.progress(), block, removal.forWorkbench()))
        }

    // Reuse the same clock, source and task-wide version checks as the draft projection.
    suspend fun offlineCaptureProblem(task: WorkTask, expected: SessionIdentity): WorkProblem? =
        offlineTransaction(task, expected) { profile, observe -> OfflineTransactionResult(captureProblem(profile, observe)) }

    private suspend fun WorkTransaction.captureProblem(profile: OfflineSampleProfile, observe: suspend () -> OfflineSession):
        WorkProblem? = try {
        val basis = profile.basis
        requireCurrentTaskDraftVersion(itemRemovals(basis.task), basis.draftVersion)
        workCheck(basis.editable, WorkProblem.ScopeMismatch)
        val source = requireOfflineSource(profile)
        val cap = CaptureGrant(source.contextId, Instant.parse(profile.captureUntilUtc))
        requireCaptureTime(source, cap) { observe().apparentNow }
        null
    } catch (failure: WorkFailure) {
        failure.problem
    }

    suspend fun prepareOfflineSampleBatch(task: WorkTask, id: String, operationIds: List<String>): BatchRecord {
        val ids = operationIds.sorted()
        require(isWorkId(id) && ids.size in 1..50 && ids.distinct().size == ids.size)
        return offlineTransaction(task) { profile, _ ->
            batch(id)?.let { saved ->
                workCheck(saved.operations.map { it.operationId } == ids, WorkProblem.BatchIdentityConflict)
                workCheck(sealedMembers(task, saved).all { profile.owner.owns(it.source) }, WorkProblem.SourceUnavailable)
                return@offlineTransaction OfflineTransactionResult(saved)
            }
            workCheck(catalog().batchIds.none {
                requireBatch(it).state in setOf(WorkState.InFlight, WorkState.OutcomeUnknown)
            }, WorkProblem.InvalidState)
            val history = sampleHistory(profile)
            requireFreshSampleBasis(profile.basis, history)
            val entries = ids.map { requireOperation(it) }.map { checkedEntry(task, it.envelope.reference()) }
            workCheck(entries.all { it.batchId == null && it.quarantine == null && it.offlineCapture != null },
                WorkProblem.InvalidState)
            workCheck(entries.all {
                profile.owner.owns(it.envelope.source) && !sourceRevoked(it.envelope.source.contextId)
            }, WorkProblem.SourceUnavailable)
            workCheck(entries.all { it.envelope.operation.baseDraftVersion == profile.basis.draftVersion },
                WorkProblem.ScopeMismatch)
            // Validate the actual subset and dependency order, without borrowing any unselected queued edit.
            val board = LocalSampleBoard(profile.basis, history.associateBy { it.operation.envelope.operation.operationId })
            for (entry in entries.sortedBy { it.envelope.originLocalEditVersion }) {
                val op = entry.envelope.operation
                board.validate(LocalSampleKey(op.attempt, op.clientSampleId), op, checkCapacity = false)
                board.overlay(describe(entry), suppliesPredecessor = true)
            }
            // The server checks its sample limit on the final atomic batch projection.
            workCheck(board.progress().all { it.applicableSamples <= MAX_CURRENT_SAMPLES }, WorkProblem.SampleLimit)
            OfflineTransactionResult(sealBatch(task, id, entries))
        }
    }

    private suspend fun WorkTransaction.sampleHistory(profile: OfflineSampleProfile): List<LocalSampleHistory> {
        val scope = profile.basis
        val applicable = scope.attempts.filter { it.applicable }.map { it.attempt }.toSet()
        return catalog().operationIds.mapNotNull { id ->
            val entry = requireOperation(id)
            val op = describe(entry)
            val owner = profile.owner.owns(op.envelope.source)
            if (!owner && op.state != WorkState.Accepted) return@mapNotNull null
            checkedEntry(scope.task, entry.envelope.reference())
            val accepted = acceptedOperation(entry)
            val individual = individualReceipt(id)
            workCheck(accepted == null || individual == null || accepted == individual, WorkProblem.StorageUnavailable)
            (accepted ?: individual)?.let { verifyOperationReceipt(scope.task, entry.envelope, it) }
            val data = entry.envelope.operation
            val mayReferencePending = entry.offlineCapture != null && !sourceRevoked(op.envelope.source.contextId)
            LocalSampleHistory(op, data.attempt in applicable, data.baseDraftVersion == scope.draftVersion, accepted,
                individual, entry.offlineCapture?.atUtc, mayReferencePending)
        }
    }

    private fun sampleBoard(basis: OfflineSampleBasis, history: List<LocalSampleHistory>): LocalSampleBoard =
        LocalSampleBoard(basis, history.associateBy { it.operation.envelope.operation.operationId }).also { board ->
            history.sortedBy { it.operation.envelope.originLocalEditVersion }.forEach(board::applyPending)
        }

    private fun requireFreshSampleBasis(basis: OfflineSampleBasis, history: List<LocalSampleHistory>) {
        sampleBasisProblem(basis, history)?.let { throw WorkFailure(it) }
    }

    private fun sampleBasisProblem(basis: OfflineSampleBasis, history: List<LocalSampleHistory>): WorkProblem? = when {
        !basis.editable -> WorkProblem.ScopeMismatch
        history.any { ((it.acceptedReceipt ?: it.individualEvidence)?.draftVersion ?: 0) > basis.draftVersion } ->
            WorkProblem.StaleGrant
        else -> null
    }

    private fun updateBasis(old: OfflineSampleBasis, observed: OfflineSampleBasis): OfflineSampleBasis = when {
        observed.draftVersion > old.draftVersion -> observed.withRemovalHistoryFrom(old)
        observed.draftVersion < old.draftVersion -> old
        old.sameObservation(observed) -> old
        old.isLegacyObservationOf(observed) -> observed.withRemovalHistoryFrom(old)
        // Equal-version disagreement may be a changed admission or inconsistent
        // response. Retain the verified observation and deny further writes.
        else -> observed.copy(editable = false).withRemovalHistoryFrom(old)
    }

    private suspend fun WorkTransaction.requireOfflineSource(profile: OfflineSampleProfile): WorkSource {
        val context = requireSource(profile.contextId, profile.basis.task)
        val state = issuance(profile.owner.userId)
        workCheck(profile.owner.owns(context.source) && validProof(context.proof) &&
            context.issuedRevision == 1L && context.issuedAuthVersion != null &&
            state.pendingId == null && state.contextId == profile.contextId && !sourceRevoked(profile.contextId),
            WorkProblem.SourceUnavailable)
        return context.source
    }

    private fun SessionIdentity.owns(source: WorkSource): Boolean =
        userId == source.contributorUserId && familyId == source.originPersonalSessionId &&
            environment == source.task.environmentId

    private suspend fun <T> offlineTransaction(task: WorkTask, expected: SessionIdentity? = null,
        action: suspend WorkTransaction.(OfflineSampleProfile, suspend () -> OfflineSession) -> OfflineTransactionResult<T>,
    ): T = sessions.withOfflineSampleSession(rows.origin) { observe ->
        val admission = observe()
        workCheck(admission.identity.environment == task.environmentId, WorkProblem.ScopeMismatch)
        // A visible task belongs to the operator whose detail was validated, even
        // if another operator has since signed in and armed this same task.
        workCheck(expected == null || admission.identity == expected, WorkProblem.SourceUnavailable)
        val result = rows.transaction(task) {
            val profile = offlineProfile(admission.identity.userId) ?: throw WorkFailure(WorkProblem.SourceUnavailable)
            workCheck(profile.owner == admission.identity && profile.basis.task == task, WorkProblem.SourceUnavailable)
            val result = action(profile, observe)
            val final = observe()
            workCheck(final.identity == profile.owner, WorkProblem.SourceUnavailable)
            result.captureDeadline?.let {
                workCheck(final.apparentNow < it, WorkProblem.CaptureWindowClosed)
            }
            currentCoroutineContext().ensureActive()
            result
        } ?: throw WorkFailure(WorkProblem.SourceUnavailable)
        result.value
    }

    private class OfflineTransactionResult<T>(val value: T, val captureDeadline: Instant? = null)

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
                workCheck(itemRemoval(op.operationId) == null, WorkProblem.OperationIdentityConflict)
                val removals = itemRemovals(access.task)
                requireCurrentTaskDraftVersion(removals, access.serverDraftVersion)
                checkSampleItemRemovals(removals, access.serverDraftVersion, setOf(op.attempt))
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
                val result = persistOperation(context.source, op)
                requireCaptureTime(context.source, access.captureGrant, captureTime)
                access.requireCurrent()
                result
            })
        }
    }

    private suspend fun WorkTransaction.persistOperation(source: WorkSource, op: SampleOperation,
                                                         evidence: OfflineCaptureEvidence? = null): WorkOperation {
        val catalog = catalog()
        val next = Math.addExact(catalog.localEditVersion, 1L)
        val payload = DraftOperationCanonical.operation(source, op, next)
        val entry = OperationEntry(OperationEnvelope(workspaceId, op, source, next, CANONICAL_VERSION, payload,
            workHash(payload)), offlineCapture = evidence)
        reserveOperationId(op.operationId)
        save(entry)
        save(catalog.copy(localEditVersion = next, operationIds = catalog.operationIds + op.operationId))
        return describe(entry)
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
            val sample = operation(operationId)
            val original = if (sample != null) {
                workCheck(describe(sample).state == WorkState.Queued, WorkProblem.InvalidState)
                sample.envelope.source
            } else {
                val item = checkedItemRemoval(access.task, operationId) ?: throw WorkFailure(WorkProblem.SourceUnavailable)
                workCheck(item.state == WorkState.Queued && access.session.canContribute, WorkProblem.InvalidState)
                item.removal.source
            }
            val stored = requireSource(original.contextId, access.task)
            workCheck(stored.source == original && validProof(stored.proof) &&
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
            sealBatch(access.task, id, entries)
        }) { "Work task unavailable" }
    }

    private suspend fun WorkTransaction.sealBatch(task: WorkTask, id: String, entries: List<OperationEntry>): BatchRecord {
        val expected = entries.first().envelope.operation.baseDraftVersion
        checkItemRemovalBarrier(task, expected)
        val removals = itemRemovals(task)
        checkSampleItemRemovals(removals, expected, entries.map { it.envelope.operation.attempt }.toSet())
        workCheck(removals.none { it.batch?.batchId == id }, WorkProblem.BatchIdentityConflict)
        val members = entries.map { it.envelope.reference() }
        val ids = members.map { it.operationId }.toSet()
        val accepted = entries.flatMap { it.envelope.operation.dependencies }.distinct()
            .filter { it.operationId !in ids }.sortedBy { it.operationId }
        workCheck(accepted.size <= 100 && accepted.all { ref ->
            val entry = requireOperation(ref.operationId)
            entry.envelope.reference() == ref && describe(entry).state == WorkState.Accepted
        }, WorkProblem.DependencyMismatch)
        val prepared = BatchRecord(id, expected, members, accepted, CANONICAL_VERSION,
            DraftOperationCanonical.batch(task, id, expected, members, accepted))
        save(prepared)
        entries.forEach { save(it.copy(batchId = id)) }
        val catalog = catalog()
        save(catalog.copy(batchIds = catalog.batchIds + id))
        return prepared
    }

    suspend fun batch(access: WorkAccess, id: String): BatchRecord? = transact(access) {
        val result = batch(id) ?: return@transact null
        ensureBatchRead(access, result)
        result
    }

    suspend fun startAttempt(access: WorkAccess, id: String) {
        transition(access, id) { batch ->
            checkItemRemovalBarrier(access.task, access.serverDraftVersion)
            workCheck(batch.state == WorkState.Queued && catalog().batchIds.none { other ->
                other != id && requireBatch(other).state in setOf(WorkState.InFlight, WorkState.OutcomeUnknown)
            }, WorkProblem.InvalidState)
            workCheck(access.serverDraftVersion == batch.expectedDraftVersion, WorkProblem.ScopeMismatch)
            val operations = sealedMembers(access.task, batch)
            checkSampleItemRemovals(itemRemovals(access.task), batch.expectedDraftVersion, operations.map { it.operation.attempt }.toSet())
            workCheck(operations.all { op ->
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
            for (entry in itemRemovals(access.task)) {
                if (entry.state == WorkState.InFlight) saveItemRemoval(entry.copy(state = WorkState.OutcomeUnknown))
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

    suspend fun inspectUpload(access: WorkAccess, id: String): UploadCandidate =
        transact(access) { uploadCandidate(access, id) } ?: throw WorkFailure(WorkProblem.InvalidState)

    suspend fun beginUpload(access: WorkAccess, candidate: UploadCandidate): RequestBody =
        transact(access) {
            val latest = uploadCandidate(access, candidate.target.batchId)
            workCheck(latest.target == candidate.target &&
                latest.sources.map { it.source to it.proof } == candidate.sources.map { it.source to it.proof },
                WorkProblem.StaleGrant)
            workCheck(latest.sources.all { access.approves(it.source) }, WorkProblem.SourceUnavailable)
            val encoded = latest.encodeBatch()
            save(latest.batch.copy(state = WorkState.InFlight))
            encoded
        } ?: throw WorkFailure(WorkProblem.StorageUnavailable)

    // This only makes a committed attempt query-only. It can run after logout
    // and cannot replace accepted or rejected evidence from another exchange.
    suspend fun leaveUploadUncertain(target: ReceiptTarget) {
        rows.transaction(target.task) {
            val batch = batch(target.batchId)
            if (workspaceId == target.workspaceId && batch?.fingerprint == target.fingerprint &&
                batch.state == WorkState.InFlight) save(batch.copy(state = WorkState.OutcomeUnknown))
        }
    }

    suspend fun applyUploadRejection(access: WorkAccess, candidate: UploadCandidate, rejection: UploadRejection,
                                     preflight: Boolean): Boolean = transact(access) {
        val target = candidate.target
        val batch = requireBatch(target.batchId)
        val state = if (preflight) WorkState.Queued else WorkState.InFlight
        if (workspaceId != target.workspaceId || batch.fingerprint != target.fingerprint || batch.state != state)
            return@transact false
        val reason = rejection.reason
        if (reason.isSourceRejection()) {
            val known = candidate.sources.map { it.source.contextId }.toSet()
            if (rejection.contexts.isEmpty() || !known.containsAll(rejection.contexts)) return@transact false
            if (!preflight && affectedOperations(candidate.members, rejection.contexts) != rejection.operations)
                return@transact false
            if (reason == WorkConflict.SourceSecurityRevoked)
                for (context in rejection.contexts) rememberSourceRevoked(context)
        } else if (preflight) return@transact false
        val next = if (reason.isSourceRejection()) WorkState.Quarantined else WorkState.Conflict
        save(batch.copy(state = next, conflict = ConflictEvidence(reason, access.serverDraftVersion)))
        true
    } == true

    private fun affectedOperations(members: List<OperationEnvelope>, sourceIds: Set<String>): Set<String> {
        val affected = mutableSetOf<String>()
        for (entry in members.sortedBy { it.originLocalEditVersion }) {
            if (entry.source.contextId in sourceIds || entry.operation.dependencies.any { it.operationId in affected })
                affected += entry.operation.operationId
        }
        return affected
    }

    private suspend fun WorkTransaction.uploadCandidate(access: WorkAccess, id: String): UploadCandidate {
        checkItemRemovalBarrier(access.task, access.serverDraftVersion)
        val batch = requireBatch(id)
        workCheck(batch.state == WorkState.Queued && catalog().batchIds.none { other ->
            other != id && requireBatch(other).state in setOf(WorkState.InFlight, WorkState.OutcomeUnknown)
        }, WorkProblem.InvalidState)
        workCheck(access.serverDraftVersion == batch.expectedDraftVersion, WorkProblem.ScopeMismatch)
        val members = sealedMembers(access.task, batch)
        checkSampleItemRemovals(itemRemovals(access.task), batch.expectedDraftVersion, members.map { it.operation.attempt }.toSet())
        for (entry in members) {
            val op = entry.operation
            workCheck(op.attempt in access.currentAttempts, WorkProblem.ScopeMismatch)
            workCheck(op == DraftOperationCanonical.normalize(op), WorkProblem.StorageUnavailable)
            for (ref in op.dependencies) {
                val previous = checkedEntry(access.task, ref).envelope
                workCheck(previous.originLocalEditVersion < entry.originLocalEditVersion &&
                    previous.operation.attempt == op.attempt && previous.operation.clientSampleId == op.clientSampleId &&
                    previous.operation.kind != SampleKind.RemoveSample, WorkProblem.DependencyMismatch)
            }
        }
        for (ref in batch.acceptedReferences) {
            val entry = checkedEntry(access.task, ref)
            val receipt = acceptedOperation(entry) ?: throw WorkFailure(WorkProblem.DependencyMismatch)
            verifyOperationReceipt(access.task, entry.envelope, receipt)
            workCheck(entry.envelope.operation.attempt in access.currentAttempts &&
                receipt.draftVersion <= batch.expectedDraftVersion, WorkProblem.DependencyMismatch)
        }
        val sources = members.map { entry ->
            val original = entry.source
            val stored = requireSource(original.contextId, access.task)
            workCheck(stored.source == original && validProof(stored.proof) &&
                original.contextId !in access.securityRevokedSources && !sourceRevoked(original.contextId),
                WorkProblem.SourceUnavailable)
            stored
        }.distinctBy { it.source.contextId }.sortedBy { it.source.contextId }
        val lookup = ReceiptLookup(ReceiptKind.Batch, id)
        return UploadCandidate(ReceiptTarget(lookup, access.task, workspaceId, id, batch.fingerprint),
            batch, members, sources)
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
        val members = sealedMembers(task, batch)
        workCheck(lookup.kind != ReceiptKind.Operation || members.any { it.operation.operationId == lookup.id },
            WorkProblem.StorageUnavailable)
        return ReceiptTarget(lookup, task, workspaceId, id, batch.fingerprint)
    }

    private suspend fun WorkTransaction.sealedMembers(task: WorkTask, batch: BatchRecord): List<OperationEnvelope> {
        val id = batch.batchId
        workCheck(isWorkId(id) && batch.canonicalizationVersion == CANONICAL_VERSION &&
            batch.expectedDraftVersion in 0 until Long.MAX_VALUE && batch.operations.size in 1..50 &&
            batch.acceptedReferences.size <= 100 && (batch.receipt != null) == (batch.state == WorkState.Accepted),
            WorkProblem.StorageUnavailable)
        val ids = batch.operations.map { it.operationId }.toSet()
        workCheck(ids.size == batch.operations.size, WorkProblem.StorageUnavailable)
        val members = batch.operations.map { ref ->
            val entry = checkedEntry(task, ref)
            workCheck(entry.batchId == id && entry.envelope.operation.baseDraftVersion == batch.expectedDraftVersion,
                WorkProblem.StorageUnavailable)
            entry.envelope
        }
        val dependencies = members.flatMap { it.operation.dependencies }.distinct()
        val frontier = dependencies.filter { it.operationId !in ids }.sortedBy { it.operationId }
        workCheck(frontier == batch.acceptedReferences &&
            batch.fingerprint == DraftOperationCanonical.batch(task, id, batch.expectedDraftVersion,
                batch.operations, batch.acceptedReferences), WorkProblem.StorageUnavailable)
        for (dependency in dependencies) {
            val entry = checkedEntry(task, dependency)
            if (dependency.operationId !in ids) {
                workCheck(acceptedOperation(entry) != null, WorkProblem.StorageUnavailable)
            }
        }
        return members
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
        entry.offlineCapture?.let {
            val at = Instant.parse(it.atUtc)
            val until = Instant.parse(it.untilUtc)
            workCheck(at >= Instant.parse(envelope.source.issuedAtUtc) && at < until &&
                until <= Instant.parse(envelope.source.captureUntilUtc), WorkProblem.StorageUnavailable)
        }
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
