package com.jaxpower.ipqc.patrol.ui

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.LineTaskDetail
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.work.DraftOperationCanonical
import com.jaxpower.ipqc.patrol.work.FrozenSampleType
import com.jaxpower.ipqc.patrol.work.ItemDisposition
import com.jaxpower.ipqc.patrol.work.ItemRemovalCommand
import com.jaxpower.ipqc.patrol.work.ItemRemovalEnvelope
import com.jaxpower.ipqc.patrol.work.ItemRemovalReceiptRecovery
import com.jaxpower.ipqc.patrol.work.ItemRemovalUploadResult
import com.jaxpower.ipqc.patrol.work.ReceiptKind
import com.jaxpower.ipqc.patrol.work.ReceiptLookup
import com.jaxpower.ipqc.patrol.work.ReceiptUncertainty
import com.jaxpower.ipqc.patrol.work.RemovalConfirmation
import com.jaxpower.ipqc.patrol.work.WorkbenchRemoval
import com.jaxpower.ipqc.patrol.work.uncertainItemRemovalStates
import com.jaxpower.ipqc.patrol.work.validRemovalReason
import com.jaxpower.ipqc.patrol.work.LocalSampleBoard
import com.jaxpower.ipqc.patrol.work.LocalSampleDraft
import com.jaxpower.ipqc.patrol.work.LocalSampleValue
import com.jaxpower.ipqc.patrol.work.OfflineSampleEdit
import com.jaxpower.ipqc.patrol.work.OnlineWorkCoordinator
import com.jaxpower.ipqc.patrol.work.SampleInput
import com.jaxpower.ipqc.patrol.work.WorkAttempt
import com.jaxpower.ipqc.patrol.work.WorkFailure
import com.jaxpower.ipqc.patrol.work.WorkOperation
import com.jaxpower.ipqc.patrol.work.WorkProblem
import com.jaxpower.ipqc.patrol.work.WorkState
import com.jaxpower.ipqc.patrol.work.WorkTask
import com.jaxpower.ipqc.patrol.work.workCheck
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

internal class WorkbenchTarget private constructor(val detail: LineTaskDetail, val task: WorkTask, val owner: SessionIdentity) {
    companion object {
        fun from(screen: Screen.Detail): WorkbenchTarget? {
            val detail = (screen.load as? Load.Data)?.value ?: return null
            val summary = detail.summary
            val access = summary.validatedAccess
            if (summary.id != screen.task.id || summary.lineId != screen.line.id || access.taskId != summary.id ||
                access.lineId != summary.lineId || access.workflowVersion != summary.workflowVersion) return null
            val task = try {
                WorkTask(access.environmentId, summary.id, summary.lineId, summary.workflowVersion)
            } catch (_: IllegalArgumentException) {
                return null
            }
            return WorkbenchTarget(detail, task, SessionIdentity(access.userId, access.personalSessionId, access.environmentId))
        }
    }
}

internal data class WorkbenchUi(
    val target: WorkbenchTarget? = null,
    val draft: LocalSampleDraft? = null,
    val hasLocalAccess: Boolean = false,
    val busy: Boolean = false,
    val blocked: WorkProblem? = null,
    val error: String? = null,
    val saved: String? = null,
    val lastSavedId: String? = null,
    val retryPending: Boolean = false,
    val selectedItemId: String? = null,
    val onlineRemoval: WorkbenchRemoval? = null,
    val removalRefreshRequired: Boolean = false,
    val removalSavePending: Boolean = false,
    val uploadingRemoval: Boolean = false,
) {
    val blocksNavigation get() = busy || retryPending || removalSavePending
    val canEdit get() = hasLocalAccess && draft != null && !blocksNavigation && blocked == null

    fun canEditSamples(attempt: WorkAttempt): Boolean =
        canEdit && draft?.itemRemoval?.sampleBlocks?.contains(attempt) != true

    fun canStartRemoval(attempt: WorkAttempt): Boolean {
        val local = draft?.itemRemoval ?: return false
        return canEdit && attempt.taskItemId == selectedItemId && !local.blocksNewRemoval &&
            !local.requiresRefresh && local.items.any { it.attempt == attempt && it.server == ItemDisposition.Applicable }
    }

    fun removalView(online: Boolean): WorkbenchRemoval? =
        onlineRemoval.takeIf { online } ?: draft?.itemRemoval
}

