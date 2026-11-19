package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.Issue
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
import kotlinx.coroutines.runBlocking
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

internal class OfflineTestFixture(val server: TestServer) {
    private val epoch = Instant.parse(Fixture.NOW).toEpochMilli()
    @Volatile private var sample = ClockSample(4, 60_000, epoch)
    private val clock = OfflineClock { sample }
    private val receipt = MemoryReceipt()
    private val rows = MemoryRows()
    val snapshots = TaskSnapshots(rows, TestCipher, server.origin.toString())
    val cache = reopen()
    val sessions = SessionRepository(server.api, server.vault, server.origin.toString(), server.clock, cache)
    val patrol = PatrolRepository(server.api, sessions, cache)

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

    private class MemoryReceipt : ReceiptStorage {
        private var bytes: ByteArray? = null
        override fun read() = bytes?.copyOf()
        override fun replace(bytes: ByteArray) { this.bytes = bytes.copyOf() }
        override fun revoke() { bytes = null }
    }

    private class MemoryRows : CacheRows {
        private val entries = mutableMapOf<String, ByteArray>()
        override suspend fun read(slot: String) = entries[slot]
        override suspend fun count() = entries.size
        override suspend fun replace(slot: String, sealed: ByteArray) { entries[slot] = sealed }
    }

    // JVM policy tests exercise identity and state separately from Android cryptography.
    private object TestCipher : CacheCipher {
        override fun seal(slot: String, plaintext: ByteArray, allowNewKey: Boolean) = plaintext.copyOf()
        override fun open(slot: String, sealed: ByteArray) = sealed.copyOf()
    }
}
