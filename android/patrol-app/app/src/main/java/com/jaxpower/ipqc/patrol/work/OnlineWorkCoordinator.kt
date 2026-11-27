package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.api.GuardedUploadExchange
import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.LineTaskSummary
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.api.ensureResponse
import com.jaxpower.ipqc.patrol.api.problemCode
import com.jaxpower.ipqc.patrol.api.responseBody
import com.jaxpower.ipqc.patrol.api.utc
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import com.jaxpower.ipqc.patrol.tasks.PatrolRepository
import java.time.Instant
import java.time.temporal.ChronoUnit
import java.util.UUID
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.isActive
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext

// Explicit foreground-only entry points. Ordinary Patrol browsing does not call this coordinator.
internal class OnlineWorkCoordinator(
    private val origin: String,
    private val patrol: PatrolRepository,
    private val api: WorkContextApi,
    private val sessions: SessionRepository,
    private val store: SharedWorkStore,
) {
    private val selection = AtomicLong()
    private val selectionCommit = Mutex()
    private val mutex = Mutex()
    private val latestVersion = mutableMapOf<WorkTask, Long>()
    private var active: Grant? = null
    internal val offlineSamples = OfflineSampleCoordinator(store)
    internal val offlineItemRemovals = OfflineItemRemovalCoordinator(store)
    internal val offlineBasicInformation = OfflineBasicInformationCoordinator(store)

    internal class Grant internal constructor(
        val draft: OnlineDraft,
        internal val selectedTask: LineTaskSummary,
        val access: WorkAccess,
        val source: WorkSource?,
        internal val selection: Long,
        private val live: AtomicBoolean,
    ) {
        internal fun invalidate() = live.set(false)
    }

    suspend fun clearSelection() {
        selectionCommit.withLock { selection.incrementAndGet() }
    }

    suspend fun open(task: LineTaskSummary, expected: SessionIdentity? = null): Grant {
        val selected = selectionCommit.withLock { selection.incrementAndGet() }
        return mutex.withLock {
            requireSelection(selected)
            val session = sessions.authorizeWork()
            workCheck(expected == null || session.identity == expected, WorkProblem.SourceUnavailable)
            publish(selected, readDraft(task, session, selected), task, session)
        }
    }

    private suspend fun readDraft(task: LineTaskSummary, session: SessionRepository.WorkSession,
                                  selected: Long): OnlineDraft {
        val draft = try {
            val detail = sessions.requestWork(session, origin) { bearer ->
                requireSelection(selected)
                patrol.onlineDetail(bearer, session.identity, task.lineId, task)
            }
            sessions.requestWork(session, origin) { bearer ->
                requireSelection(selected)
                responseBody { api.sharedDraft(bearer, detail.summary.id) }.validate(detail, session)
            }
        } catch (failure: PatrolFailure) {
            if (failure.issue in closedSampleScopes && isWorkId(task.id) && isWorkId(task.lineId) && task.workflowVersion == 2) {
                selectionCommit.withLock {
                    requireSelection(selected)
                    store.invalidateOfflineSamples(session, WorkTask(session.identity.environment, task.id, task.lineId, 2))
                }
            }
            throw failure
        }
        requireSelection(selected)
        workCheck(draft.version >= (latestVersion[draft.task] ?: 0L), WorkProblem.StaleGrant)
        latestVersion[draft.task] = draft.version
        selectionCommit.withLock {
            requireSelection(selected)
            store.observeOfflineSamples(session, draft.sampleBasis)
        }
        return draft
    }

    suspend fun readBasicInformation(
        grant: Grant, limit: Int = 20, beforeDraftVersion: Long? = null, pinToGrant: Boolean = true,
    ): BasicInformationRead = withGrant(grant) {
        val query = BasicInformationQuery(limit, beforeDraftVersion, grant.draft.version.takeIf { pinToGrant })
        observeBasicInformation(grant, grant.access, query)
    }

    private suspend fun observeBasicInformation(
        grant: Grant, access: WorkAccess, query: BasicInformationQuery,
    ): BasicInformationRead {
        val task = access.task
        val observed = try {
            request(grant) { bearer -> api.readBasicInformation(bearer, task, query) }
        } catch (_: BasicInformationVersionConflict) {
            selectionCommit.withLock {
                grant.access.requireCurrent()
                store.recordBasicInformationVersionConflict(access, requireNotNull(query.expectedDraftVersion))
            }
            throw PatrolFailure(Issue.CONFLICT)
        }
        workCheck(observed.basis.draftVersion >= (latestVersion[task] ?: 0L), WorkProblem.StaleGrant)
        selectionCommit.withLock {
            grant.access.requireCurrent()
            store.observeBasicInformation(access, observed.basis)
            latestVersion[task] = observed.basis.draftVersion
        }
        return observed
    }

    suspend fun basicInformationWorkbench(
        grant: Grant, previousPage: BasicInformationRead? = null,
    ): WorkbenchBasicInformation = withGrant(grant) {
        val access = grant.access
        val before = previousPage?.let {
            workCheck(it.basis.task == access.task && it.basis.draftVersion == access.serverDraftVersion, WorkProblem.StaleGrant)
            it.historyBeforeDraftVersion ?: throw WorkFailure(WorkProblem.InvalidState)
        }
        val observed = observeBasicInformation(grant, access, BasicInformationQuery(beforeDraftVersion = before,
            expectedDraftVersion = access.serverDraftVersion))
        if (previousPage != null) workCheck(observed.basis == previousPage.basis, WorkProblem.ReceiptMismatch)
        val read = if (previousPage == null) observed else observed.copy(history = previousPage.history + observed.history)
        store.basicInformationWorkbench(access, read)
    }

    suspend fun sendWorkbenchBasicInformation(
        grant: Grant, selected: WorkbenchBasicInformation.Row,
    ): BasicInformationUploadResult = withGrant(grant) {
        withUploadLease(grant, requireApplicableAttempts = false) { draft ->
            val access = uploadAccess(grant, draft)
            val observed = observeBasicInformation(grant, access,
                BasicInformationQuery(expectedDraftVersion = draft.version))
            workCheck(selected.canUpload && selected.state == WorkState.Queued &&
                selected.baseDraftVersion == draft.version, WorkProblem.StaleGrant)
            val id = selected.batchId ?: UUID.nameUUIDFromBytes(
                ("ipqc:basic-information:batch:" + selected.operation.operationId).encodeToByteArray()).toString()
            val candidate = selectionCommit.withLock {
                grant.access.requireCurrent()
                store.prepareBasicInformationUpload(access, observed.basis, id, selected.operation)
            }
            uploadPreparedBasicInformation(grant, draft, observed.basis, candidate)
        }
    }

    private suspend fun uploadPreparedBasicInformation(
        grant: Grant, draft: OnlineDraft, basis: BasicInformationBasis, candidate: BasicInformationUploadCandidate,
    ): BasicInformationUploadResult {
        val fence = uploadFence(grant)
        val preflight = request(grant) {
            api.checkUploadSources(it, draft.task.taskId, candidate.preflightInput(), fence)
        }
        val sources = when (preflight) {
            is UploadExchange.Success -> validateUploadSources(listOf(candidate.source), preflight.body)
            is UploadExchange.Rejected ->
                return rejectBasicInformation(grant, uploadAccess(grant, draft), candidate, preflight.rejection, preflight = true)
            is UploadExchange.Unknown -> return BasicInformationUploadResult.NotSent(preflight.issue)
        }
        val access = uploadAccess(grant, draft, sources)
        try {
            val body = selectionCommit.withLock {
                grant.access.requireCurrent()
                store.beginBasicInformationUpload(access, basis, candidate)
            }
            return when (val reply = request(grant) { api.sendBasicInformationBatch(it, draft.task, body, fence) }) {
                is UploadExchange.Success -> acceptBasicInformation(grant, access, candidate, reply.body)
                is UploadExchange.Rejected -> rejectBasicInformation(grant, access, candidate, reply.rejection, preflight = false)
                is UploadExchange.Unknown -> BasicInformationUploadResult.OutcomeUnknown(reply.issue)
            }
        } finally {
            withContext(NonCancellable) {
                try {
                    store.leaveBasicInformationUncertain(candidate)
                } catch (_: WorkFailure) {
                    // Leave the exact InFlight record recoverable if the cleanup write fails.
                }
            }
        }
    }

    private suspend fun rejectBasicInformation(
        grant: Grant, access: WorkAccess, candidate: BasicInformationUploadCandidate, rejection: UploadRejection, preflight: Boolean,
    ): BasicInformationUploadResult {
        val recorded = selectionCommit.withLock {
            grant.access.requireCurrent()
            store.applyBasicInformationRejection(access, candidate, rejection, preflight)
        }
        return if (recorded) BasicInformationUploadResult.Rejected(rejection.reason, submitted = !preflight)
        else if (preflight) BasicInformationUploadResult.NotSent(Issue.RESPONSE)
        else BasicInformationUploadResult.OutcomeUnknown(Issue.RESPONSE)
    }

    private suspend fun acceptBasicInformation(
        grant: Grant, access: WorkAccess, candidate: BasicInformationUploadCandidate, reply: BasicInformationBatchResult,
    ): BasicInformationUploadResult {
        val operation = reply.operation
        val receipt = reply.receipt
        val uploader = access.session.identity
        if (!candidate.original.matches(operation) ||
            (!reply.replayOnly && (operation.actorUserId != uploader.userId || operation.actorPersonalSessionId != uploader.familyId)) ||
            (receipt == null && operation.batchId == candidate.target.batchId))
            return BasicInformationUploadResult.OutcomeUnknown(Issue.RESPONSE)
        val target = if (receipt != null) candidate.target else
            candidate.target.copy(lookup = ReceiptLookup(ReceiptKind.Operation, operation.operation.operationId))
        val result = selectionCommit.withLock {
            grant.access.requireCurrent()
            store.reconcileBasicInformationReceipt(access, target, if (receipt != null)
                BasicInformationReceiptReply.Batch(receipt) else BasicInformationReceiptReply.Operation(operation))
        }
        return when (result) {
            is BasicInformationReceiptRecovery.BatchConfirmed ->
                BasicInformationUploadResult.Accepted(result.receipt, reply.replayOnly)
            is BasicInformationReceiptRecovery.OperationObserved -> BasicInformationUploadResult.NeedsBatchReceipt(result.receipt)
            else -> BasicInformationUploadResult.OutcomeUnknown(Issue.RESPONSE)
        }
    }

    suspend fun recoverBasicInformationReceipt(grant: Grant, lookup: ReceiptLookup): BasicInformationReceiptRecovery =
        withGrant(grant) {
            val target = store.locateBasicInformationReceipt(grant.access, lookup)
                ?: return@withGrant BasicInformationReceiptRecovery.Unresolved(ReceiptUncertainty.NoLocalAttempt)
            val reply = request(grant) { api.readBasicInformationReceipt(target, it) }
            selectionCommit.withLock {
                grant.access.requireCurrent()
                store.reconcileBasicInformationReceipt(grant.access, target, reply)
            }
        }

    suspend fun uploadBatch(grant: Grant, batchId: String): SampleUploadResult = withGrant(grant) {
        withUploadLease(grant) { draft -> uploadPreparedSampleBatch(grant, draft, batchId) }
    }

    suspend fun uploadItemRemovalBatch(grant: Grant, batchId: String): ItemRemovalUploadResult = withGrant(grant) {
        withUploadLease(grant) { draft -> uploadPreparedItemRemoval(grant, draft, batchId) }
    }

    // All draft domains share the process lease across refresh, preflight, POST
    // and settlement. Room fences unresolved attempts across process restarts.
    private suspend fun <T> withUploadLease(
        grant: Grant, requireApplicableAttempts: Boolean = true, upload: suspend (OnlineDraft) -> T,
    ): T {
        val session = grant.access.session
        if (!session.canContribute) throw PatrolFailure(Issue.FORBIDDEN)
        val scope = origin to grant.draft.task
        workCheck(synchronized(uploading) { uploading.add(scope) }, WorkProblem.InvalidState)
        try {
            store.recoverInterrupted(grant.access)
            val draft = readDraft(grant.selectedTask, session, grant.selection)
            workCheck(draft.task == grant.draft.task && draft.validPlanning &&
                draft.availability in uploadableDrafts && (!requireApplicableAttempts || draft.applicableAttempts.isNotEmpty()),
                WorkProblem.ScopeMismatch)
            return upload(draft)
        } finally {
            synchronized(uploading) { uploading.remove(scope) }
        }
    }

    private fun uploadAccess(grant: Grant, draft: OnlineDraft, sources: Set<ValidatedWorkSource> = emptySet()) =
        WorkAccess(grant.access.session, draft.task, draft.version, draft.applicableAttempts, sources, current = {
            grant.access.requireCurrent()
            true
        })

    private suspend fun uploadFence(grant: Grant): GuardedUploadExchange {
        val exchange = currentCoroutineContext()
        return GuardedUploadExchange {
            exchange.isActive && runCatching { grant.access.requireCurrent() }.isSuccess &&
                sessions.maySendContribution(grant.access.session, origin)
        }
    }

    private suspend fun uploadPreparedSampleBatch(grant: Grant, draft: OnlineDraft, batchId: String): SampleUploadResult {
        val access = uploadAccess(grant, draft)
        val candidate = store.inspectUpload(access, batchId)
        val fence = uploadFence(grant)
        val sources = when (val preflight = request(grant) { api.checkUploadSources(it, draft.task.taskId, candidate.preflightInput(), fence) }) {
            is UploadExchange.Success -> validateUploadSources(candidate.sources, preflight.body)
            is UploadExchange.Rejected -> return rejection(grant, access, candidate, preflight.rejection, preflight = true)
            is UploadExchange.Unknown -> return SampleUploadResult.NotSent(preflight.issue)
        }
        val approved = uploadAccess(grant, draft, sources)
        try {
            val body = selectionCommit.withLock {
                grant.access.requireCurrent()
                store.beginUpload(approved, candidate)
            }
            return when (val reply = request(grant) { api.sendSampleBatch(it, draft.task.taskId, body, fence) }) {
                is UploadExchange.Success -> acceptUpload(grant, approved, candidate.target, reply.body)
                is UploadExchange.Rejected -> rejection(grant, approved, candidate, reply.rejection, preflight = false)
                is UploadExchange.Unknown -> SampleUploadResult.OutcomeUnknown(reply.issue)
            }
        } finally {
            withContext(NonCancellable) {
                try {
                    store.leaveUploadUncertain(candidate.target)
                } catch (_: WorkFailure) {
                    // Storage failure preserves InFlight for explicit recovery on the next upload.
                }
            }
        }
    }

    private suspend fun uploadPreparedItemRemoval(grant: Grant, draft: OnlineDraft, batchId: String): ItemRemovalUploadResult {
        val access = uploadAccess(grant, draft)
        val candidate = store.inspectItemRemovalUpload(access, draft.sampleBasis, batchId)
        val fence = uploadFence(grant)
        val preflight = request(grant) {
            api.checkUploadSources(it, draft.task.taskId, candidate.preflightInput(), fence)
        }
        val sources = when (preflight) {
            is UploadExchange.Success -> validateUploadSources(listOf(candidate.source), preflight.body)
            is UploadExchange.Rejected -> return rejectItemRemoval(grant, access, candidate, preflight.rejection, preflight = true)
            is UploadExchange.Unknown -> return ItemRemovalUploadResult.NotSent(preflight.issue)
        }
        val approved = uploadAccess(grant, draft, sources)
        try {
            val body = selectionCommit.withLock {
                grant.access.requireCurrent()
                store.beginItemRemovalUpload(approved, draft.sampleBasis, candidate)
            }
            return when (val reply = request(grant) { api.sendItemRemovalBatch(it, draft.task.taskId, body, fence) }) {
                is UploadExchange.Success -> acceptItemRemoval(grant, approved, draft, candidate, reply.body)
                is UploadExchange.Rejected -> rejectItemRemoval(grant, approved, candidate, reply.rejection, preflight = false)
                is UploadExchange.Unknown -> ItemRemovalUploadResult.OutcomeUnknown(reply.issue)
            }
        } finally {
            withContext(NonCancellable) {
                try {
                    store.leaveItemRemovalUncertain(candidate)
                } catch (_: WorkFailure) {
                    // If a write fails, retain InFlight for recovery on reopen.
                }
            }
        }
    }

    private suspend fun rejectItemRemoval(grant: Grant, access: WorkAccess, candidate: ItemRemovalUploadCandidate,
                                          rejection: UploadRejection, preflight: Boolean): ItemRemovalUploadResult {
        val recorded = selectionCommit.withLock {
            grant.access.requireCurrent()
            store.applyItemRemovalUploadRejection(access, candidate, rejection, preflight)
        }
        return if (recorded) ItemRemovalUploadResult.Rejected(rejection.reason)
        else if (preflight) ItemRemovalUploadResult.NotSent(Issue.RESPONSE)
        else ItemRemovalUploadResult.OutcomeUnknown(Issue.RESPONSE)
    }

    private suspend fun acceptItemRemoval(grant: Grant, access: WorkAccess, draft: OnlineDraft,
                                          candidate: ItemRemovalUploadCandidate,
                                          reply: ItemRemovalBatchResult): ItemRemovalUploadResult {
        val operation = reply.operation
        if (operation.operationId != candidate.entry.removal.command.operationId)
            return ItemRemovalUploadResult.OutcomeUnknown(Issue.RESPONSE)
        val target = candidate.target
        val uploader = access.session.identity
        // Backend counts current attempts that have not been Removed, including UnableToMeasureNG.
        if (!reply.replayOnly && (operation.uploadedByUserId != uploader.userId ||
                operation.uploadedPersonalSessionId != uploader.familyId ||
                operation.remainingApplicableItemCount != draft.sampleBasis.attempts.count {
                    it.serverDisposition != ItemDisposition.Removed
                } - 1)) return ItemRemovalUploadResult.OutcomeUnknown(Issue.RESPONSE)

        val receipt = reply.receipt
        val result = selectionCommit.withLock {
            grant.access.requireCurrent()
            if (receipt != null) store.reconcileItemRemovalReceipt(access, target, ItemRemovalReceiptReply.Batch(receipt))
            else store.reconcileItemRemovalReceipt(access,
                target.copy(lookup = ReceiptLookup(ReceiptKind.Operation, operation.operationId)),
                ItemRemovalReceiptReply.Operation(operation))
        }
        return when (result) {
            is ItemRemovalReceiptRecovery.BatchConfirmed -> ItemRemovalUploadResult.Accepted(result.receipt, reply.replayOnly)
            is ItemRemovalReceiptRecovery.OperationObserved -> ItemRemovalUploadResult.NeedsBatchReceipt(result.receipt)
            else -> ItemRemovalUploadResult.OutcomeUnknown(Issue.RESPONSE)
        }
    }

    private fun validateUploadSources(sources: List<StoredWorkSource>, reply: WorkContextPreflightWire): Set<ValidatedWorkSource> {
        val byId = reply.sources.associateBy { it.workContextId }
        val time = utc(reply.validatedAtUtc)
        ensureResponse(reply.purpose == "SourcePreflightOnly" && !reply.acceptsWork &&
            byId.size == reply.sources.size && byId.keys == sources.map { it.source.contextId }.toSet())
        return sources.map { stored ->
            val original = stored.source
            val matched = byId.getValue(original.contextId)
            ensureResponse(matched.contributorUserId == original.contributorUserId &&
                matchesStoredTimestamp(original.captureUntilUtc, matched.captureUntilUtc) &&
                time >= utc(original.issuedAtUtc).truncatedTo(ChronoUnit.MICROS))
            ValidatedWorkSource(original.contextId, original.contributorUserId, original.captureUntilUtc)
        }.toSet()
    }

    private suspend fun rejection(grant: Grant, access: WorkAccess, candidate: UploadCandidate,
                                  rejection: UploadRejection, preflight: Boolean): SampleUploadResult {
        val recorded = selectionCommit.withLock {
            grant.access.requireCurrent()
            store.applyUploadRejection(access, candidate, rejection, preflight)
        }
        return if (recorded) SampleUploadResult.Rejected(rejection.reason)
        else if (preflight) SampleUploadResult.NotSent(Issue.RESPONSE)
        else SampleUploadResult.OutcomeUnknown(Issue.RESPONSE)
    }

    private suspend fun acceptUpload(grant: Grant, access: WorkAccess, target: ReceiptTarget,
                                     reply: SampleBatchResult): SampleUploadResult {
        val receipt = reply.receipt ?: return SampleUploadResult.NeedsBatchReceipt
        val uploader = access.session.identity
        if (!reply.replayOnly && (receipt.uploadedByUserId != uploader.userId ||
                receipt.uploadedPersonalSessionId != uploader.familyId))
            return SampleUploadResult.OutcomeUnknown(Issue.RESPONSE)
        val confirmed = selectionCommit.withLock {
            grant.access.requireCurrent()
            store.reconcileReceipt(access, target, ReceiptReply.Batch(receipt))
        }
        return if (confirmed is ReceiptRecovery.BatchConfirmed)
            SampleUploadResult.Accepted(confirmed.receipt, reply.replayOnly)
        else SampleUploadResult.OutcomeUnknown(Issue.RESPONSE)
    }

    suspend fun recoverBatchReceipt(grant: Grant, batchId: String): ReceiptRecovery =
        recoverReceipt(grant, ReceiptLookup(ReceiptKind.Batch, batchId))

    suspend fun recoverOperationReceipt(grant: Grant, operationId: String): ReceiptRecovery =
        recoverReceipt(grant, ReceiptLookup(ReceiptKind.Operation, operationId))

    private suspend fun recoverReceipt(grant: Grant, lookup: ReceiptLookup): ReceiptRecovery = try {
        withGrant(grant) {
            val target = store.locateReceipt(grant.access, lookup)
                ?: return@withGrant ReceiptRecovery.Unresolved(ReceiptUncertainty.NoLocalAttempt)
            val reply = request(grant) { bearer -> api.readReceipt(target, bearer) }
            // A switch can supersede an in-flight GET. A switch and the Room
            // transaction serialize only while publishing the result.
            selectionCommit.withLock {
                grant.access.requireCurrent()
                store.reconcileReceipt(grant.access, target, reply)
            }
        }
    } catch (failure: PatrolFailure) {
        when (failure.issue) {
            Issue.UNAUTHENTICATED, Issue.FORBIDDEN -> ReceiptRecovery.AuthenticationFailure(failure.issue)
            else -> throw failure
        }
    }

    suspend fun itemRemovalWorkbench(grant: Grant): WorkbenchRemoval = withGrant(grant) {
        store.itemRemovalWorkbench(grant.access, grant.draft.sampleBasis)
    }

    // Only the explicit foreground action calls this. Preflight grants permission
    // to seal the original source; upload still makes its own current admission checks.
    suspend fun sendWorkbenchItemRemoval(grant: Grant, selected: WorkbenchRemoval.Row): ItemRemovalUploadResult {
        val candidate = itemRemovalWorkbench(grant).rows.singleOrNull { it.operation == selected.operation }
            ?: throw WorkFailure(WorkProblem.ItemRemovalReconciliationRequired)
        workCheck(candidate.attempt == selected.attempt && candidate.currentAttempt && !candidate.operationConfirmed &&
            candidate.state == WorkState.Queued && (selected.batchId == null || candidate.batchId == selected.batchId),
            WorkProblem.ItemRemovalReconciliationRequired)
        candidate.batchId?.let { return uploadItemRemovalBatch(grant, it) }
        val approved = preflight(grant, candidate.operation.operationId)
        val proposedId = UUID.nameUUIDFromBytes(
            ("ipqc:item-removal:batch:" + candidate.operation.operationId).encodeToByteArray()).toString()
        val batch = prepareItemRemovalForReconciliation(approved, proposedId, candidate.operation)
        return uploadItemRemovalBatch(approved, batch.batchId)
    }

    // A different operator first obtains a matched source grant through explicit preflight.
    suspend fun prepareItemRemovalForReconciliation(grant: Grant, batchId: String, operation: OperationReference): ItemRemovalBatch =
        withGrant(grant) {
            workCheck(grant.access.session.canContribute, WorkProblem.SourceUnavailable)
            val draft = readDraft(grant.selectedTask, grant.access.session, grant.selection)
            selectionCommit.withLock {
                grant.access.requireCurrent()
                store.prepareItemRemovalForReconciliation(grant.access, draft.sampleBasis, batchId, operation)
            }
        }

    suspend fun recoverItemRemovalReceipt(grant: Grant, lookup: ReceiptLookup): ItemRemovalReceiptRecovery = try {
        withGrant(grant) {
            val target = store.locateItemRemovalReceipt(grant.access, lookup)
                ?: return@withGrant ItemRemovalReceiptRecovery.Unresolved(ReceiptUncertainty.NoLocalAttempt)
            val reply = request(grant) { bearer -> api.readItemRemovalReceipt(target, bearer) }
            selectionCommit.withLock {
                grant.access.requireCurrent()
                store.reconcileItemRemovalReceipt(grant.access, target, reply)
            }
        }
    } catch (failure: PatrolFailure) {
        when (failure.issue) {
            Issue.UNAUTHENTICATED, Issue.FORBIDDEN -> ItemRemovalReceiptRecovery.AuthenticationFailure(failure.issue)
            else -> throw failure
        }
    }

    suspend fun issueOrReuse(grant: Grant): Grant = withGrant(grant) {
        requireContextScope(grant.draft)
        val snapshot = store.issuanceSnapshot(grant.access)
        val time = captureTime(grant)
        val existing = snapshot.source
        if (existing != null && !snapshot.revoked &&
            existing.source.originPersonalSessionId == grant.access.session.identity.familyId &&
            time < utc(existing.source.captureUntilUtc)) {
            val verified = reuse(grant, existing)
            if (verified != null) return@withGrant verified
        }
        issue(grant, snapshot.state)
    }

    // Arming requires a current authenticated contribution grant and a fresh paired
    // task/detail read. Ordinary offline browse and merely fetching a task never arm it.
    suspend fun armOfflineSamples(grant: Grant) {
        withGrant(grant) { armSamples(grant) }
    }

    suspend fun armWorkbench(grant: Grant): Pair<Long, WorkbenchRemoval> = withGrant(grant) {
        val draft = armSamples(grant)
        draft.version to store.itemRemovalWorkbench(uploadAccess(grant, draft), draft.sampleBasis)
    }

    private suspend fun armSamples(grant: Grant): OnlineDraft {
        val capture = grant.access.captureGrant ?: throw WorkFailure(WorkProblem.SourceUnavailable)
        val source = grant.source ?: throw WorkFailure(WorkProblem.SourceUnavailable)
        workCheck(grant.access.session.canContribute, WorkProblem.SourceUnavailable)
        val draft = readDraft(grant.selectedTask, grant.access.session, grant.selection)
        selectionCommit.withLock {
            requireSelection(grant.selection)
            workCheck(draft.task == grant.draft.task && draft.sampleBasis.editable, WorkProblem.ScopeMismatch)
            store.armOfflineSamples(grant.access, source, capture, draft.sampleBasis)
        }
        return draft
    }

    private suspend fun reuse(grant: Grant, stored: StoredWorkSource): Grant? {
        val source = stored.source
        val wire = request(grant) { bearer ->
            responseBody { api.context(bearer, grant.draft.task.taskId, source.contextId) }
        }
        val observed = wire.validatedSource(grant.draft.task)
        ensureResponse(observed.contextId == source.contextId && observed.contributorUserId == source.contributorUserId &&
            observed.originPersonalSessionId == source.originPersonalSessionId &&
            observed.contractVersion == source.contractVersion && wire.issuedAuthVersion == stored.issuedAuthVersion &&
            stored.issuedRevision == 1L && matchesStoredTimestamp(source.issuedAtUtc, observed.issuedAtUtc) &&
            matchesStoredTimestamp(source.captureUntilUtc, observed.captureUntilUtc) &&
            matchesStoredTimestamp(source.personalSessionExpiresAtUtc, observed.personalSessionExpiresAtUtc))
        if (wire.securityRevokedAtUtc != null) {
            rememberRevocation(grant, source.contextId)
            return null
        }
        val until = minOf(utc(source.captureUntilUtc), utc(observed.captureUntilUtc))
        return if (captureTime(grant) < until) grantWithSource(grant, source, until) else null
    }

    private suspend fun issue(grant: Grant, state: WorkIssuanceState): Grant {
        val task = grant.draft.task
        val session = grant.access.session
        val reservation = store.reserveIssuance(grant.access, state)
        var mayHaveIssued = false
        try {
            val issued = request(grant) { bearer ->
                responseBody {
                    mayHaveIssued = true
                    api.issue(bearer, task.taskId, WorkContextIssueWire(task.lineId, task.workflowVersion, task.environmentId)).also {
                        if (it.code() in definiteNonIssuance) mayHaveIssued = false
                        if (it.isSuccessful) ensureResponse(it.code() == 201)
                    }
                }
            }
            val stored = issued.validate(task, session, captureTime(grant))
            store.finishIssuance(grant.access, reservation, stored)
            return grantWithSource(grant, stored.source, utc(stored.source.captureUntilUtc))
        } finally {
            if (!mayHaveIssued) withContext(NonCancellable) {
                // A cleanup failure leaves its protected reservation in place.
                try {
                    store.releaseUnsentIssuance(task, session.identity.userId, reservation)
                } catch (_: WorkFailure) {
                    // Preserve the original request failure.
                }
            }
        }
    }

    suspend fun preflight(grant: Grant, operationId: String): Grant = withGrant(grant) {
        requireContextScope(grant.draft)
        val task = grant.draft.task
        val stored = store.withSourceForPreflight(grant.access, operationId) { it }
        val source = stored.source
        val input = WorkContextPreflightInputWire(task.lineId, task.workflowVersion, task.environmentId,
            listOf(WorkContextSourceWire(source.contextId, stored.proof, source.contributorUserId)))
        val outcome = request(grant) { bearer ->
            try {
                responseBody {
                    val response = api.preflight(bearer, task.taskId, input)
                    if (response.code() == 409) {
                        val problem = response.problemCode()
                        if (problem?.code == "SourceSecurityRevoked" && problem.sourceContextIds == listOf(source.contextId))
                            throw ConfirmedSourceRevocation()
                        throw PatrolFailure(Issue.CONFLICT)
                    }
                    response
                }
            } catch (_: ConfirmedSourceRevocation) {
                null
            }
        }
        if (outcome == null) {
            rememberRevocation(grant, source.contextId)
            throw WorkFailure(WorkProblem.SourceUnavailable)
        }

        val grantSource = outcome.sources.singleOrNull()
        ensureResponse(outcome.purpose == "SourcePreflightOnly" && !outcome.acceptsWork &&
            utc(outcome.validatedAtUtc) >= utc(source.issuedAtUtc).truncatedTo(ChronoUnit.MICROS) &&
            grantSource != null && grantSource.workContextId == source.contextId &&
            grantSource.contributorUserId == source.contributorUserId &&
            matchesStoredTimestamp(source.captureUntilUtc, grantSource.captureUntilUtc))
        // Carry the matched source and the original whole envelope. There is no acceptance transition.
        publish(grant.selection, grant.draft, grant.selectedTask, grant.access.session,
            validated = setOf(ValidatedWorkSource(source.contextId, source.contributorUserId, source.captureUntilUtc)))
    }

    private suspend fun rememberRevocation(grant: Grant, contextId: String) = withContext(NonCancellable) {
        store.recordSourceRevoked(grant.access, contextId)
    }

    private suspend fun captureTime(grant: Grant): Instant =
        sessions.withWorkCaptureClock(grant.access.session, origin) { now ->
            grant.access.requireCurrent()
            now() ?: throw WorkFailure(WorkProblem.CaptureTimeUnavailable)
        }

    private suspend fun <T> request(grant: Grant, call: suspend (String) -> T): T {
        grant.access.requireCurrent()
        val result = sessions.requestWork(grant.access.session, origin) { bearer ->
            grant.access.requireCurrent()
            call(bearer)
        }
        grant.access.requireCurrent()
        return result
    }

    private suspend fun <T> withGrant(grant: Grant, action: suspend () -> T): T = mutex.withLock {
        workCheck(active === grant, WorkProblem.StaleGrant)
        grant.access.requireCurrent()
        try {
            action()
        } catch (failure: Exception) {
            grant.invalidate()
            throw failure
        }
    }

    private fun grantWithSource(grant: Grant, source: WorkSource, until: Instant) =
        publish(grant.selection, grant.draft, grant.selectedTask, grant.access.session, source, CaptureGrant(source.contextId, until))

    private fun publish(selected: Long, draft: OnlineDraft, selectedTask: LineTaskSummary, session: SessionRepository.WorkSession,
                        source: WorkSource? = null, capture: CaptureGrant? = null,
                        validated: Set<ValidatedWorkSource> = emptySet()): Grant {
        requireSelection(selected)
        val live = AtomicBoolean(true)
        val access = WorkAccess(session, draft.task, draft.version, draft.attempts, validated,
            requiresCaptureGrant = true, captureGrant = capture, current = { live.get() && selection.get() == selected })
        active?.invalidate()
        return Grant(draft, selectedTask, access, source, selected, live).also { active = it }
    }

    private fun requireSelection(selected: Long) = workCheck(selection.get() == selected, WorkProblem.StaleGrant)

    private fun requireContextScope(draft: OnlineDraft) =
        workCheck(draft.validPlanning && draft.availability.allowsContext, WorkProblem.ScopeMismatch)

    private class ConfirmedSourceRevocation : Exception()

    private companion object {
        val uploading = mutableSetOf<Pair<String, WorkTask>>()
        val uploadableDrafts = setOf(DraftAvailability.AlreadyInitialized, DraftAvailability.SharedDraftInitialized)
        val definiteNonIssuance = setOf(400, 401, 403, 404, 409, 413, 429)
        val closedSampleScopes = setOf(Issue.NOT_FOUND, Issue.UNSUPPORTED_TASK, Issue.TASK_DETAIL_CHANGED, Issue.LINE_MISMATCH)
    }
}