internal class SampleWorkbenchModel(private val scope: CoroutineScope, private val online: OnlineWorkCoordinator) {
    private val samples = online.offlineSamples
    private val removals = online.offlineItemRemovals
    private val mutable = MutableStateFlow(WorkbenchUi())
    val ui = mutable.asStateFlow()
    private var action: Job? = null
    private var observer: Job? = null
    private var pending: OfflineSampleEdit? = null
    private var pendingRemoval: ItemRemovalCommand? = null

    fun open(target: WorkbenchTarget, canReachServer: Boolean) {
        close()
        mutable.value = WorkbenchUi(target, selectedItemId = target.detail.items.minByOrNull { it.sequenceNo }?.id)
        reload(target, canReachServer)
        observer = scope.launch {
            while (ui.value.target === target) {
                delay(1_000)
                observe(target)
            }
        }
    }

    fun close() {
        observer?.cancel()
        action?.cancel()
        observer = null
        action = null
        pending = null
        pendingRemoval = null
        mutable.value = WorkbenchUi()
    }

    fun selectItem(target: WorkbenchTarget, id: String) {
        if (ui.value.target !== target || ui.value.blocksNavigation || target.detail.items.none { it.id == id }) return
        update(target) { it.copy(selectedItemId = id) }
    }

    fun reload(target: WorkbenchTarget, canReachServer: Boolean) = run(target) {
        if (canReachServer) {
            invalidateRemovalProjection(target)
        } else {
            update(target) { it.copy(onlineRemoval = null,
                removalRefreshRequired = it.removalRefreshRequired || it.onlineRemoval != null) }
        }
        refresh(target, canReachServer)
    }

    // The previous online or local removal row must not survive an action that
    // can change its durable state or replace its server observation.
    private fun invalidateRemovalProjection(target: WorkbenchTarget) {
        update(target) { it.copy(onlineRemoval = null, draft = it.draft?.copy(itemRemoval = null),
            removalRefreshRequired = true) }
    }

    private suspend fun refresh(target: WorkbenchTarget, canReachServer: Boolean) {
        var rateLimit: PatrolFailure? = null
        val grant = if (canReachServer) {
            try {
                open(target)
            } catch (failure: PatrolFailure) {
                if (failure.issue == Issue.RATE_LIMITED) rateLimit = failure
                else if (!failure.canUseOffline) throw failure
                null
            }
        } else null
        val remoteBasis = grant?.draft?.sampleBasis
        val shared = grant?.let { online.itemRemovalWorkbench(it) }
        val local = try {
            samples.draft(target.task, target.owner)
        } catch (failure: WorkFailure) {
            if (failure.problem != WorkProblem.SourceUnavailable) throw failure
            null
        } catch (failure: PatrolFailure) {
            if (remoteBasis == null) throw failure
            null
        }
        if (local != null) {
            showProtected(target, local, shared, remoteBasis?.draftVersion)
        } else if (remoteBasis != null) {
            val board = LocalSampleBoard(remoteBasis, emptyMap())
            update(target) { it.copy(draft = LocalSampleDraft(remoteBasis.draftVersion, 0, remoteBasis.samples,
                board.values(), emptyList(), board.progress()), hasLocalAccess = false, blocked = WorkProblem.SourceUnavailable,
                onlineRemoval = shared, removalRefreshRequired = false) }
        } else if (rateLimit == null) {
            update(target) { it.copy(draft = null, hasLocalAccess = false, blocked = WorkProblem.SourceUnavailable) }
        }
        rateLimit?.let { throw it }
    }

