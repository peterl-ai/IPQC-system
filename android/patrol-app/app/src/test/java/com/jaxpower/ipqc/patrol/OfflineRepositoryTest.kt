package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.PatrolApi
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import com.jaxpower.ipqc.patrol.offline.CacheCipher
import com.jaxpower.ipqc.patrol.offline.CacheRows
import com.jaxpower.ipqc.patrol.offline.ClockSample
import com.jaxpower.ipqc.patrol.offline.OfflineCache
import com.jaxpower.ipqc.patrol.offline.OfflineClock
import com.jaxpower.ipqc.patrol.offline.ReceiptStorage
import com.jaxpower.ipqc.patrol.offline.TaskSnapshots
import com.jaxpower.ipqc.patrol.tasks.PatrolRepository
import java.time.Instant
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.cancel
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.SocketPolicy
import org.junit.Assert.*
import org.junit.Test

class OfflineRepositoryTest {
    @Test fun onlyAuthenticatedVerifyCreatesTheSingleMatchingOfflineAdmission() = runBlocking<Unit> {
        TestServer().use { server ->
            val fixture = OfflineTestFixture(server)
            assertNull(fixture.cache.resume())
            server.admit(receipt = Fixture.receipt().replace("synthetic-env", "wrong-environment"))
            expectIssue(Issue.RESPONSE) { fixture.sessions.login("fixture", "fixture") }
            assertNull(fixture.reopen().resume())
            fixture.login()
            val access = requireNotNull(fixture.reopen().resume())
            assertEquals(OfflineTestFixture.SUBJECT, access.identity)
            assertEquals(Instant.parse("2026-11-22T14:00:00Z"), access.expiresAt)
            val before = server.web.requestCount
            for (other in listOf(access.identity.copy(userId = Fixture.OTHER_USER),
                access.identity.copy(familyId = Fixture.OTHER_FAMILY), access.identity.copy(environment = "other"))) {
                expectIssue(Issue.OFFLINE) { fixture.cache.resume(other) }
                expectIssue(Issue.OFFLINE) { fixture.cache.lines(other) }
            }
            assertEquals(before, server.web.requestCount)
            assertNull(fixture.reopen("https://other.example.invalid/").let {
                expectIssue(Issue.OFFLINE_CLOCK) { it.resume() }
                it.resume()
            })
        }
    }

    @Test fun cacheUsesRealLineScopesAndOnlyLoadsDownloadedDetail() = runBlocking<Unit> {
        TestServer().use { server ->
            val fixture = OfflineTestFixture(server)
            fixture.download(detail = false)
            val identity = OfflineTestFixture.SUBJECT
            assertEquals(listOf(Fixture.line, Fixture.secondLine), fixture.cache.lines(identity))
            val saved = fixture.cache.tasks(identity, Fixture.line, 1)
            assertEquals(listOf(Fixture.selectedTask), saved.items)
            assertTrue(fixture.cache.tasks(identity, Fixture.secondLine, 1).items.isEmpty())
            assertTrue(fixture.cache.tasks(identity, Fixture.line, 2).items.isEmpty())
            expectIssue(Issue.LINE_MISMATCH) { fixture.cache.tasks(identity, Fixture.line.copy(name = "unvalidated"), 1) }
            expectIssue(Issue.CACHE_DETAIL_MISSING) { fixture.cache.detail(identity, Fixture.line, saved.items.single(), 1) }
            server.enqueue(Fixture.detail())
            fixture.patrol.detail(Fixture.line, Fixture.selectedTask)
            assertEquals("Fixture measurement", fixture.cache.detail(identity, Fixture.line, saved.items.single(), 1)
                .items.single().inspectionItem)
            expectIssue(Issue.CACHE_DETAIL_MISSING) {
                fixture.cache.detail(identity, Fixture.secondLine, saved.items.single(), 1)
            }
            assertEquals(5, server.web.requestCount)
        }
    }

