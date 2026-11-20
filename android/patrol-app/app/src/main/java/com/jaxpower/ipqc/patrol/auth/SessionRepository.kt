package com.jaxpower.ipqc.patrol.auth

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.NativeAuth
import com.jaxpower.ipqc.patrol.api.NativeLogin
import com.jaxpower.ipqc.patrol.api.NativeRefresh
import com.jaxpower.ipqc.patrol.api.OfflineAccessReceipt
import com.jaxpower.ipqc.patrol.api.PasswordChange
import com.jaxpower.ipqc.patrol.api.PatrolApi
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.api.ensureResponse
import com.jaxpower.ipqc.patrol.api.responseBody
import com.jaxpower.ipqc.patrol.api.responseNoContent
import com.jaxpower.ipqc.patrol.api.utc
import com.jaxpower.ipqc.patrol.api.validGuid
import com.jaxpower.ipqc.patrol.offline.ClockProgress
import com.jaxpower.ipqc.patrol.offline.ClockSample
import com.jaxpower.ipqc.patrol.offline.OfflineCache
import com.jaxpower.ipqc.patrol.offline.OfflineSession
import java.time.Clock
import java.time.Instant
import java.time.temporal.ChronoUnit
import java.util.concurrent.atomic.AtomicLong
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable

@Serializable
data class SessionIdentity(val userId: String, val familyId: String, val environment: String)
data class Operator(val displayName: String, val mustChangePassword: Boolean)

