package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.work.*
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.SocketPolicy
import org.junit.Assert.*
import org.junit.Test

class OnlineWorkCoordinatorTest {
    @Test fun typedReadsIssueOnceAndReuseMatchedMetadataAcrossCoordinatorRecreation() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = OnlineWorkFixture(server)
            f.login()
            val coordinator = f.coordinator()
            f.enqueueRead()
            val first = coordinator.open(f.selected())
            f.readRequests()
            assertEquals(f.task, first.draft.task)
            assertEquals(7L, first.draft.version)
            assertEquals(setOf(f.attempt), first.draft.attempts)

            server.enqueue(f.issued(metadata = f.context(until = "2026-11-19T14:01:00.0000007Z")), 201)
            val issued = coordinator.issueOrReuse(first)
            assertEquals(f.expectedScope(), f.issueRequest())
            assertEquals(f.id, issued.source?.contextId)
            expectWork(WorkProblem.StaleGrant) { coordinator.issueOrReuse(first) }

            val recreated = f.coordinator()
            f.enqueueRead()
            val fresh = recreated.open(f.selected())
            f.readRequests()
            server.enqueue(f.context())
            val reused = recreated.issueOrReuse(fresh)
            server.request("${f.path}/work-contexts/${f.id}", "GET", Fixture.ACCESS)
            assertEquals(issued.source, reused.source)
            assertEquals("2026-11-19T14:01:00.0000007Z", reused.source?.captureUntilUtc)
            assertEquals("2026-11-19T14:01:00Z", reused.access.captureGrant?.until.toString())
            assertEquals(8, server.web.requestCount)
        }
    }

    @Test fun malformedTaskDraftAttemptAndExecutionClaimsFailClosed() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = OnlineWorkFixture(server)
            f.login()
            val coordinator = f.coordinator()
            val valid = f.draft()
            val malformed = listOf(
                valid.replace(f.task.taskId, testId(90)),
                valid.replace(f.task.lineId, Fixture.SECOND_LINE),
                valid.replace("\"workflowVersion\":2", "\"workflowVersion\":1"),
                valid.replace("\"draftVersion\":7", "\"draftVersion\":-1"),
                valid.replace("\"draftVersion\":7", "\"draftVersion\":9223372036854775808"),
                valid.replace("\"canExecute\":false", "\"canExecute\":true"),
                valid.replace("SharedDraftInitialized", "FoundationOnly"),
                valid.replace(f.attempt.taskItemId, testId(90)),
                valid.replace(f.attempt.inspectionAttemptId, f.attempt.taskItemId),
                valid.replace(f.pair, f.pair.dropLast(1) + "," + f.pair.drop(1)),
                valid.replace("minimumSampleCount\":2", "minimumSampleCount\":51"),
                valid.replace("minimumSampleCount\":2", "minimumSampleCount\":3"),
            )
            for (bad in malformed) {
                f.enqueueRead(body = bad)
                expectIssue(Issue.RESPONSE) { coordinator.open(f.selected()) }
                f.readRequests()
            }
            server.enqueue(f.detail().replace(Fixture.USER, Fixture.OTHER_USER))
            expectIssue(Issue.RESPONSE) { coordinator.open(f.selected()) }
            server.request("${f.path}?expectedTaskLineId=${f.task.lineId}", "GET", Fixture.ACCESS)
            assertEquals(2 + malformed.size * 2 + 1, server.web.requestCount)
        }
    }

    @Test fun typedSampleProjectionKeepsDecimalNumbersAndRejectsHistoricalAttemptRebinding() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = OnlineWorkFixture(server)
            f.login()
            val coordinator = f.coordinator()
            val sample = """[{"sampleId":"${testId(18)}","taskItemId":"${f.attempt.taskItemId}",
                "inspectionAttemptId":"${f.attempt.inspectionAttemptId}","clientSampleId":"${testId(20)}","sequenceNo":1,
                "value":{"inspectionType":"Quantitative","inspectionValue":123456789.0000000000123,"judgment":"OK","barcode":null},
                "isRemoved":false,"valueOperationId":"${testId(22)}","valueContributorUserId":"${Fixture.USER}",
                "lastOperationId":"${testId(22)}","isCurrentApplicable":true}]"""
            f.enqueueRead(body = f.draft(samples = sample))
            val grant = coordinator.open(f.selected())
            f.readRequests()
            assertEquals(setOf(f.attempt), grant.draft.attempts)
            f.enqueueRead(body = f.draft(samples = sample.replace(f.attempt.inspectionAttemptId, testId(99))))
            expectIssue(Issue.RESPONSE) { coordinator.open(f.selected()) }
            f.readRequests()
            expectWork(WorkProblem.StaleGrant) { f.work.store.operations(grant.access) }
            f.enqueueRead(body = f.draft(samples = sample.replace("123456789.0000000000123", "1e999")))
            expectIssue(Issue.RESPONSE) { coordinator.open(f.selected()) }
            f.readRequests()
        }
    }

    @Test fun validCleanFoundationCarriesNoSpeculativeAttemptsOrExecution() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = OnlineWorkFixture(server)
            f.login()
            val coordinator = f.coordinator()
            f.enqueueRead(body = f.draft(version = 0, availability = "EligibleCleanFoundationOnly", pairs = "[]"))
            val grant = coordinator.open(f.selected())
            f.readRequests()
            assertEquals(DraftAvailability.EligibleCleanFoundationOnly, grant.draft.availability)
            assertTrue(grant.draft.attempts.isEmpty())
            server.enqueue(f.issued(), 201)
            val issued = coordinator.issueOrReuse(grant)
            f.issueRequest()
            expectWork(WorkProblem.ScopeMismatch) {
                f.work.store.enqueue(issued.access, f.work.capture(context = requireNotNull(issued.source)).copy(baseDraftVersion = 0))
            }
        }
    }

    @Test fun invalidIssuedContextAndProofAreNeverPublished() = runBlocking<Unit> {
        for (variant in 0..9) {
            TestServer().use { server ->
                val f = OnlineWorkFixture(server)
                f.login()
                val coordinator = f.coordinator()
                f.enqueueRead()
                val grant = coordinator.open(f.selected())
                f.readRequests()
                val metadata = f.context()
                val body = when (variant) {
                    0 -> f.issued(metadata.replace(Fixture.USER, Fixture.OTHER_USER))
                    1 -> f.issued(metadata.replace(Fixture.FAMILY, Fixture.OTHER_FAMILY))
                    2 -> f.issued(metadata.replace(f.task.taskId, testId(90)))
                    3 -> f.issued(metadata.replace(f.task.lineId, Fixture.SECOND_LINE))
                    4 -> f.issued(metadata.replace(f.task.environmentId, "wrong-environment"))
                    5 -> f.issued(metadata.replace(f.id, "bad-context-id"))
                    6 -> f.issued(metadata.replace("\"workflowVersion\":2", "\"workflowVersion\":1"))
                    7 -> f.issued(metadata, proved = "invalid-proof-format")
                    8 -> f.issued(f.context(revoked = true))
                    else -> f.issued(f.context(until = "2026-11-23T14:00:00Z"))
                }
                server.enqueue(body, 201)
                if (variant == 7) expectWork(WorkProblem.SourceUnavailable) { coordinator.issueOrReuse(grant) }
                else expectIssue(Issue.RESPONSE) { coordinator.issueOrReuse(grant) }
                f.issueRequest()
                f.enqueueRead()
                val latest = coordinator.open(f.selected())
                f.readRequests()
                expectWork(WorkProblem.IssuanceUncertain) { coordinator.issueOrReuse(latest) }
            }
        }
    }

    @Test fun expiredAndRevokedContextsAreNotReusedAndOriginalCaptureDeadlineIsEnforced() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = OnlineWorkFixture(server)
            f.login()
            val coordinator = f.coordinator()
            f.enqueueRead()
            val read = coordinator.open(f.selected())
            f.readRequests()
            server.enqueue(f.issued(), 201)
            val old = coordinator.issueOrReuse(read)
            f.issueRequest()
            val input = f.work.capture(context = requireNotNull(old.source))
            val original = f.work.store.enqueue(old.access, input)
            f.work.offline.advance(60_000)
            expectWork(WorkProblem.CaptureWindowClosed) { f.work.store.enqueue(old.access, input.copy(operationId = testId(91))) }
            assertEquals(original.envelope.contentHash, f.work.store.enqueue(old.access, input).envelope.contentHash)
            val newId = testId(95)
            val now = server.clock.instant().toString()
            server.enqueue(f.issued(f.context(sourceId = newId, issued = now, until = "2026-11-19T14:02:00Z"), verified = now), 201)
            val replacement = coordinator.issueOrReuse(old)
            f.issueRequest()
            assertEquals(newId, replacement.source?.contextId)
            assertEquals(f.id, f.work.store.operation(replacement.access, input.operationId)?.envelope?.source?.contextId)

            val thirdId = testId(96)
            // The backend reports sticky revocation on the current candidate.
            server.enqueue(f.context(sourceId = newId, issued = now, until = "2026-11-19T14:02:00Z", revoked = true)
                .replace("2026-11-19T14:00:05Z", now))
            server.enqueue(f.issued(f.context(sourceId = thirdId, issued = now, until = "2026-11-19T14:02:00Z"), verified = now), 201)
            val next = coordinator.issueOrReuse(replacement)
            server.request("${f.path}/work-contexts/$newId", "GET", Fixture.ACCESS)
            f.issueRequest()
            assertEquals(thirdId, next.source?.contextId)
        }
    }

    @Test fun uncertainPostOutcomeSurvivesReopenAndDefiniteDenialAllowsAnExplicitRetry() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = OnlineWorkFixture(server)
            f.login()
            val first = f.coordinator()
            f.enqueueRead()
            val grant = first.open(f.selected())
            f.readRequests()
            server.enqueue("{}", 429)
            expectIssue(Issue.RATE_LIMITED) { first.issueOrReuse(grant) }
            f.issueRequest()
            f.enqueueRead()
            val retry = first.open(f.selected())
            f.readRequests()
            server.web.enqueue(MockResponse().setSocketPolicy(SocketPolicy.DISCONNECT_AFTER_REQUEST))
            expectIssue(Issue.CONNECTION) { first.issueOrReuse(retry) }
            f.issueRequest()

            val recreated = f.coordinator()
            f.enqueueRead()
            val after = recreated.open(f.selected())
            f.readRequests()
            expectWork(WorkProblem.IssuanceUncertain) { recreated.issueOrReuse(after) }
            assertEquals(10, server.web.requestCount)
        }
    }

    @Test fun duplicateCoordinatorsAndCancelledIssueDoNotReplayThePost() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = OnlineWorkFixture(server)
            f.login()
            val a = f.coordinator()
            val another = f.coordinator()
            f.enqueueRead()
            val grant = a.open(f.selected())
            f.readRequests()
            f.enqueueRead()
            val second = another.open(f.selected())
            f.readRequests()
            withHeldResponse(server, "${f.path}/work-contexts", MockResponse().setResponseCode(201).setBody(f.issued())) {
                val pending = async(Dispatchers.IO) { a.issueOrReuse(grant) }
                f.issueRequest()
                expectWork(WorkProblem.IssuanceUncertain) { another.issueOrReuse(second) }
                pending.cancelAndJoin()
            }
            f.enqueueRead()
            val after = a.open(f.selected())
            f.readRequests()
            expectWork(WorkProblem.IssuanceUncertain) { a.issueOrReuse(after) }
            assertEquals(9, server.web.requestCount)
        }
    }

    @Test fun bPreflightsExpiredQueuedAProofUsingOnlyBCurrentBearerAndCannotMarkAccepted() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = OnlineWorkFixture(server)
            val tokenB = "synthetic-b-current-access"
            f.login()
            val coordinator = f.coordinator()
            f.enqueueRead()
            val a = coordinator.open(f.selected())
            f.readRequests()
            server.enqueue(f.issued(), 201)
            val issued = coordinator.issueOrReuse(a)
            f.issueRequest()
            val source = requireNotNull(issued.source)
            val op = f.work.capture(context = source)
            val original = f.work.store.enqueue(issued.access, op)
            f.work.offline.advance(60_001)
            f.work.sessions.logout(attemptRemote = false)
            assertNull(server.vault.saved)
            f.login(f.work.b, tokenB)
            expectIssue(Issue.UNAUTHENTICATED) { coordinator.preflight(issued, op.operationId) }

            f.enqueueRead(f.work.b)
            val b = coordinator.open(f.selected())
            f.readRequests(tokenB)
            assertNull(f.work.store.operation(b.access, op.operationId))
            server.enqueue(f.preflight(source, accepted = true))
            expectIssue(Issue.RESPONSE) { coordinator.preflight(b, op.operationId) }
            f.preflightRequest(tokenB)

            f.enqueueRead(f.work.b)
            val again = coordinator.open(f.selected())
            f.readRequests(tokenB)
            server.enqueue(f.preflight(source))
            val authorized = coordinator.preflight(again, op.operationId)
            val request = f.preflightRequest(tokenB)
            assertEquals(f.expectedScope(), request.filterKeys { it != "sources" })
            val sent = request.getValue("sources").jsonArray.single().jsonObject
            assertEquals(JsonPrimitive(source.contextId), sent["workContextId"])
            assertEquals(JsonPrimitive(f.work.a.userId), sent["contributorUserIdAssertion"])
            assertTrue(sent["proofSecret"] == JsonPrimitive(f.proof))
            val preserved = requireNotNull(f.work.store.operation(authorized.access, op.operationId))
            assertEquals(WorkState.Queued, preserved.state)
            assertEquals(original.envelope.contentHash, preserved.envelope.contentHash)
            assertEquals(f.work.a.userId, preserved.envelope.source.contributorUserId)
            expectWork(WorkProblem.SourceUnavailable) {
                f.work.store.enqueue(authorized.access, op.copy(operationId = testId(91)))
            }
            expectWork(WorkProblem.StaleGrant) { f.work.store.operations(again.access) }
            assertEquals(13, server.web.requestCount)
        }
    }

    @Test fun mismatchedRevocationIsIgnoredAndConfirmedRevocationPreservesAcceptedHistoricalWork() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = OnlineWorkFixture(server)
            f.login()
            val coordinator = f.coordinator()
            f.enqueueRead()
            val a = coordinator.open(f.selected())
            f.readRequests()
            server.enqueue(f.issued(), 201)
            val issued = coordinator.issueOrReuse(a)
            f.issueRequest()
            val source = requireNotNull(issued.source)
            val acceptedInput = f.work.capture(context = source)
            val queued = f.work.capture(12, source, testId(13))
            val original = f.work.store.enqueue(issued.access, acceptedInput)
            f.work.store.enqueue(issued.access, queued)

            server.enqueue(f.preflight(source))
            val approved = coordinator.preflight(issued, acceptedInput.operationId)
            f.preflightRequest(Fixture.ACCESS)
            val batch = f.work.store.prepareBatch(approved.access, testId(29), listOf(acceptedInput.operationId))
            f.work.store.startAttempt(approved.access, batch.batchId)
            f.work.store.recordReceipt(approved.access, f.work.receipt(batch, listOf(original), uploader = f.work.a))

            val bToken = "synthetic-b-current-access"
            f.login(f.work.b, bToken)
            f.enqueueRead(f.work.b, f.draft(version = 8))
            val b = coordinator.open(f.selected())
            f.readRequests(bToken)
            fun revocation(id: String) = """{"code":"SourceSecurityRevoked","sourceContextIds":["$id"]}"""
            server.enqueue(revocation(testId(99)), 409)
            expectIssue(Issue.CONFLICT) { coordinator.preflight(b, queued.operationId) }
            f.preflightRequest(bToken)
            f.enqueueRead(f.work.b, f.draft(version = 8))
            val matched = coordinator.open(f.selected())
            f.readRequests(bToken)
            server.enqueue(revocation(source.contextId), 409)
            expectWork(WorkProblem.SourceUnavailable) { coordinator.preflight(matched, queued.operationId) }
            f.preflightRequest(bToken)

            val reopened = f.coordinator()
            f.enqueueRead(f.work.b, f.draft(version = 8))
            val after = reopened.open(f.selected())
            f.readRequests(bToken)
            val history = requireNotNull(f.work.store.operation(after.access, acceptedInput.operationId))
            assertEquals(WorkState.Accepted, history.state)
            assertEquals(original.envelope.contentHash, history.envelope.contentHash)
            assertEquals(f.work.a.userId, history.envelope.source.contributorUserId)
            val oldApproval = f.work.access(version = 8, checked = listOf(source))
            expectWork(WorkProblem.SourceUnavailable) {
                f.work.store.prepareBatch(oldApproval, testId(79), listOf(queued.operationId))
            }
            expectWork(WorkProblem.SourceUnavailable) { reopened.preflight(after, queued.operationId) }
        }
    }

    @Test fun preflight404RetainsProtectedQueuedOperationAndItsIdentity() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = OnlineWorkFixture(server)
            f.login()
            val coordinator = f.coordinator()
            f.enqueueRead()
            val read = coordinator.open(f.selected())
            f.readRequests()
            server.enqueue(f.issued(), 201)
            val issued = coordinator.issueOrReuse(read)
            f.issueRequest()
            val input = f.work.capture(context = requireNotNull(issued.source))
            val original = f.work.store.enqueue(issued.access, input)
            server.enqueue("{}", 404)
            expectIssue(Issue.NOT_FOUND) { coordinator.preflight(issued, input.operationId) }
            f.preflightRequest(Fixture.ACCESS)
            f.enqueueRead()
            val after = coordinator.open(f.selected())
            f.readRequests()
            val retained = requireNotNull(f.work.store.operation(after.access, input.operationId))
            assertEquals(WorkState.Queued, retained.state)
            assertEquals(original.envelope.contentHash, retained.envelope.contentHash)
        }
    }

    @Test fun lateDraftResponsesAndVersionRegressionCannotReplaceCurrentAuthority() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = OnlineWorkFixture(server)
            f.login()
            val coordinator = f.coordinator()
            f.enqueueRead(body = f.draft(version = 9))
            val original = coordinator.open(f.selected())
            f.readRequests()
            f.enqueueRead(body = f.draft(version = 8))
            expectWork(WorkProblem.StaleGrant) { coordinator.open(f.selected()) }
            f.readRequests()
            expectWork(WorkProblem.StaleGrant) { f.work.store.operations(original.access) }

            server.enqueue(f.detail())
            withHeldResponse(server, "${f.path}/shared-draft", MockResponse().setBody(f.draft(version = 10))) { release ->
                val slow = async(Dispatchers.IO) { runCatching { coordinator.open(f.selected()) } }
                f.readRequests()
                coordinator.clearSelection()
                release()
                assertEquals(WorkProblem.StaleGrant, (slow.await().exceptionOrNull() as? WorkFailure)?.problem)
            }

            f.enqueueRead(body = f.draft(version = 9))
            assertEquals(9L, coordinator.open(f.selected()).draft.version)
            f.readRequests()
        }
    }

    @Test fun tokenRotationAndHttpRejectionInvalidateGrantsAndNeverReplayNonIdempotentIssue() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = OnlineWorkFixture(server)
            f.login()
            val coordinator = f.coordinator()
            f.enqueueRead()
            val a = coordinator.open(f.selected())
            f.readRequests()
            f.work.offline.advance(300_000)
            f.work.rotate(restart = false)
            server.request("/api/auth/refresh")
            server.request("/api/line-patrol/access/verify", bearer = "synthetic-access-rotated")
            expectIssue(Issue.UNAUTHENTICATED) { f.work.store.operations(a.access) }

            f.enqueueRead()
            val rotated = coordinator.open(f.selected())
            f.readRequests("synthetic-access-rotated")
            server.enqueue("{}", 401)
            expectIssue(Issue.UNAUTHENTICATED) { coordinator.issueOrReuse(rotated) }
            f.issueRequest("synthetic-access-rotated")
            assertNull(f.work.sessions.resumeOffline())
            assertEquals(9, server.web.requestCount)

            val secondToken = "synthetic-b-forbidden"
            f.login(f.work.b, secondToken)
            server.enqueue("{}", 403)
            expectIssue(Issue.FORBIDDEN) { coordinator.open(f.selected()) }
            server.request("${f.path}?expectedTaskLineId=${f.task.lineId}", "GET", secondToken)
            assertNull(f.work.sessions.resumeOffline())
            assertNull(server.vault.saved)
            assertEquals(12, server.web.requestCount)
        }
    }

    @Test fun logoutWhileDraftIsInFlightRejectsLateGrantAndOptionalFailureKeepsOfflineSnapshots() = runBlocking<Unit> {
        TestServer().use { server ->
            val f = OnlineWorkFixture(server)
            f.login()
            val fPatrol = f.work.offline.patrol
            server.enqueue(Fixture.directory)
            fPatrol.lines()
            server.request("/api/line-patrol/lines", "GET", Fixture.ACCESS)
            server.enqueue(Fixture.page())
            fPatrol.tasks(Fixture.line, 1)
            server.request("/api/line-patrol/tasks?lineId=${Fixture.FIRST_LINE}&page=1&pageSize=20", "GET", Fixture.ACCESS)
            val before = f.work.offline.snapshots.page(f.work.a, Fixture.FIRST_LINE, 1)
            assertNotNull(before)

            val coordinator = f.coordinator()
            server.enqueue(f.detail())
            server.enqueue("{}", 503)
            expectIssue(Issue.UNAVAILABLE) { coordinator.open(f.selected()) }
            f.readRequests()
            assertEquals(before, f.work.offline.snapshots.page(f.work.a, Fixture.FIRST_LINE, 1))

            server.enqueue(f.detail())
            withHeldResponse(server, "${f.path}/shared-draft", MockResponse().setBody(f.draft())) { release ->
                val waiting = async(Dispatchers.IO) { runCatching { coordinator.open(f.selected()) } }
                f.readRequests()
                f.work.sessions.logout(attemptRemote = false)
                release()
                assertEquals(Issue.UNAUTHENTICATED,
                    (waiting.await().exceptionOrNull() as? com.jaxpower.ipqc.patrol.api.PatrolFailure)?.issue)
            }
            assertEquals(before, f.work.offline.snapshots.page(f.work.a, Fixture.FIRST_LINE, 1))
        }
    }

}