    @Test fun rejectsFailedAndInvalidDownloadsAndReplacesOnlyAcceptedPage() = runBlocking<Unit> {
        TestServer().use { server ->
            val fixture = OfflineTestFixture(server)
            fixture.download(detail = true)
            val identity = OfflineTestFixture.SUBJECT
            server.enqueue("{}", 503)
            expectIssue(Issue.UNAVAILABLE) { fixture.patrol.tasks(Fixture.line, 1) }
            server.enqueue(Fixture.page(Fixture.summary(user = Fixture.OTHER_USER)))
            expectIssue(Issue.RESPONSE) { fixture.patrol.tasks(Fixture.line, 1) }
            server.enqueue(Fixture.page(Fixture.summary(lineId = Fixture.SECOND_LINE)))
            expectIssue(Issue.LINE_MISMATCH) { fixture.patrol.tasks(Fixture.line, 1) }
            assertEquals(Fixture.selectedTask, fixture.cache.tasks(identity, Fixture.line, 1).items.single())

            server.enqueue(Fixture.page(Fixture.summary().replace("\"currentRevisionNo\":0", "\"currentRevisionNo\":1")))
            val online = fixture.patrol.tasks(Fixture.line, 1)
            val cached = fixture.cache.tasks(identity, Fixture.line, 1)
            assertEquals(online, cached)
            assertEquals(1, cached.items.single().currentRevisionNo)
            expectIssue(Issue.CACHE_DETAIL_MISSING) {
                fixture.cache.detail(identity, Fixture.line, cached.items.single(), 1)
            }
        }
    }

    @Test fun logoutThenRebootEndsAdmissionAndRetainsOriginalArchiveForLaterReconciliation() = runBlocking<Unit> {
        TestServer().use { server ->
            val fixture = OfflineTestFixture(server)
            fixture.download(detail = true)
            assertFalse(fixture.sessions.logout(attemptRemote = false))
            assertNull(server.vault.saved)
            fixture.rebootAt(180_000)
            val reopened = fixture.reopen()
            assertNull(reopened.resume())
            expectIssue(Issue.OFFLINE) { reopened.lines(OfflineTestFixture.SUBJECT) }
            val other = OfflineTestFixture.SUBJECT.copy(userId = Fixture.OTHER_USER, familyId = Fixture.OTHER_FAMILY)
            server.admit(Fixture.auth(user = other.userId, family = other.familyId), Fixture.receipt(other.userId, other.familyId))
            fixture.sessions.login("another-fixture", "synthetic-only")
            assertEquals(other, fixture.cache.resume()?.identity)
            assertTrue(fixture.cache.lines(other).isEmpty())
            expectIssue(Issue.OFFLINE) { fixture.cache.lines(OfflineTestFixture.SUBJECT) }
            val archived = fixture.snapshots.page(OfflineTestFixture.SUBJECT, Fixture.FIRST_LINE, 1)!!.items.single()
            assertEquals(Fixture.USER, archived.validatedAccess.userId)
            assertEquals(Fixture.FAMILY, archived.validatedAccess.personalSessionId)
            assertEquals(7, server.web.requestCount)
        }
    }

    @Test fun signedInProcessCanResumeAfterRebootAndExpiryLeavesDownloadedDataIntact() = runBlocking<Unit> {
        TestServer().use { server ->
            val fixture = OfflineTestFixture(server)
            fixture.download()
            server.vault.clear()
            fixture.rebootAt(60_000)
            val reopened = fixture.reopen()
            val grant = requireNotNull(reopened.resume())
            assertEquals(Instant.parse(Fixture.NOW).plusSeconds(60), grant.apparentNow)
            assertEquals(Fixture.selectedTask, reopened.tasks(grant.identity, Fixture.line, 1).items.single())
            fixture.rebootAt(72 * 3600 * 1000L)
            expectIssue(Issue.OFFLINE_EXPIRED) { fixture.reopen().resume() }
            assertNull(fixture.reopen().resume())
            assertNotNull(fixture.snapshots.page(grant.identity, Fixture.FIRST_LINE, 1))
            assertEquals(4, server.web.requestCount)
        }
    }

    @Test fun invalidOfflineClockNeverGrantsLocalAccessButValidVerificationPermitsLiveReads() = runBlocking<Unit> {
        TestServer().use { server ->
            val fixture = OfflineTestFixture(server)
            fixture.invalidateOfflineClock()
            fixture.download()
            assertNull(fixture.reopen().resume())
            assertNotNull(fixture.snapshots.page(OfflineTestFixture.SUBJECT, Fixture.FIRST_LINE, 1))
            assertEquals(4, server.web.requestCount)
        }
    }

    @Test fun duplicateVerificationDoesNotRebaseAnAcceptedAnchor() = runBlocking<Unit> {
        TestServer().use { server ->
            val fixture = OfflineTestFixture(server)
            fixture.login()
            val original = fixture.cache.resume()!!
            fixture.advance(120_000)
            server.enqueue(Fixture.receipt())
            fixture.sessions.verifyOnline()
            val observed = fixture.reopen().resume()!!
            assertEquals(original.expiresAt, observed.expiresAt)
            assertEquals(original.apparentNow.plusSeconds(120), observed.apparentNow)
        }
    }

