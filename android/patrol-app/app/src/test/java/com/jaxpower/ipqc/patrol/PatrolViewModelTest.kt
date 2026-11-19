package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.ui.Load
import com.jaxpower.ipqc.patrol.ui.PatrolUi
import com.jaxpower.ipqc.patrol.ui.PatrolViewModel
import com.jaxpower.ipqc.patrol.ui.Screen
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.setMain
import kotlinx.coroutines.withTimeout
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class PatrolViewModelTest {
    @Before fun mainDispatcher() { Dispatchers.setMain(Dispatchers.Unconfined) }
    @After fun resetDispatcher() { Dispatchers.resetMain() }

    private suspend fun waitFor(model: PatrolViewModel, condition: (PatrolUi) -> Boolean): PatrolUi =
        withTimeout(6_000) { model.ui.first { condition(it) } }

    @Test fun lineSwitchUsesFreshDirectoryAndOfflineClearsVisibleTasks() = runBlocking<Unit> {
        TestServer().use { s ->
            val connectivity = MutableStateFlow(false)
            val model = PatrolViewModel(s.session, s.patrol, connectivity)
            waitFor(model) { it.issue == Issue.OFFLINE }
            model.login("fixture", "fixture")
            assertEquals(0, s.web.requestCount)
            connectivity.value = true
            waitFor(model) { it.online && it.issue == null }
            s.admit()
            model.login("fixture", "fixture")
            model.login("fixture", "fixture")
            waitFor(model) { it.screen is Screen.Home }
            assertEquals(2, s.web.requestCount)
            s.enqueue(Fixture.directory)
            model.openPatrol()
            waitFor(model) { (it.screen as? Screen.Lines)?.load is Load.Data }
            model.select(Fixture.line.copy(id = "70000000-0000-0000-0000-000000000001"))
            assertEquals(3, s.web.requestCount)
            s.enqueue(Fixture.page())
            model.select(Fixture.line)
            waitFor(model) { (it.screen as? Screen.Tasks)?.load is Load.Data }
            s.enqueue(Fixture.directory)
            model.switchLine()
            waitFor(model) { (it.screen as? Screen.Lines)?.load is Load.Data }
            s.enqueue(Fixture.page(items = "", total = 0))
            model.select(Fixture.secondLine)
            val loaded = waitFor(model) { (it.screen as? Screen.Tasks)?.load is Load.Data }.screen as Screen.Tasks
            assertEquals(Fixture.SECOND_LINE, loaded.line.id)
            assertTrue((loaded.load as Load.Data).value.items.isEmpty())
            connectivity.value = false
            assertEquals(Screen.Login, waitFor(model) { it.issue == Issue.OFFLINE }.screen)
            assertEquals(6, s.web.requestCount)
        }
    }

    @Test fun loadingEmptyAndServerErrorCannotReuseOldTaskRows() = runBlocking<Unit> {
        TestServer().use { s ->
            val network = MutableStateFlow(false)
            val model = PatrolViewModel(s.session, s.patrol, network)
            waitFor(model) { it.issue == Issue.OFFLINE }
            network.value = true
            waitFor(model) { it.online && it.issue == null }
            s.admit()
            model.login("fixture", "fixture")
            waitFor(model) { it.screen is Screen.Home }
            s.enqueue(Fixture.directory)
            model.openPatrol()
            waitFor(model) { (it.screen as? Screen.Lines)?.load is Load.Data }
            s.enqueue(Fixture.page())
            model.select(Fixture.line)
            waitFor(model) { (it.screen as? Screen.Tasks)?.load is Load.Data }
            s.web.enqueue(okhttp3.mockwebserver.MockResponse()
                .setHeadersDelay(450, java.util.concurrent.TimeUnit.MILLISECONDS)
                .setResponseCode(503))
            model.refresh()
            assertIsLoading(model.ui.value)
            val failed = waitFor(model) { (it.screen as? Screen.Tasks)?.load is Load.Failed }.screen as Screen.Tasks
            assertEquals(Issue.UNAVAILABLE, (failed.load as Load.Failed).issue)
            s.enqueue(Fixture.page(items = "", total = 0))
            model.refresh()
            val empty = waitFor(model) { (it.screen as? Screen.Tasks)?.load is Load.Data }.screen as Screen.Tasks
            assertTrue((empty.load as Load.Data).value.items.isEmpty())
        }
    }

    private fun assertIsLoading(ui: PatrolUi) {
        assertTrue(ui.busy)
        assertEquals(Load.Loading, (ui.screen as Screen.Tasks).load)
    }
}