    fun arm(target: WorkbenchTarget) = run(target) {
        invalidateRemovalProjection(target)
        val grant = open(target)
        workCheck(grant.access.session.canContribute, WorkProblem.SourceUnavailable)
        workCheck(grant.draft.sampleBasis.editable, WorkProblem.ScopeMismatch)
        val (version, shared) = online.armWorkbench(online.issueOrReuse(grant))
        showProtected(target, samples.draft(target.task, target.owner), shared, version)
    }

    private suspend fun open(target: WorkbenchTarget): OnlineWorkCoordinator.Grant =
        online.open(target.detail.summary, target.owner).also {
            workCheck(it.draft.task == target.task, WorkProblem.ScopeMismatch)
        }

    fun save(target: WorkbenchTarget, attempt: WorkAttempt, input: SampleInput, previous: LocalSampleValue?) {
        val state = ui.value
        if (state.target !== target || !state.canEditSamples(attempt)) return
        val requirement = state.draft?.progress?.singleOrNull { it.attempt == attempt } ?: return
        val type = when (input) {
            is SampleInput.Qualitative -> FrozenSampleType.Qualitative
            is SampleInput.Quantitative -> FrozenSampleType.Quantitative
        }
        if (requirement.type != type) return
        try {
            DraftOperationCanonical.normalizeSample(input.payload())
        } catch (_: IllegalArgumentException) {
            update(target) { it.copy(error = "Enter a valid decimal and an optional barcode of up to 200 characters.", saved = null) }
            return
        }
        val command = if (previous == null) samples.newCapture(attempt, input) else {
            val exact = predecessor(state, attempt, previous) ?: return
            OfflineSampleEdit.Correct(previous.key, input, exact)
        }
        submit(target, command)
    }

    fun remove(target: WorkbenchTarget, attempt: WorkAttempt, previous: LocalSampleValue) {
        val state = ui.value
        if (state.target !== target || !state.canEditSamples(attempt)) return
        val exact = predecessor(state, attempt, previous) ?: return
        submit(target, OfflineSampleEdit.Remove(previous.key, exact))
    }

    fun saveItemRemoval(target: WorkbenchTarget, attempt: WorkAttempt, confirmed: Boolean,
                        source: RemovalConfirmation, reason: String?) {
        val state = ui.value
        if (state.target !== target || !state.canStartRemoval(attempt) || !confirmed || !validRemovalReason(reason)) return
        run(target) {
            val exact = removals.newRemoval(target.task, target.owner, attempt, confirmed, source, reason)
            pendingRemoval = exact
            saveExactRemoval(target, exact)
        }
    }

    fun retryItemRemovalSave(target: WorkbenchTarget) = run(target) {
        val exact = pendingRemoval ?: return@run
        saveExactRemoval(target, exact)
    }

    fun checkItemRemovalSave(target: WorkbenchTarget) = run(target) {
        val exact = pendingRemoval ?: return@run
        val stored = removals.draft(target.task, target.owner).history
            .singleOrNull { it.removal.command.operationId == exact.operationId }?.removal
        if (stored == null) {
            update(target) { it.copy(error = "No local save found. Retry uses the same removal.") }
        } else {
            workCheck(stored.command == exact, WorkProblem.OperationIdentityConflict)
            savedRemoval(target, stored)
        }
    }

    private suspend fun saveExactRemoval(target: WorkbenchTarget, exact: ItemRemovalCommand) {
        val saved = try {
            removals.save(exact)
        } catch (failure: WorkFailure) {
            if (ui.value.target === target && failure.problem != WorkProblem.StorageUnavailable) pendingRemoval = null
            throw failure
        }
        savedRemoval(target, saved)
    }

