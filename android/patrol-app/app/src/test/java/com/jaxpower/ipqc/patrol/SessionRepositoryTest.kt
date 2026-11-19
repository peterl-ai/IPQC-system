package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.Fixture.ACCESS
import com.jaxpower.ipqc.patrol.Fixture.REFRESH
import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.httpsOrigin
import com.jaxpower.ipqc.patrol.auth.StoredRefresh
import java.time.Instant
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.async
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import kotlinx.coroutines.Dispatchers
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.SocketPolicy
import org.junit.Assert.*
import org.junit.Test
import java.util.concurrent.TimeUnit

class SessionRepositoryTest {
    @Test fun nativeLoginVerifiesBeforeStoringOnlyRefresh() = runBlocking<Unit> {
        TestServer().use { s ->
            s.login()
            val login = Json.parseToJsonElement(s.request("/api/auth/login").body.readUtf8()).jsonObject
            assertEquals(setOf("username", "password", "clientType"), login.keys)
            assertEquals("Native", login.getValue("clientType").jsonPrimitive.content)
            s.request("/api/line-patrol/access/verify", bearer = ACCESS)
            val saved = requireNotNull(s.vault.saved)
            assertEquals(REFRESH, saved.refreshToken)
            val persisted = Json.encodeToString(saved)
            assertFalse(persisted.contains(ACCESS))
            assertFalse(persisted.contains("fixture-password"))
            assertFalse(saved.toString().contains(REFRESH))
        }
    }

    @Test fun rolesInactiveAndMissingPermissionGetNoPatrolGrant() = runBlocking<Unit> {
        TestServer().use { s ->
            for (body in listOf(Fixture.auth(role = "Admin"), Fixture.auth(role = "PQE"),
                Fixture.auth(active = false), Fixture.auth(permission = false))) {
                s.enqueue(body)
                expectIssue(Issue.FORBIDDEN) { s.session.login("fixture", "fixture-pass") }
                assertNull(s.vault.saved)
                s.request("/api/auth/login")
            }
            assertEquals(4, s.web.requestCount)
        }
    }

    @Test fun admissionNeedsMatchingOnlineUserAndFamilyReceipt() = runBlocking<Unit> {
        TestServer().use { s ->
            s.admit(receipt = Fixture.receipt(user = Fixture.OTHER_USER))
            expectIssue(Issue.RESPONSE) { s.session.login("fixture", "fixture-pass") }
            assertNull(s.vault.saved)
            expectIssue(Issue.UNAUTHENTICATED) { s.patrol.lines() }
            assertEquals(2, s.web.requestCount)
        }
    }

    @Test fun serverReceiptCanonicalizesOnlyPostgresTimestampPrecision() = runBlocking<Unit> {
        TestServer().use { s ->
            val canonical = "2026-11-26T14:00:00.123456Z"
            val native = Fixture.auth().replace(Fixture.FAMILY_EXPIRY, "2026-11-26T14:00:00.1234567Z")
            val receipt = Fixture.receipt().replace(Fixture.FAMILY_EXPIRY, canonical)
            s.admit(native, receipt.replace(canonical, "2026-11-26T14:00:00.123455Z"))
            expectIssue(Issue.RESPONSE) { s.session.login("fixture", "fixture-pass") }
            assertNull(s.vault.saved)
            s.admit(native, receipt)
            s.session.login("fixture", "fixture-pass")
            assertEquals(Instant.parse(canonical), Instant.parse(s.vault.saved?.expiresAtUtc))
            s.admit(Fixture.auth(access = "new-access", refresh = "new-refresh")
                .replace(Fixture.FAMILY_EXPIRY, canonical), receipt)
            assertNotNull(s.newRepository().restore())
        }
    }

    @Test fun serverLoginFailureIsDistinctFromPermissionFailure() = runBlocking<Unit> {
        TestServer().use { s ->
            s.enqueue("""{"title":"redacted"}""", 401)
            expectIssue(Issue.INVALID_LOGIN) { s.session.login("fixture", "incorrect-test-password") }
            assertNull(s.vault.saved)
        }
    }