    @Test fun cancelled401RevokesDurableAdmissionBeforeAnyRetry() = cancelledDenial(401)

    @Test fun cancelled403RevokesDurableAdmissionAndReusableCredentials() = cancelledDenial(403)

    private fun cancelledDenial(status: Int) = runBlocking<Unit> {
        TestServer().use { server ->
            val fixture = OfflineTestFixture(server)
            val api = object : PatrolApi by server.api {
                override suspend fun lines(bearer: String) = server.api.lines(bearer).also {
                    check(it.code() == status)
                    // Retrofit has delivered the denial. Cancel the caller before the
                    // repository can acquire either durable-storage lock.
                    currentCoroutineContext().cancel()
                }
            }
            val session = SessionRepository(api, server.vault, server.origin.toString(), server.clock, fixture.cache)
            val patrol = PatrolRepository(api, session, fixture.cache)
            fixture.download(detail = true)
            server.admit(Fixture.auth(refresh = "new-fixture-refresh"))
            assertNotNull(session.restore())
            server.enqueue("{}", status)
            val call = launch {
                if (status == 403) expectIssue(Issue.FORBIDDEN) { patrol.lines() }
                else patrol.lines()
            }
            call.join()
            assertTrue(call.isCancelled)
            assertNull(session.resumeOffline())
            assertNull(fixture.reopen().resume())
            assertNotNull(fixture.snapshots.detail(OfflineTestFixture.SUBJECT, Fixture.selectedTask))
            assertEquals(8, server.web.requestCount)
            if (status == 403) assertNull(server.vault.saved)
        }
    }

    @Test fun delayedDenialRevokesItsReceiptAfterAnUnsuccessfulConcurrentRotation() = runBlocking<Unit> {
        TestServer().use { server ->
            val held = CompletableDeferred<Unit>()
            val release = CompletableDeferred<Unit>()
            val api = object : PatrolApi by server.api {
                override suspend fun lines(bearer: String) = server.api.lines(bearer).also {
                    if (it.code() == 403) {
                        held.complete(Unit)
                        release.await()
                    }
                }
            }
            val fixture = OfflineTestFixture(server, api)
            fixture.download(detail = true)
            server.enqueue("{}", 403)
            val denied = launch { expectIssue(Issue.FORBIDDEN) { fixture.patrol.lines() } }
            try {
                withTimeout(8_000) { held.await() }
                fixture.advance(6 * 60 * 1_000L)
                server.web.enqueue(MockResponse().setSocketPolicy(SocketPolicy.DISCONNECT_AFTER_REQUEST))
                expectIssue(Issue.REAUTHENTICATE) { fixture.patrol.tasks(Fixture.line, 1) }
                assertNull(server.vault.saved)
                assertNotNull(fixture.reopen().resume())
            } finally {
                release.complete(Unit)
                denied.join()
            }
            assertNull(fixture.reopen().resume())
            assertNotNull(fixture.snapshots.detail(OfflineTestFixture.SUBJECT, Fixture.selectedTask))
        }
    }

    @Test fun mismatchedOnlineDetailLeavesTheAcceptedFrozenDefinitionReadableOffline() = runBlocking<Unit> {
        TestServer().use { server ->
            val fixture = OfflineTestFixture(server)
            fixture.download(detail = true)
            val identity = OfflineTestFixture.SUBJECT
            val accepted = fixture.cache.detail(identity, Fixture.line, Fixture.selectedTask, 1)
            for (summary in listOf(
                Fixture.summary().replace("\"currentRevisionNo\":0", "\"currentRevisionNo\":1"),
                Fixture.summary().replace("\"planningSnapshotVersion\":1", "\"planningSnapshotVersion\":2"),
                Fixture.summary().replace("ValidV1", "HistoricalUnadmitted"),
            )) {
                server.enqueue(Fixture.detail(summary))
                expectIssue(Issue.TASK_DETAIL_CHANGED) { fixture.patrol.detail(Fixture.line, Fixture.selectedTask) }
                assertEquals(accepted, fixture.reopen().detail(identity, Fixture.line, Fixture.selectedTask, 1))
            }
        }
    }