    private suspend fun savedRemoval(target: WorkbenchTarget, envelope: ItemRemovalEnvelope) {
        if (ui.value.target !== target) return
        pendingRemoval = null
        update(target) { it.copy(saved = "Removal saved locally.", lastSavedId = envelope.command.operationId,
            removalSavePending = false, onlineRemoval = null) }
        try {
            showProtected(target, samples.draft(target.task, target.owner))
        } catch (failure: Exception) {
            if (failure is CancellationException) throw failure
            update(target) { it.copy(draft = null, hasLocalAccess = false, blocked = WorkProblem.SourceUnavailable,
                error = "Local removal saved. Reload to see the current draft.") }
        }
    }

    fun uploadItemRemoval(target: WorkbenchTarget, row: WorkbenchRemoval.Row) {
        val state = ui.value
        val view = state.removalView(online = true) ?: return
        if (state.target !== target || state.blocksNavigation || state.removalRefreshRequired ||
            state.selectedItemId != row.attempt.taskItemId || view.rows.none { it === row } ||
            row.state != WorkState.Queued || !row.currentAttempt || row.operationConfirmed || view.requiresRefresh) return
        run(target, uploadingRemoval = true) {
            invalidateRemovalProjection(target)
            val grant = open(target)
            val reply = try {
                online.sendWorkbenchItemRemoval(grant, row)
            } catch (failure: WorkFailure) {
                refresh(target, true)
                if (failure.problem != WorkProblem.SourceUnavailable) throw failure
                update(target) { it.copy(error = "The saved removal source is unavailable or no longer eligible.") }
                return@run
            }
            val message = when (reply) {
                is ItemRemovalUploadResult.Accepted -> "Server confirmed the item removal."
                is ItemRemovalUploadResult.NeedsBatchReceipt -> "Item removal verified. Check the batch receipt."
                is ItemRemovalUploadResult.OutcomeUnknown -> "Upload outcome unknown. Check the receipt."
                is ItemRemovalUploadResult.Rejected -> "The removal was rejected. The original is retained."
                is ItemRemovalUploadResult.NotSent -> "The removal was not sent. ${reply.issue.message}"
            }
            update(target) { it.copy(saved = message) }
            refresh(target, true)
        }
    }

    fun checkItemRemovalReceipt(target: WorkbenchTarget, row: WorkbenchRemoval.Row) {
        val state = ui.value
        val batchId = row.batchId ?: return
        if (state.target !== target || state.blocksNavigation || state.removalRefreshRequired ||
            state.selectedItemId != row.attempt.taskItemId ||
            state.removalView(online = true)?.rows?.any { it === row } != true || row.state !in uncertainItemRemovalStates) return
        run(target) {
            invalidateRemovalProjection(target)
            val grant = open(target)
            val batch = online.recoverItemRemovalReceipt(grant, ReceiptLookup(ReceiptKind.Batch, batchId))
            val reply = if (batch is ItemRemovalReceiptRecovery.Unresolved && batch.reason == ReceiptUncertainty.NotObserved)
                online.recoverItemRemovalReceipt(grant, ReceiptLookup(ReceiptKind.Operation, row.operation.operationId))
            else batch
            if (reply is ItemRemovalReceiptRecovery.AuthenticationFailure) throw PatrolFailure(reply.issue)
            val message = when (reply) {
                is ItemRemovalReceiptRecovery.BatchConfirmed -> "Server confirmed the item removal and its batch."
                is ItemRemovalReceiptRecovery.OperationObserved -> "Item removal verified. The batch receipt is still required."
                is ItemRemovalReceiptRecovery.Unresolved -> "Receipt not found yet. The upload outcome remains unknown."
                is ItemRemovalReceiptRecovery.Conflict -> "The receipt did not match. The original upload remains unresolved."
                is ItemRemovalReceiptRecovery.TransientFailure -> "Receipt check unavailable. The upload remains unresolved."
                is ItemRemovalReceiptRecovery.AuthenticationFailure -> error("Already handled")
            }
            update(target) { it.copy(saved = message) }
            refresh(target, true)
        }
    }

