package com.jaxpower.ipqc.patrol.ui

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.LineTaskDetail
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.work.DraftOperationCanonical
import com.jaxpower.ipqc.patrol.work.FrozenSampleType
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
) {
    val canEdit get() = hasLocalAccess && draft != null && !busy && !retryPending && blocked == null
}

internal class SampleWorkbenchModel(private val scope: CoroutineScope, private val online: OnlineWorkCoordinator) {
    private val samples = online.offlineSamples
    private val mutable = MutableStateFlow(WorkbenchUi())
    val ui = mutable.asStateFlow()
    private var action: Job? = null
    private var observer: Job? = null
    private var pending: OfflineSampleEdit? = null

    fun open(target: WorkbenchTarget, canReachServer: Boolean) {
        close()
        mutable.value = WorkbenchUi(target)
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
        mutable.value = WorkbenchUi()
    }

    fun reload(target: WorkbenchTarget, canReachServer: Boolean) = run(target) {
        val remoteBasis = if (canReachServer) {
            try {
                val grant = online.open(target.detail.summary, target.owner)
                workCheck(grant.draft.task == target.task, WorkProblem.ScopeMismatch)
                grant.draft.sampleBasis
            } catch (failure: PatrolFailure) {
                if (!failure.canUseOffline) throw failure
                null
            }
        } else null
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
            showProtected(target, local)
        } else if (remoteBasis != null) {
            val board = LocalSampleBoard(remoteBasis, emptyMap())
            update(target) { it.copy(draft = LocalSampleDraft(remoteBasis.draftVersion, 0, remoteBasis.samples,
                board.values(), emptyList(), board.progress()), hasLocalAccess = false, blocked = WorkProblem.SourceUnavailable) }
        } else {
            update(target) { it.copy(draft = null, hasLocalAccess = false, blocked = WorkProblem.SourceUnavailable) }
        }
    }

    fun arm(target: WorkbenchTarget) = run(target) {
        val grant = online.open(target.detail.summary, target.owner)
        workCheck(grant.draft.task == target.task, WorkProblem.ScopeMismatch)
        workCheck(grant.access.session.canContribute, WorkProblem.SourceUnavailable)
        workCheck(grant.draft.sampleBasis.editable, WorkProblem.ScopeMismatch)
        online.armOfflineSamples(online.issueOrReuse(grant))
        showProtected(target, samples.draft(target.task, target.owner))
    }

    fun save(target: WorkbenchTarget, attempt: WorkAttempt, input: SampleInput, previous: LocalSampleValue?) {
        val state = ui.value
        if (state.target !== target || !state.canEdit) return
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
        if (state.target !== target || !state.canEdit) return
        val exact = predecessor(state, attempt, previous) ?: return
        submit(target, OfflineSampleEdit.Remove(previous.key, exact))
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

    private fun showProtected(target: WorkbenchTarget, draft: LocalSampleDraft) {
        update(target) { it.copy(draft = draft, hasLocalAccess = true, blocked = draft.captureProblem) }
    }

    private fun run(target: WorkbenchTarget, block: suspend () -> Unit) {
        if (ui.value.target !== target || ui.value.busy) return
        update(target) { it.copy(busy = true, error = null, saved = null) }
        action = scope.launch {
            try {
                block()
            } catch (failure: Exception) {
                if (failure is CancellationException) throw failure
                recordFailure(target, failure)
            } finally {
                update(target) { it.copy(busy = false, retryPending = pending != null) }
            }
        }
    }

    private fun recordFailure(target: WorkbenchTarget, failure: Exception) {
        if (ui.value.target !== target) return
        val issue = (failure as? PatrolFailure)?.issue
        val problem = (failure as? WorkFailure)?.problem
        val lost = issue in closedScopeIssues || problem == WorkProblem.SourceUnavailable
        if (lost) pending = null
        val message = if (pending != null) "Local save could not be confirmed. Check the saved operation or retry it exactly."
            else issue?.message ?: problem?.label() ?: "The local draft is unavailable."
        update(target) { it.copy(
            error = message, retryPending = pending != null, draft = it.draft.takeUnless { lost },
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
    WorkProblem.ScopeMismatch, WorkProblem.StaleGrant -> "This inspection attempt is unavailable for local editing. Reload the task."
    WorkProblem.SampleInputMismatch -> "The sample does not match the frozen inspection requirement."
    WorkProblem.SampleLimit -> "This attempt already has 50 applicable samples."
    WorkProblem.DependencyMismatch -> "The sample changed. Reload before correcting or removing it."
    WorkProblem.IssuanceUncertain -> "The server could not confirm activation. Local capture stays locked."
    WorkProblem.OperationIdentityConflict -> "This operation could not be matched to the saved sample."
    else -> "The protected local draft is unavailable."
}
