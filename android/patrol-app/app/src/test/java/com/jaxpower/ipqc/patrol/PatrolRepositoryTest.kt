package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.Issue
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test

class PatrolRepositoryTest {
    @Test fun lineIdsLabelsAndOrderingComeFromServer() = runBlocking<Unit> {
        TestServer().use { s ->
            s.login()
            s.enqueue(Fixture.directory)
            assertEquals(listOf(Fixture.line, Fixture.secondLine), s.patrol.lines())
            s.request("/api/auth/login")
            s.request("/api/line-patrol/access/verify", bearer = Fixture.ACCESS)
            s.request("/api/line-patrol/lines", "GET", Fixture.ACCESS)
        }
    }

    @Test fun malformedIncompleteAndUnavailableDirectoryFailClosed() = runBlocking<Unit> {
        TestServer().use { s ->
            s.login()
            s.enqueue(Fixture.directory.replace(Fixture.SECOND_LINE, Fixture.FIRST_LINE))
            expectIssue(Issue.LINE_CONFIGURATION) { s.patrol.lines() }
            s.enqueue("""{"items":[]}""")
            expectIssue(Issue.LINE_CONFIGURATION) { s.patrol.lines() }
            s.enqueue("""{"code":"LineConfigurationUnavailable"}""", 503)
            expectIssue(Issue.LINE_CONFIGURATION) { s.patrol.lines() }
        }
    }

    @Test fun listUsesServerLineAndTotalAndSwitchChangesOnlyQuery() = runBlocking<Unit> {
        TestServer().use { s ->
            s.login()
            s.enqueue(Fixture.page(page = 2))
            val secondPage = s.patrol.tasks(Fixture.line, 2)
            assertEquals(41, secondPage.total)
            assertTrue(secondPage.hasNext)
            assertFalse(secondPage.items.single().canExecute)
            assertFalse(secondPage.copy(page = 3).hasNext)
            s.enqueue(Fixture.page(items = "", total = 0))
            assertTrue(s.patrol.tasks(Fixture.secondLine, 1).items.isEmpty())
            s.request("/api/auth/login")
            s.request("/api/line-patrol/access/verify", bearer = Fixture.ACCESS)
            s.request("/api/line-patrol/tasks?lineId=${Fixture.FIRST_LINE}&page=2&pageSize=20", "GET", Fixture.ACCESS)
            s.request("/api/line-patrol/tasks?lineId=${Fixture.SECOND_LINE}&page=1&pageSize=20", "GET", Fixture.ACCESS)
            assertEquals(4, s.web.requestCount)
        }
    }

    @Test fun detailUsesExpectedServerLineAndPreservesFrozenText() = runBlocking<Unit> {
        TestServer().use { s ->
            s.login()
            s.enqueue(Fixture.detail())
            val detail = s.patrol.detail(Fixture.line, Fixture.selectedTask)
            assertFalse(detail.canExecute)
            assertEquals("2", detail.items.single().sampleCount)
            assertEquals("Raw fixture text", detail.items.single().photoRequirement)
            assertEquals("PendingInspection", detail.summary.status)
            s.request("/api/auth/login")
            s.request("/api/line-patrol/access/verify", bearer = Fixture.ACCESS)
            s.request("/api/line-patrol/tasks/${Fixture.TASK}?expectedTaskLineId=${Fixture.FIRST_LINE}", "GET", Fixture.ACCESS)
            assertEquals(3, s.web.requestCount)
        }
    }

    @Test fun mismatchUnsupportedLegacyAndUnexpectedExecutionCannotRender() = runBlocking<Unit> {
        TestServer().use { s ->
            s.login()
            s.enqueue("""{"code":"TaskLineMismatch"}""", 409)
            expectIssue(Issue.LINE_MISMATCH) { s.patrol.detail(Fixture.line, Fixture.selectedTask) }
            s.enqueue("""{"code":"TaskSnapshotUnavailable"}""", 409)
            expectIssue(Issue.UNSUPPORTED_TASK) { s.patrol.detail(Fixture.line, Fixture.selectedTask) }
            s.enqueue(Fixture.detail(summary = Fixture.summary(status = "UnsupportedState")))
            expectIssue(Issue.UNSUPPORTED_TASK) { s.patrol.detail(Fixture.line, Fixture.selectedTask) }
            s.enqueue(Fixture.detail().replace("ValidV1", "UnknownSnapshot"))
            expectIssue(Issue.UNSUPPORTED_TASK) { s.patrol.detail(Fixture.line, Fixture.selectedTask) }
            s.enqueue(Fixture.page(Fixture.summary(workflow = 1)))
            expectIssue(Issue.RESPONSE) { s.patrol.tasks(Fixture.line, 1) }
            s.enqueue(Fixture.detail(executable = true))
            expectIssue(Issue.RESPONSE) { s.patrol.detail(Fixture.line, Fixture.selectedTask) }
            s.enqueue(Fixture.page(Fixture.summary(user = Fixture.OTHER_USER)))
            expectIssue(Issue.RESPONSE) { s.patrol.tasks(Fixture.line, 1) }
        }
    }
}