    private fun predecessor(state: WorkbenchUi, attempt: WorkAttempt, previous: LocalSampleValue) =
        state.draft?.effective?.singleOrNull {
            it.key == previous.key && it.key.attempt == attempt && !it.removed &&
                it.editablePredecessor == previous.editablePredecessor
        }?.editablePredecessor

    fun retry(target: WorkbenchTarget) {
        val command = pending ?: return
        submit(target, command)
    }

    fun checkSave(target: WorkbenchTarget) = run(target) {
        val command = pending ?: return@run
        val result = samples.savedOperation(target.task, command.operationId, target.owner)
        if (result == null) {
            update(target) { it.copy(error = "No local save found for this operation. Retry uses the same operation.") }
        } else {
            val original = result.envelope.operation
            if (command.operation(result.envelope.source.contextId, original.baseDraftVersion) != original) {
                pending = null
                throw WorkFailure(WorkProblem.OperationIdentityConflict)
            }
            saved(target, result)
        }
    }

    private fun submit(target: WorkbenchTarget, command: OfflineSampleEdit) = run(target) {
        pending = command
        val operation = try {
            samples.save(target.task, command, target.owner)
        } catch (failure: WorkFailure) {
            if (ui.value.target === target && failure.problem != WorkProblem.StorageUnavailable) pending = null
            throw failure
        }
        saved(target, operation)
    }

    private suspend fun saved(target: WorkbenchTarget, operation: WorkOperation) {
        if (ui.value.target !== target) return
        pending = null
        update(target) { it.copy(saved = "Saved locally · " + operation.state.label(),
            lastSavedId = operation.envelope.operation.operationId, retryPending = false) }
        try {
            showProtected(target, samples.draft(target.task, target.owner))
        } catch (failure: Exception) {
            if (failure is CancellationException) throw failure
            update(target) { it.copy(draft = null, hasLocalAccess = false, blocked = WorkProblem.SourceUnavailable,
                error = "Local save confirmed. Reload to see the latest draft.") }
        }
    }

    private suspend fun observe(target: WorkbenchTarget) {
        val before = ui.value
        if (before.target !== target || before.busy || !before.hasLocalAccess) return
        try {
            val problem = samples.captureProblem(target.task, target.owner)
            if (before !== ui.value) return
            if (problem != before.blocked && (problem != null || before.blocked != WorkProblem.StaleGrant)) {
                val draft = samples.draft(target.task, target.owner)
                if (before === ui.value) showProtected(target, draft)
            }
        } catch (failure: Exception) {
            if (failure is CancellationException) throw failure
            if (before === ui.value) recordFailure(target, failure)
        }
    }

    private fun showProtected(target: WorkbenchTarget, draft: LocalSampleDraft,
                              shared: WorkbenchRemoval? = null, sharedVersion: Long? = null) {
        val verified = shared?.takeIf { sharedVersion == draft.observedServerDraftVersion &&
            (draft.itemRemoval?.requiresRefresh != true || it.requiresRefresh) }
        update(target) {
            val superseded = it.onlineRemoval != null && (it.draft?.observedServerDraftVersion != draft.observedServerDraftVersion ||
                it.draft?.itemRemoval != draft.itemRemoval || draft.itemRemoval?.requiresRefresh == true)
            it.copy(draft = draft, hasLocalAccess = true, blocked = draft.captureProblem,
                onlineRemoval = verified ?: it.onlineRemoval.takeUnless { superseded },
                removalRefreshRequired = verified == null && (it.removalRefreshRequired || superseded))
        }
    }

    private fun run(target: WorkbenchTarget, uploadingRemoval: Boolean = false, block: suspend () -> Unit) {
        if (ui.value.target !== target || ui.value.busy) return
        update(target) { it.copy(busy = true, error = null, saved = null, uploadingRemoval = uploadingRemoval) }
        action = scope.launch {
            try {
                block()
            } catch (failure: Exception) {
                if (failure is CancellationException) throw failure
                recordFailure(target, failure)
            } finally {
                update(target) { it.copy(busy = false, retryPending = pending != null,
                    removalSavePending = pendingRemoval != null, uploadingRemoval = false) }
            }
        }
    }