    @Test fun forcedPasswordChangeIsOnlineOnlyAndRequiresNewLogin() = runBlocking<Unit> {
        TestServer().use { s ->
            s.enqueue(Fixture.auth(forced = true))
            assertTrue(s.session.login("fixture", "original-test-password").mustChangePassword)
            expectIssue(Issue.FORBIDDEN) { s.patrol.lines() }
            assertNull(s.vault.saved)
            s.enqueue("""{"errors":{"password":["test rejection"]}}""", 400)
            expectIssue(Issue.CHANGE_REJECTED) { s.session.changePassword("original-test-password", "invalid") }
            s.web.enqueue(MockResponse().setResponseCode(204))
            s.session.changePassword("original-test-password", "new-test-password")
            s.request("/api/auth/login")
            s.request("/api/auth/change-password", bearer = ACCESS)
            val change = Json.parseToJsonElement(s.request("/api/auth/change-password", bearer = ACCESS).body.readUtf8()).jsonObject
            assertEquals(setOf("currentPassword", "newPassword"), change.keys)
            assertNull(s.vault.saved)
            assertNull(s.session.restore())
            expectIssue(Issue.UNAUTHENTICATED) { s.patrol.lines() }
            assertEquals(3, s.web.requestCount)
        }
    }

    @Test fun ambiguousPasswordChangeClearsTheForcedSession() = runBlocking<Unit> {
        TestServer().use { s ->
            s.enqueue(Fixture.auth(forced = true))
            s.session.login("fixture", "original-test-password")
            s.enqueue("{}", 200)
            expectIssue(Issue.REAUTHENTICATE) { s.session.changePassword("original-test-password", "new-test-password") }
            assertNull(s.session.restore())
            expectIssue(Issue.UNAUTHENTICATED) { s.session.changePassword("original-test-password", "new-test-password") }
            assertEquals(2, s.web.requestCount)
        }
    }

    @Test fun concurrentReadsRotateExactlyOnceAndPreserveFamily() = runBlocking<Unit> {
        TestServer().use { s ->
            s.login()
            s.request("/api/auth/login")
            s.request("/api/line-patrol/access/verify", bearer = ACCESS)
            s.clock.current = Instant.parse("2026-11-19T14:04:55Z")
            s.admit(Fixture.auth(access = "synthetic-access-b", refresh = "synthetic-refresh-b", expires = "2026-11-19T14:10:00Z"))
            s.enqueue(Fixture.directory)
            s.enqueue(Fixture.directory)
            val one = async { s.patrol.lines() }
            val two = async { s.patrol.lines() }
            assertEquals(one.await(), two.await())
            val refresh = s.request("/api/auth/refresh").body.readUtf8()
            assertEquals("""{"refreshToken":"$REFRESH"}""", refresh)
            s.request("/api/line-patrol/access/verify", bearer = "synthetic-access-b")
            repeat(2) { s.request("/api/line-patrol/lines", "GET", "synthetic-access-b") }
            assertEquals(6, s.web.requestCount)
            assertEquals(Fixture.FAMILY, s.vault.saved?.familyId)
            assertEquals("synthetic-refresh-b", s.vault.saved?.refreshToken)
        }
    }

    @Test fun restartRequiresRotationAndOnlineVerification() = runBlocking<Unit> {
        TestServer().use { s ->
            s.login()
            val restarted = s.newRepository()
            expectIssue(Issue.UNAUTHENTICATED) { restarted.verifyOnline() }
            s.admit(Fixture.auth(access = "new-access", refresh = "new-refresh"))
            assertNotNull(restarted.restore())
            s.request("/api/auth/login")
            s.request("/api/line-patrol/access/verify", bearer = ACCESS)
            s.request("/api/auth/refresh")
            s.request("/api/line-patrol/access/verify", bearer = "new-access")
        }
    }

    @Test fun refreshRejectionClearsReusableCredentialsAndStopsReads() = runBlocking<Unit> {
        TestServer().use { s ->
            s.login()
            s.enqueue("{}", 401)
            s.enqueue("{}", 401)
            expectIssue(Issue.UNAUTHENTICATED) { s.patrol.lines() }
            assertNull(s.vault.saved)
            expectIssue(Issue.UNAUTHENTICATED) { s.patrol.lines() }
            assertEquals(4, s.web.requestCount)
        }
    }

    @Test fun unknownRefreshCannotRepeatAnOldTokenEvenAfterRestart() = runBlocking<Unit> {
        TestServer().use { s ->
            s.login()
            s.web.enqueue(MockResponse().setSocketPolicy(SocketPolicy.DISCONNECT_AFTER_REQUEST))
            val restarted = s.newRepository()
            expectIssue(Issue.REAUTHENTICATE) { restarted.restore() }
            assertNull(s.vault.saved)
            assertNull(s.newRepository().restore())
            assertEquals(3, s.web.requestCount)
        }
    }