class SessionRepository(
    private val api: PatrolApi,
    private val vault: CredentialVault,
    private val origin: String,
    private val clock: Clock = Clock.systemUTC(),
    private val offline: OfflineCache? = null,
) {
    private val mutex = Mutex()
    private val generation = AtomicLong()
    private var active: Credentials? = null
    // Refresh consumes the live credential before network IO. Retain the owning
    // credential for the offline receipt until verification replaces it or it is revoked.
    private var offlineAuthority: Any? = null

    private class Credentials(
        val generation: Long,
        val operator: Operator,
        val identity: SessionIdentity,
        val access: String,
        val refresh: String,
        val accessExpiry: Instant,
        var familyExpiry: Instant,
    ) {
        val bearer: String get() = "Bearer $access"
        val authorityId = Any()
        var rejected = false // Guarded by the session mutex.
    }

    // Share the lock used to revoke online authority before reading its durable receipt.
    internal suspend fun resumeOffline(expected: SessionIdentity? = null): OfflineSession? = mutex.withLock {
        offline?.resume(expected)
    }

    suspend fun restore(): Operator? {
        if (mutex.withLock { active?.operator?.mustChangePassword == false }) return verifyOnline()
        val stamp = generation.get()
        return mutex.withLock {
            checkGeneration(stamp)
            active?.let { return@withLock it.operator }
            val saved = try {
                storage { vault.read() }
            } catch (failure: PatrolFailure) {
                storage { vault.clear() }
                throw failure
            } ?: return@withLock null
            if (saved.origin != origin || !validGuid(saved.userId) || !validGuid(saved.familyId) ||
                saved.environment.isBlank() || !validToken(saved.refreshToken)) {
                endSession()
                throw PatrolFailure(Issue.REAUTHENTICATE)
            }
            val expiry = try {
                utc(saved.expiresAtUtc)
            } catch (_: PatrolFailure) {
                storage { vault.clear() }
                throw PatrolFailure(Issue.STORAGE)
            }
            rotate(saved, expiry, stamp).operator
        }
    }

    suspend fun login(username: String, password: String): Operator {
        val stamp = generation.incrementAndGet()
        return mutex.withLock {
            checkGeneration(stamp)
            endSession()
            val auth = try {
                responseBody { api.login(NativeLogin(username, password, "Native")) }
            } catch (e: PatrolFailure) {
                throw if (e.issue == Issue.UNAUTHENTICATED) PatrolFailure(Issue.INVALID_LOGIN) else e
            }
            val next = credentials(auth, stamp)
            if (!next.operator.mustChangePassword) verifyAndPersist(next)
            checkGeneration(stamp)
            active = next
            next.operator
        }
    }

    suspend fun changePassword(currentPassword: String, newPassword: String) = mutex.withLock {
        val current = current()
        if (!current.operator.mustChangePassword) throw PatrolFailure(Issue.FORBIDDEN)
        if (!current.accessExpiry.isAfter(clock.instant())) {
            discard()
            throw PatrolFailure(Issue.UNAUTHENTICATED)
        }
        try {
            responseNoContent { api.changePassword(current.bearer, PasswordChange(currentPassword, newPassword)) }
        } catch (e: PatrolFailure) {
            if (e.issue == Issue.BAD_REQUEST) throw PatrolFailure(Issue.CHANGE_REJECTED)
            discard()
            throw if (e.issue == Issue.UNAUTHENTICATED || e.issue == Issue.FORBIDDEN) e
                else PatrolFailure(Issue.REAUTHENTICATE)
        }
        discard()
    }

    suspend fun logout(attemptRemote: Boolean = true): Boolean {
        val stamp = generation.incrementAndGet()
        val prior = withContext(NonCancellable) {
            mutex.withLock {
                checkGeneration(stamp)
                active.also { endSession() }
            }
        } ?: return false
        if (!attemptRemote) return false
        return try {
            responseNoContent { api.logout(prior.bearer, NativeRefresh(prior.refresh)) }
            true
        } catch (_: PatrolFailure) {
            false
        }
    }

    suspend fun verifyOnline(): Operator = authenticatedRead(
        accepted = { auth, verified -> acceptVerification(auth, verified) },
        request = { auth ->
            try {
                verify(auth)
            } catch (e: PatrolFailure) {
                if (e.issue == Issue.RESPONSE || e.issue == Issue.CONFIGURATION) invalidate(auth)
                throw e
            }
        },
    ).operator

    suspend fun <T> read(
        accepted: suspend (SessionIdentity, T) -> Unit = { _, _ -> },
        request: suspend (String, SessionIdentity) -> T,
    ): T = authenticatedRead(
        accepted = { auth, result -> accepted(auth.identity, result) },
        request = { auth -> request(auth.bearer, auth.identity) },
    )

    private suspend fun <T> authenticatedRead(
        accepted: suspend (Credentials, T) -> Unit,
        request: suspend (Credentials) -> T,
    ): T {
        val first = authorize()
        try {
            return admittedRead(first, accepted, request)
        } catch (e: PatrolFailure) {
            if (e.issue == Issue.FORBIDDEN) invalidate(first)
            if (e.issue != Issue.UNAUTHENTICATED) throw e
            reject(first, allowRefresh = true)
            currentCoroutineContext().ensureActive()
        }
        val renewed = authorize(first)
        return try {
            admittedRead(renewed, accepted, request)
        } catch (e: PatrolFailure) {
            if (e.issue == Issue.UNAUTHENTICATED || e.issue == Issue.FORBIDDEN) invalidate(renewed)
            throw e
        }
    }

    private suspend fun <T> admittedRead(
        auth: Credentials,
        accepted: suspend (Credentials, T) -> Unit,
        request: suspend (Credentials) -> T,
    ): T {
        checkGeneration(auth.generation)
        val result = request(auth)
        return mutex.withLock {
            val current = current()
            if (auth.rejected || current.identity != auth.identity || current.generation != auth.generation)
                throw PatrolFailure(Issue.UNAUTHENTICATED)
            accepted(current, result)
            checkGeneration(auth.generation)
            result
        }
    }

    private suspend fun invalidate(rejected: Credentials) = reject(rejected, allowRefresh = false)

    private suspend fun reject(rejected: Credentials, allowRefresh: Boolean) = withContext(NonCancellable) {
        mutex.withLock {
            // Match the exact bearer and generation. An unsuccessful rotation can
            // leave its predecessor's receipt active after consuming the refresh token.
            rejected.rejected = true
            if (active !== rejected && offlineAuthority !== rejected.authorityId) return@withLock
            if (allowRefresh) {
                try {
                    revokeOffline()
                } catch (failure: PatrolFailure) {
                    endSession()
                    throw failure
                }
            } else {
                endSession()
            }
        }
    }

    private fun current(): Credentials {
        val auth = active ?: throw PatrolFailure(Issue.UNAUTHENTICATED)
        checkGeneration(auth.generation)
        return auth
    }

    private suspend fun authorize(rejected: Credentials? = null): Credentials = mutex.withLock {
        val auth = current()
        if (auth.operator.mustChangePassword) throw PatrolFailure(Issue.FORBIDDEN)
        if (rejected != null && (auth.generation != rejected.generation || auth.identity != rejected.identity))
            throw PatrolFailure(Issue.UNAUTHENTICATED)
        if (auth === rejected || auth.rejected || !auth.accessExpiry.isAfter(clock.instant().plusSeconds(15)))
            rotate(stored(auth), auth.familyExpiry, auth.generation)
        else auth
    }

    private suspend fun rotate(saved: StoredRefresh, expiry: Instant, stamp: Long): Credentials {
        // Consume the saved token before transmitting it. An unknown response or process
        // death cannot cause the same potentially rotated token to be sent again.
        discard()
        checkGeneration(stamp)
        if (!expiry.isAfter(clock.instant())) {
            revokeOffline()
            throw PatrolFailure(Issue.UNAUTHENTICATED)
        }
        val next = try {
            val received = responseBody { api.refresh(NativeRefresh(saved.refreshToken)) }
            credentials(received, stamp).also {
                ensureResponse(it.identity == SessionIdentity(saved.userId, saved.familyId, saved.environment) &&
                    it.familyExpiry == expiry && it.refresh != saved.refreshToken && !it.operator.mustChangePassword)
                verifyAndPersist(it)
            }
        } catch (e: PatrolFailure) {
            if (!e.canUseOffline && e.issue != Issue.RATE_LIMITED)
                revokeOffline()
            throw if (e.issue in setOf(Issue.UNAUTHENTICATED, Issue.FORBIDDEN, Issue.STORAGE, Issue.CACHE_STORAGE, Issue.OFFLINE_CLOCK)) e
                else PatrolFailure(Issue.REAUTHENTICATE, canUseOffline = e.canUseOffline)
        }
        checkGeneration(stamp)
        active = next
        return next
    }

    private fun credentials(auth: NativeAuth, stamp: Long): Credentials {
        if (!auth.user.isActive || auth.user.role != "IPQA" ||
            (!auth.user.mustChangePassword && "LinePatrol.Read" !in auth.user.permissions))
            throw PatrolFailure(Issue.FORBIDDEN)

        val family = auth.personalSessionId
        val environment = auth.personalSessionEnvironmentId
        val expiry = auth.personalSessionExpiresAtUtc
        if (family == null || environment == null || expiry == null) throw PatrolFailure(Issue.CONFIGURATION)
        ensureResponse(validGuid(auth.user.id) && validGuid(family) && environment.isNotBlank() &&
            environment == environment.trim() && environment.length <= 200 && auth.user.displayName.isNotBlank())
        val refresh = auth.refreshToken
        ensureResponse(refresh != null && validToken(refresh) && validToken(auth.accessToken))
        val accessExpiry = utc(auth.accessExpiresAtUtc)
        val familyExpiry = utc(expiry)
        ensureResponse(accessExpiry.isAfter(clock.instant()) && familyExpiry.isAfter(clock.instant()))
        return Credentials(stamp, Operator(auth.user.displayName, auth.user.mustChangePassword),
            SessionIdentity(auth.user.id, family, environment), auth.accessToken, requireNotNull(refresh), accessExpiry, familyExpiry)
    }

    private data class Verification(val receipt: OfflineAccessReceipt, val anchor: ClockSample?, val operator: Operator)

    private suspend fun verify(auth: Credentials): Verification {
        val anchor = try {
            offline?.sample()
        } catch (e: PatrolFailure) {
            if (e.issue != Issue.OFFLINE_CLOCK) throw e
            null
        }
        val receipt = responseBody { api.verify(auth.bearer) }
        return Verification(receipt, anchor, auth.operator)
    }

    private suspend fun acceptVerification(auth: Credentials, verification: Verification) {
        try {
            auth.familyExpiry = validateReceipt(verification.receipt, auth)
            val anchor = verification.anchor
            if (anchor == null) {
                revokeOffline()
            } else {
                try {
                    offline?.accept(auth.identity, auth.operator, verification.receipt, anchor)
                    offlineAuthority = auth.authorityId
                } catch (e: PatrolFailure) {
                    if (e.issue != Issue.OFFLINE_CLOCK && e.issue != Issue.OFFLINE_EXPIRED) throw e
                    // A valid current server check still permits live browsing.
                    revokeOffline()
                }
            }
        } catch (failure: PatrolFailure) {
            endSession()
            throw failure
        }
    }

    private suspend fun verifyAndPersist(auth: Credentials) {
        checkGeneration(auth.generation)
        val verification = verify(auth)
        checkGeneration(auth.generation)
        acceptVerification(auth, verification)
        try {
            storage { vault.replace(stored(auth)) }
        } catch (e: PatrolFailure) {
            endSession()
            throw e
        }
    }

    private fun validateReceipt(receipt: OfflineAccessReceipt, auth: Credentials): Instant {
        val verified = utc(receipt.verifiedAtUtc)
        val deadline = utc(receipt.offlineAccessUntilUtc)
        val expiry = utc(receipt.personalSessionExpiresAtUtc)
        ensureResponse(receipt.version == 1 &&
            SessionIdentity(receipt.userId, receipt.personalSessionId, receipt.environmentId) == auth.identity &&
            // The login response can carry .NET ticks before PostgreSQL stores microseconds.
            (expiry == auth.familyExpiry || expiry == auth.familyExpiry.truncatedTo(ChronoUnit.MICROS)) &&
            verified.isBefore(expiry) && deadline == minOf(verified.plusSeconds(ClockProgress.WINDOW_SECONDS), expiry))
        return expiry
    }

    private fun stored(auth: Credentials) = StoredRefresh(origin, auth.refresh, auth.identity.userId, auth.identity.familyId,
        auth.identity.environment, auth.familyExpiry.toString())

    private fun checkGeneration(expected: Long) {
        if (generation.get() != expected) throw PatrolFailure(Issue.UNAUTHENTICATED)
    }

    // Callers hold the session mutex through both durable operations.
    private suspend fun endSession() = withContext(NonCancellable) {
        active = null
        try {
            revokeOffline()
        } finally {
            storage { vault.clear() }
        }
    }

    private suspend fun revokeOffline() = withContext(NonCancellable) {
        offlineAuthority = null
        offline?.revoke()
    }

    private suspend fun discard() {
        active = null
        storage { vault.clear() }
    }

    private fun validToken(value: String) = value.isNotBlank() && value.length <= 8192 && value.all { it in '!'..'~' }

    private suspend fun <T> storage(block: () -> T): T = try {
        withContext(Dispatchers.IO) { block() }
    } catch (e: CancellationException) {
        throw e
    } catch (_: Exception) {
        throw PatrolFailure(Issue.STORAGE)
    }
}
