package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.work.*
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit.SECONDS
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.*
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.SocketPolicy
import org.junit.Assert.*
import org.junit.Test

class SampleUploadCoordinatorTest {
    @Test fun bSendsExactOriginalWorkContextAndDecimalAfterALogsOut() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = Exchange(server)
            e.prepare(issue = true, number = "79228162514264337593543950335")
            val original = e.operation.envelope
            val b = e.signInB()
            val receipt = e.receipt(actor = e.f.work.b)
            e.queueAttempt()
            server.enqueue(uploadResult(receipt))
            assertEquals(SampleUploadResult.Accepted(receipt, false), e.upload(b))
            val sent = e.consumeAttempt()
            val scope = e.f.expectedScope()
            assertEquals(scope, sent.filterKeys { it in scope.keys })
            assertEquals(JsonPrimitive(e.batch.batchId), sent["batchId"])
            assertEquals(JsonPrimitive(7), sent["expectedDraftVersion"])
            assertEquals(JsonPrimitive(1), sent["canonicalizationVersion"])
            val item = sent.getValue("operations").jsonArray.single().jsonObject
            val op = original.operation
            assertEquals(JsonPrimitive(op.operationId), item["operationId"])
            assertEquals(JsonPrimitive(op.attempt.taskItemId), item["taskItemId"])
            assertEquals(JsonPrimitive(op.attempt.inspectionAttemptId), item["inspectionAttemptId"])
            assertEquals(JsonPrimitive(op.clientSampleId), item["clientSampleId"])
            assertEquals(JsonPrimitive(original.originLocalEditVersion), item["originLocalEditVersion"])
            assertEquals(JsonPrimitive(original.source.contextId), item["sourceWorkContextId"])
            assertEquals(JsonPrimitive(e.f.work.a.userId), item["contributorUserIdAssertion"])
            val value = item.getValue("data").jsonObject.getValue("inspectionValue").jsonPrimitive
            assertFalse(value.isString)
            assertEquals(op.data?.inspectionValue, value.content)
            assertFalse("uploadedByUserId" in sent)
            assertFalse("contentHash" in item)
            val proof = sent.getValue("sourceProofs").jsonArray.single().jsonObject
            assertEquals(setOf("workContextId", "proofSecret"), proof.keys)
            assertTrue(proof["proofSecret"]?.jsonPrimitive?.content == e.f.proof)
            assertEquals(JsonPrimitive(original.source.contextId), proof["workContextId"])
            val stored = requireNotNull(e.f.work.reopen().operation(b.access, op.operationId))
            assertEquals(WorkState.Accepted, stored.state)
            assertEquals(original.canonicalPayload, stored.envelope.canonicalPayload)
            assertEquals(original.contentHash, stored.envelope.contentHash)
            assertEquals(original.source, stored.envelope.source)
            assertEquals(e.f.work.a.familyId, stored.envelope.source.originPersonalSessionId)
            assertEquals(e.f.work.b.familyId, e.f.work.store.batch(b.access, e.batch.batchId)?.receipt?.uploadedPersonalSessionId)
            assertEquals(13, server.web.requestCount)
        }
    }

    @Test fun fullReplayKeepsCAndAnAcceptedBatchUsesGetOnly() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = Exchange(server)
            e.prepare()
            val b = e.signInB()
            val receipt = e.receipt(actor = e.c)
            e.queueAttempt()
            server.enqueue(uploadResult(receipt, replay = true))
            assertEquals(SampleUploadResult.Accepted(receipt, true), e.upload(b))
            e.consumeAttempt()
            server.enqueue(receipt.wire())
            assertEquals(ReceiptRecovery.BatchConfirmed(receipt), e.coordinator.recoverBatchReceipt(b, e.batch.batchId))
            server.request(e.receiptPath, "GET", e.token)
            e.queueRefresh()
            expectWork(WorkProblem.InvalidState) { e.upload(b) }
            e.f.readRequests(e.token)
            assertEquals(15, server.web.requestCount)
        }
    }

    @Test fun pureReplayAndRepeated404PreserveBarrierUntilBRecoversCReceipt() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = Exchange(server)
            val a = e.prepare()
            val sibling = e.queueSibling()
            val receipt = e.receipt(actor = e.c)
            e.queueAttempt()
            server.enqueue(uploadResult(null, true, receipt.operations))
            assertEquals(SampleUploadResult.NeedsBatchReceipt, e.upload(a))
            e.consumeAttempt()
            repeat(2) {
                server.enqueue("{}", 404)
                assertEquals(ReceiptRecovery.Unresolved(ReceiptUncertainty.NotObserved),
                    e.coordinator.recoverBatchReceipt(a, e.batch.batchId))
                server.request(e.receiptPath, "GET", e.token)
            }
            e.queueRefresh()
            expectWork(WorkProblem.InvalidState) { e.coordinator.uploadBatch(a, sibling.batchId) }
            e.f.readRequests(e.token)
            assertEquals(WorkState.OutcomeUnknown, e.state())
            assertEquals(WorkState.Queued, e.f.work.store.batch(e.f.work.access(), sibling.batchId)?.state)
            assertEquals(12, server.web.requestCount)
            val b = e.signInB()
            server.enqueue(receipt.wire())
            assertEquals(ReceiptRecovery.BatchConfirmed(receipt), e.coordinator.recoverBatchReceipt(b, e.batch.batchId))
            server.request(e.receiptPath, "GET", e.token)
            assertEquals(e.c.familyId, e.f.work.store.batch(b.access, e.batch.batchId)?.receipt?.uploadedPersonalSessionId)
        }
    }

    @Test fun freshReadRejectsWrongVersionAttemptDispositionAndOtherTask() = runBlocking<Unit> {
        for (mutation in listOf("version", "task", "line", "workflow", "attempt", "removed", "batch")) {
            TestServer().use { server ->
                val e = Exchange(server)
                val a = e.prepare()
                val scope = when (mutation) {
                    "task" -> e.f.task.copy(taskId = testId(44))
                    "line" -> e.f.task.copy(lineId = Fixture.SECOND_LINE)
                    else -> e.f.task
                }
                val grant = if (scope == e.f.task) a else e.open(scope)
                val fresh = when (mutation) {
                    "version" -> e.draft(scope, 8)
                    "workflow" -> e.draft(scope).replace(""""workflowVersion":2""", """"workflowVersion":1""")
                    "attempt" -> e.draft(scope).replace(e.f.attempt.inspectionAttemptId, testId(98))
                    "removed" -> e.draft(scope).replace("Applicable", "UnableToMeasureNG")
                    else -> e.draft(scope)
                }
                e.f.enqueueRead(body = fresh, scope = scope)
                val id = if (mutation == "batch") testId(94) else e.batch.batchId
                when (mutation) {
                    "workflow" -> expectIssue(Issue.RESPONSE) { e.coordinator.uploadBatch(grant, id) }
                    "version", "attempt", "removed" -> expectWork(WorkProblem.ScopeMismatch) { e.coordinator.uploadBatch(grant, id) }
                    else -> expectWork(WorkProblem.InvalidState) { e.coordinator.uploadBatch(grant, id) }
                }
                e.f.readRequests(scope = scope)
                assertEquals(WorkState.Queued, e.state())
                assertEquals(if (scope == e.f.task) 6 else 8, server.web.requestCount)
            }
        }
    }

    @Test fun currentUserInAnotherEnvironmentCannotSendTheOriginalSealedBatch() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = Exchange(server)
            e.prepare()
            e.f.work.sessions.logout(attemptRemote = false)
            val b = e.f.work.b.copy(environment = "synthetic-other-env")
            val token = "synthetic-other-env-b"
            e.f.login(b, token)
            val scope = e.f.task.copy(environmentId = b.environment)
            repeat(2) {
                server.enqueue(e.f.detail(b, scope).replace(e.f.task.environmentId, scope.environmentId))
                server.enqueue(e.draft(scope))
            }
            val current = e.coordinator.open(e.f.selected(scope))
            e.f.readRequests(token, scope)
            expectWork(WorkProblem.InvalidState) { e.upload(current) }
            e.f.readRequests(token, scope)
            assertEquals(10, server.web.requestCount)
        }
    }

    @Test fun absentProofWrongContributorRevocationAndBrokenDependenciesCannotSend() = runBlocking<Unit> {
        for (mutation in listOf("proof", "actor", "revoked", "foreign", "hash", "missing")) {
            TestServer().use { server ->
                val e = Exchange(server)
                val grant = e.prepare(dependent = true)
                val foreignOp = if (mutation == "foreign") {
                    val foreign = e.f.task.copy(taskId = testId(82))
                    val foreignSource = e.source.copy(task = foreign)
                    val other = e.f.work.access(foreign, checked = listOf(foreignSource))
                    e.f.work.store.storeSource(other, foreignSource, e.f.work.proof)
                    val operation = e.f.work.store.enqueue(other, e.f.work.capture(91, foreignSource))
                    val batch = e.f.work.store.prepareBatch(other, testId(93), listOf(operation.envelope.operation.operationId))
                    e.f.work.store.startAttempt(other, batch.batchId)
                    val result = e.f.work.receipt(batch, listOf(operation), e.f.work.a)
                    e.f.work.store.recordReceipt(other, result.copy(taskId = foreign.taskId,
                        operations = result.operations.map { it.copy(taskId = foreign.taskId) }))
                    operation.envelope.reference()
                } else null
                e.f.work.rewriteFixture {
                    when (mutation) {
                        "proof" -> save(StoredWorkSource(e.source, ""))
                        "actor" -> save(StoredWorkSource(e.source.copy(contributorUserId = e.f.work.b.userId), e.f.work.proof))
                        "revoked" -> rememberSourceRevoked(e.source.contextId)
                        else -> {
                            val child = requireNotNull(operation(testId(11)))
                            val prior = child.envelope.operation.dependencies.single()
                            val ref = when (mutation) {
                                "foreign" -> requireNotNull(foreignOp)
                                "hash" -> prior.copy(contentHash = "F".repeat(64))
                                else -> prior.copy(operationId = testId(92))
                            }
                            val updated = child.envelope.operation.copy(dependencies = listOf(ref))
                            val edit = child.envelope.originLocalEditVersion
                            val payload = DraftOperationCanonical.operation(e.source, updated, edit)
                            val rewritten = OperationEnvelope(workspaceId, updated, e.source, edit,
                                CANONICAL_VERSION, payload, workHash(payload))
                            save(child.copy(envelope = rewritten))
                            val batch = requireNotNull(batch(e.batch.batchId))
                            val members = batch.operations.map { if (it.operationId == updated.operationId) rewritten.reference() else it }
                            val frontier = if (mutation == "hash") emptyList() else listOf(ref)
                            save(batch.copy(operations = members, acceptedReferences = frontier,
                                fingerprint = DraftOperationCanonical.batch(e.f.task, batch.batchId, 7, members, frontier)))
                        }
                    }
                }
                e.queueRefresh()
                val problem = when (mutation) {
                    "proof", "actor", "revoked" -> WorkProblem.SourceUnavailable
                    "hash" -> WorkProblem.StorageUnavailable
                    else -> WorkProblem.InvalidState
                }
                expectWork(problem) { e.upload(grant) }
                e.f.readRequests()
                assertEquals(WorkState.Queued, e.state())
                assertEquals(6, server.web.requestCount)
            }
        }
    }



    @Test fun onlyMatchedPreflightRevocationQuarantinesWithoutBusinessPost() = runBlocking<Unit> {
        for (revoked in listOf(false, true)) {
            TestServer().use { server ->
                val e = Exchange(server)
                val a = e.prepare()
                e.queueRefresh()
                if (revoked) problem(server, 409, "SourceSecurityRevoked", ""","sourceContextIds":["${e.source.contextId}"]""")
                else server.enqueue(e.f.preflight(e.source.copy(contributorUserId = e.f.work.b.userId)))
                if (revoked) assertEquals(SampleUploadResult.Rejected(WorkConflict.SourceSecurityRevoked), e.upload(a))
                else expectIssue(Issue.RESPONSE) { e.upload(a) }
                e.f.readRequests()
                e.f.preflightRequest(Fixture.ACCESS)
                assertEquals(if (revoked) WorkState.Quarantined else WorkState.Queued, e.state())
                assertEquals(7, server.web.requestCount)
            }
        }
    }

    @Test fun recognizedRejectionVersusUnknownErrorsAndNoTransportRetry() = runBlocking<Unit> {
        val cases = listOf(400 to "InvalidDraftBatch", 409 to "DraftVersionConflict",
            400 to "FutureError", 409 to "FutureError", 429 to "", 500 to "", 503 to "", 302 to "")
        for ((status, code) in cases) {
            TestServer().use { server ->
                val e = Exchange(server)
                val a = e.prepare()
                e.queueAttempt()
                if (code.isNotEmpty()) problem(server, status, code)
                else server.web.enqueue(MockResponse().setResponseCode(status)
                    .setHeader("Retry-After", "0").setHeader("Location", server.origin.resolve(e.uploadPath).toString()).setBody("{}"))
                val result = e.upload(a)
                val recognized = code in setOf("InvalidDraftBatch", "DraftVersionConflict")
                if (recognized) assertEquals(SampleUploadResult.Rejected(if (status == 400) WorkConflict.BatchInputRejected
                    else WorkConflict.DraftVersionConflict), result)
                else assertTrue(result is SampleUploadResult.OutcomeUnknown)
                e.consumeAttempt()
                assertEquals(if (recognized) WorkState.Conflict else WorkState.OutcomeUnknown, e.state())
                assertEquals(8, server.web.requestCount)
            }
        }
    }

    @Test fun postQuarantineRequiresExactContextAndAffectedOperations() = runBlocking<Unit> {
        for (matches in listOf(false, true)) {
            TestServer().use { server ->
                val e = Exchange(server)
                val a = e.prepare(dependent = true)
                e.queueAttempt()
                val ids = if (matches) listOf(e.operation.envelope.operation.operationId, testId(11)) else listOf(testId(11))
                val jsonIds = JsonArray(ids.map(::JsonPrimitive))
                problem(server, 409, "SourceSecurityRevoked", ""","preserveOriginals":true,"quarantineRequired":true,
                    "sourceContextIds":["${e.source.contextId}"],"operationIds":$jsonIds""")
                val result = e.upload(a)
                if (matches) assertEquals(SampleUploadResult.Rejected(WorkConflict.SourceSecurityRevoked), result)
                else assertTrue(result is SampleUploadResult.OutcomeUnknown)
                e.consumeAttempt()
                assertEquals(if (matches) WorkState.Quarantined else WorkState.OutcomeUnknown, e.state())
            }
        }
    }

    @Test fun malformedAndMismatchedSuccessResponsesNeverAccept() = runBlocking<Unit> {
        val cases = listOf("task", "line", "environment", "workflow", "batch", "hash", "fingerprint", "actor",
            "family", "outer", "null", "flags", "unknown", "duplicate", "body", "oversize", "oversize-error")
        for (name in cases) {
            TestServer().use { server ->
                val e = Exchange(server)
                val a = e.prepare()
                val good = e.receipt()
                val op = good.operations.single()
                val changed = when (name) {
                    "task" -> good.copy(taskId = testId(53))
                    "line" -> good.copy(operations = listOf(op.copy(lineId = Fixture.SECOND_LINE)))
                    "environment" -> good.copy(operations = listOf(op.copy(environmentId = "synthetic-other")))
                    "workflow" -> good.copy(operations = listOf(op.copy(workflowVersion = 1)))
                    "batch" -> good.copy(batchId = testId(53))
                    "hash" -> good.copy(operations = listOf(op.copy(contentHash = "F".repeat(64))))
                    "fingerprint" -> good.copy(requestFingerprint = "F".repeat(64))
                    "actor" -> e.receipt(actor = e.c)
                    "family" -> e.receipt(actor = e.f.work.a.copy(familyId = e.c.familyId))
                    else -> good
                }
                val wire = when (name) {
                    "outer" -> uploadResult(good, operations = listOf(op.copy(contentHash = "F".repeat(64))))
                    "null" -> uploadResult(null, operations = good.operations)
                    "flags" -> uploadResult(good).replace(""""canExecute":false""", """"canExecute":true""")
                    "unknown" -> uploadResult(good).replaceFirst("{", """{"unexpected":0,""")
                    "duplicate" -> uploadResult(good).replaceFirst("{", """{"replayOnly":false,""")
                    "body" -> "{"
                    "oversize" -> " ".repeat(512 * 1024) + uploadResult(good)
                    "oversize-error" -> " ".repeat(16 * 1024) +
                        """{"title":"DraftVersionConflict","code":"DraftVersionConflict","status":409}"""
                    else -> uploadResult(changed)
                }
                e.queueAttempt()
                if (name == "oversize-error") server.web.enqueue(MockResponse().setResponseCode(409)
                    .setHeader("Content-Type", "application/problem+json").setBody(wire))
                else server.enqueue(wire)
                assertTrue(name, e.upload(a) is SampleUploadResult.OutcomeUnknown)
                e.consumeAttempt()
                assertEquals(name, WorkState.OutcomeUnknown, e.state())
            }
        }
    }

    @Test fun tcpLossAndTimeoutAfterDeliveryRemainQueryOnly() = runBlocking<Unit> {
        for (disconnect in listOf(false, true)) {
            TestServer().use { server ->
                val e = Exchange(server)
                val a = e.prepare()
                e.queueAttempt()
                val response = success(uploadResult(e.receipt()))
                if (disconnect) response.setSocketPolicy(SocketPolicy.DISCONNECT_AFTER_REQUEST)
                else response.setBodyDelay(18, SECONDS)
                server.web.enqueue(response)
                assertEquals(SampleUploadResult.OutcomeUnknown(Issue.CONNECTION), e.upload(a))
                e.consumeAttempt()
                assertEquals(WorkState.OutcomeUnknown, e.state())
                assertEquals(8, server.web.requestCount)
            }
        }
    }

    @Test fun anotherCoordinatorDoesNotMistakeActiveInFlightForInterruptedWork() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = Exchange(server)
            val first = e.prepare()
            val sibling = e.queueSibling()
            val other = e.f.coordinator()
            e.queueRefresh()
            val another = other.open(e.f.selected())
            e.f.readRequests()
            e.queueAttempt()
            withHeldResponse(server, e.uploadPath, success(uploadResult(e.receipt()))) { release ->
                val pending = async(Dispatchers.IO) { e.upload(first) }
                e.consumeAttempt()
                assertEquals(WorkState.InFlight, e.state())
                expectWork(WorkProblem.InvalidState) { other.uploadBatch(another, sibling.batchId) }
                assertEquals(WorkState.InFlight, e.state())
                release()
                assertTrue(pending.await() is SampleUploadResult.Accepted)
            }
            assertEquals(WorkState.Queued, e.f.work.store.batch(e.f.work.access(), sibling.batchId)?.state)
            assertEquals(10, server.web.requestCount)
        }
    }

    @Test fun reopenedInFlightBlocksAnyNewBusinessPost() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = Exchange(server)
            val a = e.prepare()
            val next = e.queueSibling()
            e.f.work.store.startAttempt(e.f.work.access(), e.batch.batchId)
            e.queueRefresh()
            expectWork(WorkProblem.InvalidState) { e.coordinator.uploadBatch(a, next.batchId) }
            e.f.readRequests()
            assertEquals(WorkState.OutcomeUnknown, e.state())
            assertEquals(6, server.web.requestCount)
        }
    }

    @Test fun logoutBeforeAdmissionAndMissingContributionPermissionSendNothing() = runBlocking<Unit> {
        for (logout in listOf(false, true)) {
            TestServer().use { server ->
                val e = Exchange(server, canContribute = logout)
                val a = e.prepare()
                if (logout) e.f.work.sessions.logout(attemptRemote = false)
                expectIssue(if (logout) Issue.UNAUTHENTICATED else Issue.FORBIDDEN) { e.upload(a) }
                assertEquals(4, server.web.requestCount)
            }
        }
    }



    @Test fun lastNetworkHopRejectsRotationAfterRoomCommitAndBeforeHttpHeaders() = runBlocking<Unit> {
        val entered = CountDownLatch(1)
        val release = CountDownLatch(1)
        TestServer { request ->
            if (request.url.encodedPath.endsWith("/operation-batches")) {
                entered.countDown()
                check(release.await(10, SECONDS))
            }
        }.use { server ->
            val e = Exchange(server)
            val a = e.prepare()
            e.queueAttempt()
            e.f.work.stageRotation(server.clock.instant().plusSeconds(300))
            val pending = async(Dispatchers.IO) { runCatching { e.upload(a) } }
            try {
                assertTrue(entered.await(5, SECONDS))
                e.f.readRequests()
                e.f.preflightRequest(Fixture.ACCESS)
                assertEquals(WorkState.InFlight, e.state())
                e.f.work.offline.advance(300_000)
                e.f.work.sessions.read { _, _ -> Unit }
                server.request("/api/auth/refresh")
                server.request("/api/line-patrol/access/verify", bearer = "synthetic-access-rotated")
            } finally {
                release.countDown()
            }
            assertEquals(Issue.UNAUTHENTICATED, (pending.await().exceptionOrNull() as? PatrolFailure)?.issue)
            assertEquals(WorkState.OutcomeUnknown, e.state())
            assertEquals(9, server.web.requestCount)
        }
    }

    @Test fun interruptionLogoutSwitchAndRotationDuringResponsePreserveAttempt() = runBlocking<Unit> {
        for (race in listOf("cancel", "logout", "task", "line", "rotate", "other-user")) {
            TestServer().use { server ->
                val e = Exchange(server)
                val a = e.prepare()
                e.queueAttempt()
                if (race == "rotate") e.f.work.stageRotation(server.clock.instant().plusSeconds(300))
                if (race == "other-user") server.admit(Fixture.auth(user = e.f.work.b.userId, family = e.f.work.b.familyId,
                    access = "synthetic-other-session"), Fixture.receipt(e.f.work.b.userId, e.f.work.b.familyId))
                withHeldResponse(server, e.uploadPath, success(uploadResult(e.receipt()))) { release ->
                    val pending = async(Dispatchers.IO) { runCatching { e.upload(a) } }
                    e.consumeAttempt()
                    when (race) {
                        "cancel" -> pending.cancelAndJoin()
                        "logout" -> e.f.work.sessions.logout(attemptRemote = false)
                        "task", "line" -> e.coordinator.clearSelection()
                        "other-user" -> {
                            e.f.work.sessions.logout(attemptRemote = false)
                            e.f.work.sessions.login("synthetic-other", "test-only")
                            server.request("/api/auth/login")
                            server.request("/api/line-patrol/access/verify", bearer = "synthetic-other-session")
                        }
                        "rotate" -> {
                            e.f.work.offline.advance(300_000)
                            e.f.work.sessions.read { _, _ -> Unit }
                            server.request("/api/auth/refresh")
                            server.request("/api/line-patrol/access/verify", bearer = "synthetic-access-rotated")
                        }
                    }
                    release()
                    if (race != "cancel") assertTrue(pending.await().isFailure)
                }
                if (race in setOf("logout", "other-user")) e.f.login()
                if (race == "task") e.open(e.f.task.copy(taskId = testId(92)))
                if (race == "line") e.open(e.f.task.copy(lineId = Fixture.SECOND_LINE))
                assertEquals(WorkState.OutcomeUnknown, e.state())
            }
        }
    }

    @Test fun authDenialRevokesOfflineAdmissionWhileAttemptRemains() = runBlocking<Unit> {
        for (status in listOf(401, 403)) {
            TestServer().use { server ->
                val e = Exchange(server)
                val a = e.prepare()
                e.queueAttempt()
                server.enqueue("{}", status)
                expectIssue(if (status == 401) Issue.UNAUTHENTICATED else Issue.FORBIDDEN) { e.upload(a) }
                e.consumeAttempt()
                assertNull(e.f.work.sessions.resumeOffline())
                e.f.work.sessions.logout(attemptRemote = false)
                e.f.login()
                assertEquals(WorkState.OutcomeUnknown, e.state())
                assertEquals(10, server.web.requestCount)
            }
        }
    }

    @Test fun qualitativeCorrectionAndRemovalCarryTheExactAcceptedFrontierWithoutItsProof() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = Exchange(server)
            val a = e.prepare(qualitative = true)
            val accepted = e.receipt()
            e.queueAttempt()
            server.enqueue(uploadResult(accepted))
            assertEquals(SampleUploadResult.Accepted(accepted, false), e.upload(a))
            val first = e.consumeAttempt().getValue("operations").jsonArray.single().jsonObject
            assertEquals(JsonPrimitive("NG"), first.getValue("data").jsonObject["judgment"])
            assertEquals(JsonNull, first.getValue("data").jsonObject["inspectionValue"])

            val old = e.operation.envelope
            val access = e.f.work.access(version = 8)
            val freshSource = e.f.work.sourceFor(e.f.work.a, testId(75))
            e.f.work.store.storeSource(access, freshSource, e.f.proof)
            val correction = e.f.work.capture(11, freshSource).copy(kind = SampleKind.CorrectSample,
                baseDraftVersion = 8, data = SamplePayload("OK", null, null), dependencies = listOf(old.reference()))
            val middle = e.f.work.store.enqueue(access, correction)
            val removal = correction.copy(operationId = testId(12), kind = SampleKind.RemoveSample,
                data = null, dependencies = listOf(middle.envelope.reference()))
            val last = e.f.work.store.enqueue(access, removal)
            e.batch = e.f.work.store.prepareBatch(access, testId(24), listOf(correction.operationId, removal.operationId))
            e.f.work.store.recordSourceRevoked(access, old.source.contextId)
            e.source = freshSource

            val result = e.receipt(listOf(middle, last), references = accepted.operations)
            val grant = e.open(version = 8)
            e.queueAttempt(8)
            server.enqueue(uploadResult(result))
            assertEquals(SampleUploadResult.Accepted(result, false), e.upload(grant))
            val wire = e.consumeAttempt()
            val members = wire.getValue("operations").jsonArray.map { it.jsonObject }.associateBy { it.getValue("operationId").jsonPrimitive.content }
            assertEquals(JsonPrimitive("OK"), members.getValue(correction.operationId).getValue("data").jsonObject["judgment"])
            assertEquals(JsonNull, members.getValue(removal.operationId)["data"])
            assertEquals(JsonPrimitive(correction.operationId),
                members.getValue(removal.operationId).getValue("dependencies").jsonArray.single().jsonObject["operationId"])
            assertEquals(JsonPrimitive(old.contentHash), wire.getValue("acceptedReferences").jsonArray.single().jsonObject["contentHash"])
            assertEquals(JsonPrimitive(freshSource.contextId), wire.getValue("sourceProofs").jsonArray.single().jsonObject["workContextId"])
            assertEquals(correction.operationId, result.operations.single { it.isRemoved }.valueOperationId)
        }
    }

    private class Exchange(val server: TestServer, canContribute: Boolean = true) {
        val f = OnlineWorkFixture(server, canContribute)
        val coordinator = f.coordinator()
        val c = SessionIdentity(testId(77), testId(78), f.task.environmentId)
        val uploadPath = "${f.path}/operation-batches"
        val receiptPath get() = "$uploadPath/${batch.batchId}/receipt"
        lateinit var source: WorkSource
        lateinit var operation: WorkOperation
        lateinit var batch: BatchRecord
        var token = Fixture.ACCESS
        private var actor = f.work.a

        suspend fun prepare(issue: Boolean = false, number: String = "1.23", qualitative: Boolean = false,
                            dependent: Boolean = false): OnlineWorkCoordinator.Grant {
            f.login()
            val grant = open()
            val capture = if (issue) {
                server.enqueue(f.issued(), 201)
                coordinator.issueOrReuse(grant).also { f.issueRequest() }
            } else grant
            source = capture.source ?: f.work.source
            val access = if (issue) capture.access else f.work.access().also { f.work.store.storeSource(it, source, f.work.proof) }
            val value = if (qualitative) SamplePayload("NG", null, null) else SamplePayload(null, number, "fixture barcode")
            operation = f.work.store.enqueue(access, f.work.capture(context = source).copy(data = value))
            val ids = mutableListOf(operation.envelope.operation.operationId)
            if (dependent) {
                val correction = f.work.capture(11, source).copy(kind = SampleKind.CorrectSample,
                    dependencies = listOf(operation.envelope.reference()))
                ids += f.work.store.enqueue(access, correction).envelope.operation.operationId
            }
            batch = f.work.store.prepareBatch(access, testId(20), ids)
            return capture
        }

        suspend fun signInB(): OnlineWorkCoordinator.Grant {
            f.work.sessions.logout(attemptRemote = false)
            f.work.offline.advance(61_000)
            token = "synthetic-contributing-b"
            actor = f.work.b
            f.login(actor, token)
            return open()
        }

        fun draft(scope: WorkTask = f.task, version: Long = 7): String {
            val raw = Json.parseToJsonElement(f.draft(scope, version)).jsonObject
            val item = raw.getValue("currentAttempts").jsonArray.single().jsonObject
            return JsonObject(raw + ("items" to JsonArray(listOf(JsonObject(item + ("disposition" to JsonPrimitive("Applicable"))))))).toString()
        }

        suspend fun open(scope: WorkTask = f.task, version: Long = 7): OnlineWorkCoordinator.Grant {
            f.enqueueRead(actor, draft(scope, version), scope)
            return coordinator.open(f.selected(scope)).also { f.readRequests(token, scope) }
        }

        fun queueRefresh(version: Long = 7) = f.enqueueRead(actor, draft(version = version))
        fun queueAttempt(version: Long = 7) {
            queueRefresh(version)
            server.enqueue(f.preflight(source))
        }

        suspend fun upload(grant: OnlineWorkCoordinator.Grant) = coordinator.uploadBatch(grant, batch.batchId)
        suspend fun state() = f.work.store.batch(f.work.access(checked = listOf(source)), batch.batchId)?.state

        fun consumeAttempt(): JsonObject {
            f.readRequests(token)
            val preflight = f.preflightRequest(token)
            val sentSource = preflight.getValue("sources").jsonArray.single().jsonObject
            assertEquals(JsonPrimitive(source.contextId), sentSource["workContextId"])
            assertEquals(JsonPrimitive(source.contributorUserId), sentSource["contributorUserIdAssertion"])
            return Json.parseToJsonElement(server.request(uploadPath, "POST", token).body.readUtf8()).jsonObject
        }

        suspend fun queueSibling(): BatchRecord {
            val access = f.work.access()
            val next = f.work.capture(30, sample = testId(31))
            f.work.store.enqueue(access, next)
            return f.work.store.prepareBatch(access, testId(32), listOf(next.operationId))
        }

        fun receipt(operations: List<WorkOperation> = listOf(operation), actor: SessionIdentity = f.work.a,
                    references: List<ServerOperationReceipt> = emptyList()): ServerBatchReceipt {
            val raw = f.work.receipt(batch, operations, actor, references)
            val fresh = raw.operations.take(operations.size).zip(operations).map { (server, entry) ->
                val op = entry.envelope.operation
                val value = op.data?.let {
                    if (it.inspectionValue != null) ServerSampleValue("Quantitative", it.inspectionValue, "OK", it.barcode)
                    else ServerSampleValue("Qualitative", null, requireNotNull(it.judgment), null)
                }
                server.copy(value = value, isRemoved = op.kind == SampleKind.RemoveSample,
                    valueOperationId = if (op.kind == SampleKind.RemoveSample) op.dependencies.single().operationId else op.operationId)
            }
            return raw.copy(operations = (fresh + references).sortedBy { it.operationId })
        }
    }

    private fun uploadResult(receipt: ServerBatchReceipt?, replay: Boolean = false,
                             operations: List<ServerOperationReceipt> = requireNotNull(receipt).operations) =
        """{"batchReceipt":${receipt?.wire() ?: "null"},"operations":[${operations.joinToString(",") { it.wire() }}],
           "replayOnly":$replay,"canExecute":false,"acceptsFormalInspection":false}"""

    private fun success(body: String) = MockResponse().setHeader("Content-Type", "application/json").setBody(body)

    private fun problem(server: TestServer, status: Int, code: String, extra: String = "") {
        server.web.enqueue(MockResponse().setResponseCode(status).setHeader("Content-Type", "application/problem+json")
            .setBody("""{"status":$status,"title":"$code","code":"$code"$extra}"""))
    }
}