    @Test fun explicitHttpRetryAfterDoesNotRepeatRotatingPost() = runBlocking<Unit> {
        TestServer().use { s ->
            s.login()
            s.web.enqueue(MockResponse().setResponseCode(503).setHeader("Retry-After", "0"))
            s.admit(Fixture.auth(access = "new-access", refresh = "new-refresh"))
            expectIssue(Issue.REAUTHENTICATE) { s.newRepository().restore() }
            assertNull(s.vault.saved)
            assertEquals(3, s.web.requestCount)
        }
    }

    @Test fun changedFamilyOrOriginCannotRestoreSavedAccess() = runBlocking<Unit> {
        TestServer().use { s ->
            s.login()
            s.enqueue(Fixture.auth(family = Fixture.OTHER_FAMILY, refresh = "new-refresh"))
            expectIssue(Issue.REAUTHENTICATE) { s.newRepository().restore() }
            assertNull(s.vault.saved)
            s.vault.saved = StoredRefresh("https://different.example.invalid/", REFRESH, Fixture.USER, Fixture.FAMILY,
                "synthetic-env", Fixture.FAMILY_EXPIRY)
            expectIssue(Issue.REAUTHENTICATE) { s.newRepository().restore() }
            assertNull(s.vault.saved)
            assertEquals(3, s.web.requestCount)
        }
    }

    @Test fun logoutClearsLocalAndPostsOriginalNativeBearerAndRefresh() = runBlocking<Unit> {
        TestServer().use { s ->
            s.login()
            s.web.enqueue(MockResponse().setResponseCode(204))
            assertTrue(s.session.logout())
            assertNull(s.vault.saved)
            expectIssue(Issue.UNAUTHENTICATED) { s.patrol.lines() }
            s.request("/api/auth/login")
            s.request("/api/line-patrol/access/verify", bearer = ACCESS)
            assertEquals("""{"refreshToken":"$REFRESH"}""",
                s.request("/api/auth/logout", bearer = ACCESS).body.readUtf8())
        }
    }

    @Test fun offlineLogoutClearsLocalWithoutClaimingRemoteRevocation() = runBlocking<Unit> {
        TestServer().use { s ->
            s.login()
            assertFalse(s.session.logout(attemptRemote = false))
            assertNull(s.vault.saved)
            assertNull(s.newRepository().restore())
            assertEquals(2, s.web.requestCount)
        }
    }

    @Test fun cancellingTheLogoutCallerStillClearsTheLocalVault() = runBlocking<Unit> {
        TestServer().use { s ->
            s.login()
            val logout = launch(start = CoroutineStart.UNDISPATCHED) { s.session.logout(attemptRemote = false) }
            logout.cancelAndJoin()
            assertNull(s.vault.saved)
            assertNull(s.newRepository().restore())
            expectIssue(Issue.UNAUTHENTICATED) { s.patrol.lines() }
            assertEquals(2, s.web.requestCount)
        }
    }

    @Test fun inFlightGetCannotReturnAfterExplicitLogoutOrToAnotherUser() = runBlocking<Unit> {
        TestServer().use { s ->
            s.login()
            s.request("/api/auth/login")
            s.request("/api/line-patrol/access/verify", bearer = ACCESS)
            s.web.enqueue(MockResponse().setBody(Fixture.directory).setBodyDelay(850, TimeUnit.MILLISECONDS))
            val pending = async { expectIssue(Issue.UNAUTHENTICATED) { s.patrol.lines() } }
            withContext(Dispatchers.IO) { s.request("/api/line-patrol/lines", "GET", ACCESS) }
            s.web.enqueue(MockResponse().setResponseCode(204))
            assertTrue(s.session.logout())
            s.admit(Fixture.auth(user = Fixture.OTHER_USER, family = Fixture.OTHER_FAMILY,
                access = "other-access", refresh = "other-refresh"), Fixture.receipt(Fixture.OTHER_USER, Fixture.OTHER_FAMILY))
            s.session.login("fixture-two", "fixture-password-two")
            pending.await()
            assertEquals(Fixture.OTHER_USER, s.vault.saved?.userId)
        }
    }

    @Test fun onlyAPlainHttpsOriginCanConfigureTheApp() {
        assertNotNull(httpsOrigin("https://app.example.invalid:443/"))
        for (bad in listOf("", "http://app.example.invalid/", "https://user@app.example.invalid/",
            "https://app.example.invalid/a", "https://app.example.invalid/?foo", "https://app.example.invalid/#part",
            "https://app.example.invalid/\u0001", "https://app.example.invalid\\other")) {
            assertNull(bad, httpsOrigin(bad))
        }
    }
}