    private fun recordFailure(target: WorkbenchTarget, failure: Exception) {
        if (ui.value.target !== target) return
        val issue = (failure as? PatrolFailure)?.issue
        val problem = (failure as? WorkFailure)?.problem
        val lost = issue in closedScopeIssues || problem == WorkProblem.SourceUnavailable
        if (lost) {
            pending = null
            pendingRemoval = null
        }
        val message = when {
            pending != null -> "Local save could not be confirmed. Check the saved operation or retry it exactly."
            pendingRemoval != null -> "Removal save could not be confirmed. Check the local save or retry the same removal."
            else -> issue?.message ?: problem?.label() ?: "The local draft is unavailable."
        }
        update(target) { it.copy(
            error = message, retryPending = pending != null, removalSavePending = pendingRemoval != null,
            draft = it.draft.takeUnless { lost }, onlineRemoval = it.onlineRemoval.takeUnless { lost },
            hasLocalAccess = it.hasLocalAccess && !lost, saved = it.saved.takeUnless { lost },
            blocked = when {
                lost -> WorkProblem.SourceUnavailable
                problem in captureBlocks -> problem
                else -> it.blocked
            },
        ) }
    }

    private companion object {
        val captureBlocks = setOf(WorkProblem.CaptureWindowClosed, WorkProblem.CaptureTimeUnavailable,
            WorkProblem.ScopeMismatch, WorkProblem.StaleGrant, WorkProblem.StorageUnavailable)
        val closedScopeIssues = setOf(Issue.UNAUTHENTICATED, Issue.FORBIDDEN, Issue.OFFLINE_EXPIRED, Issue.OFFLINE_CLOCK,
            Issue.CACHE_STORAGE, Issue.LINE_MISMATCH, Issue.NOT_FOUND, Issue.TASK_DETAIL_CHANGED, Issue.UNSUPPORTED_TASK, Issue.RESPONSE)
    }

    private fun update(target: WorkbenchTarget, transform: (WorkbenchUi) -> WorkbenchUi) {
        val state = ui.value
        if (state.target === target) mutable.value = transform(state)
    }
}

internal fun WorkState.label(): String = when (this) {
    WorkState.Queued -> "Pending sync"
    WorkState.InFlight -> "In flight"
    WorkState.OutcomeUnknown -> "Outcome unknown"
    WorkState.Accepted -> "Server accepted"
    WorkState.Conflict -> "Conflict"
    WorkState.Quarantined -> "Quarantined"
}

internal fun WorkProblem.label(): String = when (this) {
    WorkProblem.SourceUnavailable -> "Local capture requires an eligible online activation."
    WorkProblem.CaptureWindowClosed -> "The local capture window expired. Activate capture online to continue."
    WorkProblem.CaptureTimeUnavailable -> "The protected capture time is unavailable. Verify online."
    WorkProblem.ScopeMismatch -> "This inspection attempt is unavailable for local editing. Reload the task."
    WorkProblem.StaleGrant -> "Reload the shared draft online before editing."
    WorkProblem.SampleInputMismatch -> "The sample does not match the frozen inspection requirement."
    WorkProblem.SampleLimit -> "This attempt already has 50 applicable samples."
    WorkProblem.ItemRemovalInputMismatch -> "Confirm the removal and enter a reason of at most 500 characters without control characters."
    WorkProblem.ItemDispositionConflict -> "Resolve the inspection item disposition before editing its samples."
    WorkProblem.ItemRemovalReconciliationRequired -> "Reconcile the pending item removal online before removing another item."
    WorkProblem.DependencyMismatch -> "The sample changed. Reload before correcting or removing it."
    WorkProblem.IssuanceUncertain -> "The server could not confirm activation. Local capture stays locked."
    WorkProblem.OperationIdentityConflict -> "This operation could not be matched to the saved sample."
    else -> "The protected local draft is unavailable."
}