    @Test fun unknownRefreshKeepsExistingDeadlineButObservedDenialRevokesEligibility() = runBlocking<Unit> {
        TestServer().use { server ->
            val fixture = OfflineTestFixture(server)
            fixture.download()
            val deadline = fixture.cache.resume()!!.expiresAt
            fixture.advance(6 * 60 * 1_000L)
            server.web.enqueue(MockResponse().setSocketPolicy(SocketPolicy.DISCONNECT_AFTER_REQUEST))
            expectIssue(Issue.REAUTHENTICATE) { fixture.patrol.lines() }
            assertNull(server.vault.saved)
            assertEquals(deadline, fixture.reopen().resume()!!.expiresAt)

            fixture.login()
            server.enqueue("""{"code":"LineConfigurationUnavailable"}""", 403)
            expectIssue(Issue.FORBIDDEN) { fixture.patrol.lines() }
            assertNull(fixture.reopen().resume())
            assertNotNull(fixture.snapshots.page(OfflineTestFixture.SUBJECT, Fixture.FIRST_LINE, 1))
        }
    }
}

internal class OfflineTestFixture(val server: TestServer, api: PatrolApi = server.api) {
    private val epoch = Instant.parse(Fixture.NOW).toEpochMilli()
    @Volatile private var sample = ClockSample(4, 60_000, epoch)
    private val clock = OfflineClock { sample }
    val receipt = MemoryReceipt()
    private val rows = MemoryRows()
    var onFrozenDetailRead: (() -> Unit)?
        get() = rows.onFrozenDetailRead
        set(value) { rows.onFrozenDetailRead = value }
    val snapshots = TaskSnapshots(rows, TestCipher, server.origin.toString())
    val cache = reopen()
    val sessions = SessionRepository(api, server.vault, server.origin.toString(), server.clock, cache)
    val patrol = PatrolRepository(api, sessions, cache)

    fun reopen(origin: String = server.origin.toString()) = OfflineCache(origin, clock, receipt,
        TaskSnapshots(rows, TestCipher, origin))

    fun advance(millis: Long) {
        sample = sample.copy(elapsedMs = sample.elapsedMs + millis, utcMs = sample.utcMs + millis)
        server.clock.current = server.clock.current.plusMillis(millis)
    }

    fun invalidateOfflineClock() { sample = sample.copy(boot = -1) }

    fun rebootAt(millisFromVerification: Long) {
        sample = ClockSample(sample.boot + 1, 4_000, epoch + millisFromVerification)
    }

    suspend fun login() {
        server.clock.current = Instant.parse(Fixture.NOW)
        server.admit()
        sessions.login("fixture", "fixture")
    }

    suspend fun download(detail: Boolean = false) {
        login()
        server.enqueue(Fixture.directory)
        patrol.lines()
        server.enqueue(Fixture.page())
        patrol.tasks(Fixture.line, 1)
        if (detail) {
            server.enqueue(Fixture.detail())
            patrol.detail(Fixture.line, Fixture.selectedTask)
        }
    }

    companion object { val SUBJECT = SessionIdentity(Fixture.USER, Fixture.FAMILY, "synthetic-env") }

    internal class MemoryReceipt : ReceiptStorage {
        @Volatile private var bytes: ByteArray? = null
        @Volatile var beforeRead: (() -> Unit)? = null
        @Volatile var beforeReplace: (() -> Unit)? = null
        @Volatile var beforeRevoke: (() -> Unit)? = null

        override fun read(): ByteArray? {
            beforeRead?.invoke()
            return bytes?.copyOf()
        }

        override fun replace(bytes: ByteArray) {
            beforeReplace?.invoke()
            this.bytes = bytes.copyOf()
        }

        override fun revoke() {
            beforeRevoke?.invoke()
            bytes = null
        }
    }

    private class MemoryRows : CacheRows {
        override suspend fun <T> transaction(block: suspend () -> T): T = block()

        private val entries = mutableMapOf<String, ByteArray>()
        @Volatile var onFrozenDetailRead: (() -> Unit)? = null

        override suspend fun read(slot: String): ByteArray? {
            val value = entries[slot]
            val hook = onFrozenDetailRead
            if (hook != null && value?.decodeToString()?.contains("\"kind\":\"detail\"") == true) hook()
            return value
        }
        override suspend fun count() = entries.size
        override suspend fun replace(slot: String, sealed: ByteArray) { entries[slot] = sealed }
    }

    // JVM policy tests exercise identity and state separately from Android cryptography.
    private object TestCipher : CacheCipher {
        override fun seal(slot: String, plaintext: ByteArray, allowNewKey: Boolean) = plaintext.copyOf()
        override fun open(slot: String, sealed: ByteArray) = sealed.copyOf()
    }
}
