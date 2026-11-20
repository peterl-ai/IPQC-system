package com.jaxpower.ipqc.patrol.work

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
    private val mutex = Mutex()
    private val latestVersion = mutableMapOf<WorkTask, Long>()
    private var active: Grant? = null

    internal class Grant internal constructor(
        val draft: OnlineDraft,
        val access: WorkAccess,
        val source: WorkSource?,
        internal val selection: Long,
        private val live: AtomicBoolean,
    ) {
        internal fun invalidate() = live.set(false)
    }

    fun clearSelection() { selection.incrementAndGet() }

    suspend fun open(task: LineTaskSummary): Grant {
        val selected = selection.incrementAndGet()
        return mutex.withLock {
            requireSelection(selected)
            val session = sessions.authorizeWork()
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
            publish(selected, draft, session)
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
        publish(grant.selection, grant.draft, grant.access.session,
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
        publish(grant.selection, grant.draft, grant.access.session, source, CaptureGrant(source.contextId, until))

    private fun publish(selected: Long, draft: OnlineDraft, session: SessionRepository.WorkSession,
                        source: WorkSource? = null, capture: CaptureGrant? = null,
                        validated: Set<ValidatedWorkSource> = emptySet()): Grant {
        requireSelection(selected)
        val live = AtomicBoolean(true)
        val access = WorkAccess(session, draft.task, draft.version, draft.attempts, validated,
            requiresCaptureGrant = true, captureGrant = capture, current = { live.get() && selection.get() == selected })
        active?.invalidate()
        return Grant(draft, access, source, selected, live).also { active = it }
    }

    private fun requireSelection(selected: Long) = workCheck(selection.get() == selected, WorkProblem.StaleGrant)

    private fun requireContextScope(draft: OnlineDraft) =
        workCheck(draft.validPlanning && draft.availability.allowsContext, WorkProblem.ScopeMismatch)

    private class ConfirmedSourceRevocation : Exception()

    private companion object {
        val definiteNonIssuance = setOf(400, 401, 403, 404, 409, 413, 429)
    }
}
