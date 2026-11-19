package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import com.jaxpower.ipqc.patrol.tasks.PatrolRepository
import com.jaxpower.ipqc.patrol.ui.Load
import com.jaxpower.ipqc.patrol.ui.PatrolUi
import com.jaxpower.ipqc.patrol.ui.PatrolViewModel
import com.jaxpower.ipqc.patrol.ui.Screen
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.setMain
import kotlinx.coroutines.withTimeout
import okhttp3.mockwebserver.MockResponse
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class OfflineViewModelTest {
    @Before fun useMain() { Dispatchers.setMain(Dispatchers.Unconfined) }
    @After fun resetMain() { Dispatchers.resetMain() }

    @Test fun firstTimeOfflineCannotSignInOrSendAnyRequests() = runBlocking<Unit> {
        TestServer().use { server ->
            val fixture = OfflineTestFixture(server)
            val model = PatrolViewModel(fixture.sessions, fixture.patrol, MutableStateFlow(false), fixture.cache)
            await(model) { it.issue == Issue.OFFLINE && !it.busy }
            model.login("fixture", "fixture")
            model.resume()
            assertEquals(Screen.Login, model.ui.value.screen)
            assertEquals(0, server.web.requestCount)
            model.leaveForeground()
        }
    }

    @Test fun offlineNavigationIsLocalAndReconnectRequiresActualOnlineVerification() = runBlocking<Unit> {
        TestServer().use { server ->
            val fixture = OfflineTestFixture(server)
            fixture.download(detail = true)
            val network = MutableStateFlow(false)
            val model = PatrolViewModel(fixture.sessions, fixture.patrol, network, fixture.cache)
            try {
                await(model) { it.screen is Screen.Home && it.offline != null }
                model.openPatrol()
                await(model) { (it.screen as? Screen.Lines)?.load is Load.Data }
                model.select(Fixture.line)
                await(model) { (it.screen as? Screen.Tasks)?.load is Load.Data }
                model.openTask(Fixture.selectedTask)
                await(model) { (it.screen as? Screen.Detail)?.load is Load.Data }
                model.switchLine()
                await(model) { (it.screen as? Screen.Lines)?.load is Load.Data }
                model.select(Fixture.secondLine)
                val empty = await(model) { (it.screen as? Screen.Tasks)?.load is Load.Data }.screen as Screen.Tasks
                assertTrue((empty.load as Load.Data).value.items.isEmpty())
                assertEquals(5, server.web.requestCount)

                // Protect a screen restored after the process has spent time in the background.
                model.leaveForeground()
                assertEquals(Screen.Login, model.ui.value.screen)
                model.returnToForeground()
                await(model) { it.screen is Screen.Home && it.offline != null }

                server.web.enqueue(MockResponse().setBody(Fixture.receipt()).setBodyDelay(800, TimeUnit.MILLISECONDS))
                network.value = true
                await(model) { it.screen == Screen.Login && it.busy && it.offline == null }
                model.openPatrol()
                val restored = await(model) { it.screen is Screen.Home && it.offline == null }
                assertTrue(restored.online)
                assertEquals(6, server.web.requestCount)
                repeat(5) { assertNotNull(server.web.takeRequest(4, TimeUnit.SECONDS)) }
                server.request("/api/line-patrol/access/verify", bearer = Fixture.ACCESS)
            } finally {
                model.leaveForeground()
            }
        }
    }

    @Test fun missingRefreshTokenWithWifiStillAllowsOnlyTheVerifiedReadOnlyCache() = runBlocking<Unit> {
        TestServer().use { server ->
            val fixture = OfflineTestFixture(server)
            fixture.download()
            server.vault.clear()
            fixture.rebootAt(60_000)
            val cache = fixture.reopen()
            val restarted = SessionRepository(server.api, server.vault, server.origin.toString(), server.clock, cache)
            val model = PatrolViewModel(restarted, PatrolRepository(server.api, restarted, cache),
                MutableStateFlow(true), cache)
            try {
                val home = await(model) { it.screen is Screen.Home && it.offline != null }
                assertTrue(home.online)
                assertEquals(OfflineTestFixture.SUBJECT, home.offline?.identity)
                assertEquals(4, server.web.requestCount)
            } finally {
                model.leaveForeground()
            }
        }
    }

    @Test fun expiredReceiptClosesTheVisibleOfflineScreenWithoutCallingServer() = runBlocking<Unit> {
        TestServer().use { server ->
            val fixture = OfflineTestFixture(server)
            fixture.download()
            val model = PatrolViewModel(fixture.sessions, fixture.patrol, MutableStateFlow(false), fixture.cache)
            try {
                await(model) { it.screen is Screen.Home && it.offline != null }
                model.openPatrol()
                await(model) { (it.screen as? Screen.Lines)?.load is Load.Data }
                model.select(Fixture.line)
                await(model) { (it.screen as? Screen.Tasks)?.load is Load.Data }
                fixture.advance(72 * 3600 * 1_000L)
                await(model) { it.screen == Screen.Login && it.issue == Issue.OFFLINE_EXPIRED && it.offline == null }
                assertEquals(4, server.web.requestCount)
            } finally {
                model.leaveForeground()
            }
        }
    }

    private suspend fun await(model: PatrolViewModel, predicate: (PatrolUi) -> Boolean) =
        withTimeout(8_000) { model.ui.first { predicate(it) } }
}
