package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.api.GuardedSampleExchange
import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.LineTaskSummary
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.api.ensureResponse
import com.jaxpower.ipqc.patrol.api.problemCode
import com.jaxpower.ipqc.patrol.api.responseBody
import com.jaxpower.ipqc.patrol.api.utc
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import com.jaxpower.ipqc.patrol.tasks.PatrolRepository
import java.time.Instant
import java.time.temporal.ChronoUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong
import kotlinx.coroutines.NonCancellable
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

    suspend fun open(task: LineTaskSummary): Grant {
        val selected = selectionCommit.withLock { selection.incrementAndGet() }
        return mutex.withLock {
            requireSelection(selected)
            val session = sessions.authorizeWork()
            publish(selected, readDraft(task, session, selected), task, session)
        }
    }

    private suspend fun readDraft(task: LineTaskSummary, session: SessionRepository.WorkSession,
                                  selected: Long): OnlineDraft {
        val detail = sessions.requestWork(session, origin) { bearer ->
            requireSelection(selected)
            patrol.onlineDetail(bearer, session.identity, task.lineId, task)
        }
        val draft = sessions.requestWork(session, origin) { bearer ->
            requireSelection(selected)
            responseBody { api.sharedDraft(bearer, detail.summary.id) }.validate(detail, session)
        }
        requireSelection(selected)
        workCheck(draft.version >= (latestVersion[draft.task] ?: 0L), WorkProblem.StaleGrant)
        latestVersion[draft.task] = draft.version
        return draft
    }

    // The task lease covers refresh, preflight, the POST and local settlement.
    // A second coordinator in this process cannot treat a live attempt as abandoned.
    suspend fun uploadBatch(grant: Grant, batchId: String): SampleUploadResult = withGrant(grant) {
        val session = grant.access.session
        if (!session.canContribute) throw PatrolFailure(Issue.FORBIDDEN)
        val scope = origin to grant.draft.task
        workCheck(synchronized(uploading) { uploading.add(scope) }, WorkProblem.InvalidState)
        try {
            store.recoverInterrupted(grant.access)
            val draft = readDraft(grant.selectedTask, session, grant.selection)
            workCheck(draft.task == grant.draft.task && draft.validPlanning &&
                draft.availability in uploadableDrafts && draft.applicableAttempts.isNotEmpty(), WorkProblem.ScopeMismatch)
            uploadPreparedSampleBatch(grant, draft, batchId)
        } finally {
            synchronized(uploading) { uploading.remove(scope) }
        }
    }

    private suspend fun uploadPreparedSampleBatch(grant: Grant, draft: OnlineDraft, batchId: String): SampleUploadResult {
        val session = grant.access.session
        fun uploadAccess(sources: Set<ValidatedWorkSource> = emptySet()) =
            WorkAccess(session, draft.task, draft.version, draft.applicableAttempts, sources, current = {
                grant.access.requireCurrent()
                true
            })
        val access = uploadAccess()
        val candidate = store.inspectUpload(access, batchId)
        val fence = GuardedSampleExchange {
            runCatching { grant.access.requireCurrent() }.isSuccess && sessions.maySendContribution(session, origin)
        }
        val sources = when (val preflight = request(grant) { api.checkSampleSources(it, candidate, fence) }) {
            is SampleExchange.Success -> validateUploadSources(candidate, preflight.body)
            is SampleExchange.Rejected -> return rejection(grant, access, candidate, preflight.rejection, preflight = true)
            is SampleExchange.Unknown -> return SampleUploadResult.NotSent(preflight.issue)
        }
        val approved = uploadAccess(sources)
        try {
            val body = selectionCommit.withLock {
                grant.access.requireCurrent()
                store.beginUpload(approved, candidate)
            }
            return when (val reply = request(grant) { api.sendSampleBatch(it, draft.task.taskId, body, fence) }) {
                is SampleExchange.Success -> acceptUpload(grant, approved, candidate.target, reply.body)
                is SampleExchange.Rejected -> rejection(grant, approved, candidate, reply.rejection, preflight = false)
                is SampleExchange.Unknown -> SampleUploadResult.OutcomeUnknown(reply.issue)
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

    private fun validateUploadSources(candidate: UploadCandidate, reply: WorkContextPreflightWire): Set<ValidatedWorkSource> {
        val byId = reply.sources.associateBy { it.workContextId }
        val time = utc(reply.validatedAtUtc)
        ensureResponse(reply.purpose == "SourcePreflightOnly" && !reply.acceptsWork &&
            byId.size == reply.sources.size && byId.keys == candidate.sources.map { it.source.contextId }.toSet())
        return candidate.sources.map { stored ->
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
    }
}
