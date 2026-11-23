package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.work.*
import java.time.Instant
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import org.junit.Assert.*
import org.junit.Test

class OfflineSampleCoordinatorTest {
    @Test fun typedCaptureCorrectionRemovalAndExactRetryKeepImmutableBytes() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = SampleFixture(server, type = "visual", count = "", minimum = 1)
            e.arm()
            val before = server.web.requestCount
            val edits = e.coordinator.offlineSamples
            val draft = edits.draft(e.task)
            assertEquals(1, draft.progress.single().required)
            assertFalse(draft.progress.single().meetsCount)
            val capture = edits.newCapture(e.f.attempt, ok)
            val a = edits.save(e.task, capture)
            val retry = edits.save(e.task, capture)
            assertEquals(a.envelope.contentHash, retry.envelope.contentHash)
            assertEquals(capture.key.clientSampleId, a.envelope.operation.clientSampleId)
            assertEquals(1L, retry.envelope.originLocalEditVersion)
            assertEquals("OK", a.envelope.operation.data?.judgment)
            assertNull(a.envelope.operation.data?.inspectionValue)
            assertTrue(edits.draft(e.task).progress.single().meetsCount)
            expectWork(WorkProblem.OperationIdentityConflict) { edits.save(e.task, capture.copy(input = ng)) }

            val prior = a.envelope.reference()
            expectWork(WorkProblem.DependencyMismatch) {
                edits.save(e.task, OfflineSampleEdit.Correct(capture.key, ng, prior.copy(contentHash = "F".repeat(64))))
            }
            expectWork(WorkProblem.DependencyMismatch) {
                edits.save(e.task, OfflineSampleEdit.Correct(capture.key.copy(clientSampleId = testId(59)), ng, prior))
            }
            expectWork(WorkProblem.ScopeMismatch) {
                val other = capture.key.copy(attempt = e.f.attempt.copy(inspectionAttemptId = testId(98)))
                edits.save(e.task, OfflineSampleEdit.Correct(other, ng, prior))
            }
            val b = edits.save(e.task, OfflineSampleEdit.Correct(capture.key, ng, prior))
            assertNotEquals(a.envelope.operation.operationId, b.envelope.operation.operationId)
            assertEquals("NG", b.envelope.operation.data?.judgment)
            assertEquals(listOf(prior), b.envelope.operation.dependencies)
            expectWork(WorkProblem.DependencyMismatch) { edits.save(e.task, OfflineSampleEdit.Remove(capture.key, prior)) }
            val c = edits.save(e.task, OfflineSampleEdit.Remove(capture.key, b.envelope.reference()))
            assertNull(c.envelope.operation.data)
            expectWork(WorkProblem.DependencyMismatch) {
                edits.save(e.task, OfflineSampleEdit.Correct(capture.key, ok, c.envelope.reference()))
            }
            val version = edits.draft(e.task)
            assertEquals(7L, version.observedServerDraftVersion)
            assertEquals(3L, version.localEditVersion)
            assertTrue(version.effective.single().removed)
            assertNull(version.effective.single().value)
            assertEquals(0, version.progress.single().applicableSamples)
            assertEquals(listOf("OK", "NG", null), version.history.map { it.operation.envelope.operation.data?.judgment })
            assertEquals(a.envelope.canonicalPayload, edits.savedOperation(e.task, capture.operationId)?.envelope?.canonicalPayload)
            assertEquals(before, server.web.requestCount)
        }
    }

    @Test fun exactDecimalAndCapacityUseFrozenCountAndActualSampleIds() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = SampleFixture(server, type = "numeric", count = "50", minimum = 50)
            e.arm()
            val edits = e.coordinator.offlineSamples
            val max = "79228162514264337593543950335"
            val capture = edits.newCapture(e.f.attempt, SampleInput.Quantitative(max, "x".repeat(200)))
            val saved = edits.save(e.task, capture)
            assertEquals(max, saved.envelope.operation.data?.inspectionValue)
            assertNull(saved.envelope.operation.data?.judgment)
            expectWork(WorkProblem.SampleInputMismatch) { edits.save(e.task, edits.newCapture(e.f.attempt, ok)) }
            for (bad in listOf("1e-29", "79228162514264337593543950336", "2.5,0", "NaN")) {
                expectWork(WorkProblem.SampleInputMismatch) {
                    edits.save(e.task, edits.newCapture(e.f.attempt, SampleInput.Quantitative(bad)))
                }
            }
            val tooLong = SampleInput.Quantitative("1.25", "x".repeat(201))
            expectWork(WorkProblem.SampleInputMismatch) { edits.save(e.task, edits.newCapture(e.f.attempt, tooLong)) }
            repeat(49) { edits.save(e.task, edits.newCapture(e.f.attempt, SampleInput.Quantitative("1.2300"))) }
            val full = edits.draft(e.task)
            assertEquals(50, full.progress.single().applicableSamples)
            assertEquals(50, full.progress.single().required)
            assertEquals(50, full.effective.distinctBy { it.key }.size)
            assertEquals("1.23", full.effective.last().value?.inspectionValue)
            expectWork(WorkProblem.SampleLimit) {
                edits.save(e.task, edits.newCapture(e.f.attempt, SampleInput.Quantitative("1e-28")))
            }
            edits.save(e.task, OfflineSampleEdit.Remove(capture.key, saved.envelope.reference()))
            assertTrue(edits.draft(e.task).history.size > 50)
            assertFalse(edits.draft(e.task).progress.single().meetsCount)
            edits.save(e.task, edits.newCapture(e.f.attempt, SampleInput.Quantitative("1e-28")))
            assertTrue(edits.draft(e.task).progress.single().meetsCount)
        }
    }

    @Test fun processRestartRebootAndExpiredBearerKeepOriginalWindowAndReplay() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = SampleFixture(server)
            e.arm(until = "2026-11-19T14:10:00Z")
            val edits = e.coordinator.offlineSamples
            repeat(2) { edits.save(e.task, edits.newCapture(e.f.attempt, number)) }
            val before = server.web.requestCount
            e.f.work.offline.advance(301_000)
            expectIssue(Issue.UNAUTHENTICATED) { e.f.work.sessions.bindWorkSession() }
            edits.save(e.task, edits.newCapture(e.f.attempt, number))
            e.f.work.restartOffline()
            val reopened = e.f.coordinator().offlineSamples
            assertEquals(3, reopened.draft(e.task).progress.single().applicableSamples)
            e.f.work.offline.rebootAt(360_000)
            assertEquals(3, reopened.draft(e.task).history.size)
            e.f.work.offline.advance(239_999)
            val replay = reopened.newCapture(e.f.attempt, number)
            val saved = reopened.save(e.task, replay)
            e.f.work.offline.advance(1)
            expectWork(WorkProblem.CaptureWindowClosed) { reopened.save(e.task, reopened.newCapture(e.f.attempt, number)) }
            assertEquals(saved.envelope.contentHash, reopened.save(e.task, replay).envelope.contentHash)
            e.f.work.offline.advance(1)
            expectWork(WorkProblem.CaptureWindowClosed) { reopened.save(e.task, reopened.newCapture(e.f.attempt, number)) }
            assertNotNull(reopened.savedOperation(e.task, replay.operationId))
            assertEquals(before, server.web.requestCount)
        }
    }

    @Test fun invalidAndExpiredA02ReceiptLockRetainedLedger() = runBlocking<Unit> {
        for (anomaly in listOf("expired", "invalid", "rollback", "logout", "corrupt")) {
            TestServer().use { server ->
                val e = SampleFixture(server)
                e.arm(until = "2026-11-22T14:00:00Z")
                val edits = e.coordinator.offlineSamples
                val capture = edits.newCapture(e.f.attempt, number)
                val saved = edits.save(e.task, capture)
                when (anomaly) {
                    "expired" -> e.f.work.offline.advance(72L * 3600 * 1000)
                    "invalid" -> e.f.work.offline.invalidateOfflineClock()
                    "rollback" -> {
                        e.f.work.offline.advance(300_000)
                        edits.draft(e.task)
                        e.f.work.offline.rollbackWallClock(120_000)
                    }
                    "logout" -> {
                        e.f.work.sessions.logout(attemptRemote = false)
                        e.f.work.offline.rebootAt(100)
                    }
                    "corrupt" -> e.f.work.offline.receipt.replace(byteArrayOf(1, 2, 3))
                }
                e.f.work.restartOffline()
                val reopened = e.f.coordinator().offlineSamples
                val issue = when (anomaly) {
                    "invalid", "rollback" -> Issue.OFFLINE_CLOCK
                    "corrupt" -> Issue.CACHE_STORAGE
                    "expired" -> Issue.OFFLINE_EXPIRED
                    else -> Issue.UNAUTHENTICATED
                }
                expectIssue(issue) { reopened.draft(e.task) }
                e.f.work.rewriteFixture {
                    assertEquals(saved.envelope.contentHash, operation(capture.operationId)?.envelope?.contentHash)
                }
            }
        }
    }

    @Test fun freshAttemptAndDispositionInvalidateOlderPendingWithoutDeletingHistory() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = SampleFixture(server)
            e.arm()
            val edits = e.coordinator.offlineSamples
            val capture = edits.newCapture(e.f.attempt, number)
            val saved = edits.save(e.task, capture)
            val newAttempt = e.f.attempt.copy(inspectionAttemptId = testId(99))
            e.queue(version = 8, attempt = newAttempt)
            e.coordinator.open(e.f.selected())
            e.f.readRequests()
            val draft = edits.draft(e.task)
            assertEquals(8L, draft.observedServerDraftVersion)
            assertEquals(newAttempt, draft.progress.single().attempt)
            assertTrue(draft.effective.isEmpty())
            assertFalse(draft.history.single().currentAttempt)
            assertEquals(saved.envelope.contentHash, draft.history.single().operation.envelope.contentHash)
            expectWork(WorkProblem.ScopeMismatch) { edits.save(e.task, edits.newCapture(e.f.attempt, number)) }
            expectWork(WorkProblem.ScopeMismatch) {
                edits.prepareBatch(e.task, testId(73), listOf(capture.operationId))
            }
            server.enqueue(e.f.detail().replace("FoundationOnly", "HistoricalInspectionData"))
            expectIssue(Issue.UNSUPPORTED_TASK) { e.coordinator.open(e.f.selected()) }
            server.request(e.f.path + "?expectedTaskLineId=" + e.task.lineId, "GET", Fixture.ACCESS)
            expectWork(WorkProblem.ScopeMismatch) { edits.save(e.task, edits.newCapture(newAttempt, number)) }
            assertTrue(edits.draft(e.task).progress.isEmpty())
            e.queue(version = 9, attempt = newAttempt, disposition = "UnableToMeasureNG")
            e.coordinator.open(e.f.selected())
            e.f.readRequests()
            expectWork(WorkProblem.ScopeMismatch) { edits.save(e.task, edits.newCapture(newAttempt, number)) }
            assertEquals(saved.envelope.contentHash, edits.savedOperation(e.task, capture.operationId)?.envelope?.contentHash)
        }
    }

    @Test fun onlyExplicitArmCanGrantLocalCaptureAndBatchKeepsExactDependencies() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = SampleFixture(server)
            e.f.login()
            e.queue()
            val grant = e.coordinator.open(e.f.selected())
            e.f.readRequests()
            val edits = e.coordinator.offlineSamples
            val x = edits.newCapture(e.f.attempt, number)
            expectWork(WorkProblem.SourceUnavailable) { edits.save(e.task, x) }
            server.enqueue(e.f.issued(), 201)
            val issued = e.coordinator.issueOrReuse(grant)
            e.f.issueRequest()
            expectWork(WorkProblem.SourceUnavailable) { edits.save(e.task, x) }
            e.queue()
            e.coordinator.armOfflineSamples(issued)
            e.f.readRequests()
            val a = edits.save(e.task, x)
            val y = OfflineSampleEdit.Correct(x.key, SampleInput.Quantitative("1.25"), a.envelope.reference())
            val b = edits.save(e.task, y)
            val id = testId(71)
            expectWork(WorkProblem.DependencyMismatch) { edits.prepareBatch(e.task, id, listOf(y.operationId)) }
            val ids = listOf(y.operationId, x.operationId)
            val batch = edits.prepareBatch(e.task, id, ids)
            assertEquals(emptyList<OperationReference>(), batch.acceptedReferences)
            assertEquals(batch, edits.prepareBatch(e.task, id, ids.reversed()))
            assertEquals("1.25", edits.draft(e.task).effective.single().value?.inspectionValue)
            expectWork(WorkProblem.BatchIdentityConflict) { edits.prepareBatch(e.task, id, listOf(x.operationId)) }
            expectWork(WorkProblem.DependencyMismatch) {
                edits.save(e.task, OfflineSampleEdit.Remove(x.key, b.envelope.reference()))
            }
            val sibling = edits.save(e.task, edits.newCapture(e.f.attempt, number))
            val conflict = edits.newCapture(e.f.attempt, number)
            edits.save(e.task, conflict)
            val otherBatch = edits.prepareBatch(e.task, testId(73), listOf(conflict.operationId))
            val online = e.f.work.access(checked = listOf(requireNotNull(issued.source)))
            e.f.work.store.startAttempt(online, id)
            expectWork(WorkProblem.InvalidState) {
                edits.prepareBatch(e.task, testId(72), listOf(sibling.envelope.operation.operationId))
            }
            e.f.work.store.recordOutcomeUnknown(online, id)
            assertEquals(WorkState.OutcomeUnknown, edits.draft(e.task).history.first().operation.state)
            expectWork(WorkProblem.InvalidState) {
                edits.prepareBatch(e.task, testId(72), listOf(sibling.envelope.operation.operationId))
            }
            expectWork(WorkProblem.InvalidState) {
                e.f.work.store.recordRejection(online, id, WorkConflict.BatchInputRejected)
            }
            e.f.work.store.recordRejection(online, otherBatch.batchId, WorkConflict.BatchInputRejected)
            e.f.work.store.quarantineUnbatched(online, sibling.envelope.operation.operationId, WorkConflict.SourceSecurityRevoked)
            val projection = edits.draft(e.task)
            assertEquals(setOf(WorkState.OutcomeUnknown, WorkState.Conflict, WorkState.Quarantined),
                projection.history.map { it.operation.state }.toSet())
            assertEquals(WorkState.OutcomeUnknown, projection.effective.single().status)
            assertEquals(7, server.web.requestCount)
        }
    }

    @Test fun bUsesSeparateScopeAndCanExplicitlyReconcileAsUploaderWithoutReauthoringA() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = SampleFixture(server)
            e.arm()
            val edits = e.coordinator.offlineSamples
            val command = edits.newCapture(e.f.attempt, number)
            val a = edits.save(e.task, command)
            val batch = edits.prepareBatch(e.task, testId(75), listOf(command.operationId))
            e.f.work.offline.advance(60_000)
            expectWork(WorkProblem.CaptureWindowClosed) { edits.save(e.task, edits.newCapture(e.f.attempt, number)) }
            e.f.work.sessions.logout(attemptRemote = false)
            val b = e.f.work.b
            val token = "test-user-b"
            e.f.login(b, token)
            expectWork(WorkProblem.SourceUnavailable) { edits.savedOperation(e.task, command.operationId) }
            val whenB = server.clock.instant()
            val grantB = e.arm(identity = b, token = token, sourceId = testId(77),
                issued = whenB, until = whenB.plusSeconds(60).toString(), login = false)
            assertNull(edits.savedOperation(e.task, command.operationId))
            expectWork(WorkProblem.OperationIdentityConflict) { edits.save(e.task, command) }
            val own = edits.save(e.task, edits.newCapture(e.f.attempt, number))
            assertEquals(b.userId, own.envelope.source.contributorUserId)
            assertEquals(b.familyId, own.envelope.source.originPersonalSessionId)
            assertEquals(testId(77), own.envelope.source.contextId)
            assertEquals(e.f.work.a.userId, a.envelope.source.contributorUserId)

            server.enqueue(e.f.preflight(a.envelope.source))
            val preflight = e.coordinator.preflight(grantB, command.operationId)
            e.f.preflightRequest(token)
            val beforeUpload = requireNotNull(e.f.work.store.operation(preflight.access, command.operationId))
            assertEquals(a.envelope.contentHash, beforeUpload.envelope.contentHash)
            val receipt = e.f.work.receipt(batch, listOf(a), b)
            val result = """{"batchReceipt":${receipt.wire()},"operations":[${receipt.operations.single().wire()}],
                "replayOnly":false,"canExecute":false,"acceptsFormalInspection":false}"""
            e.queue(identity = b)
            server.enqueue(e.f.preflight(a.envelope.source))
            server.enqueue(result)
            assertEquals(SampleUploadResult.Accepted(receipt, false), e.coordinator.uploadBatch(preflight, batch.batchId))
            e.f.readRequests(token)
            e.f.preflightRequest(token)
            val sent = kotlinx.serialization.json.Json.parseToJsonElement(
                server.request("${e.f.path}/operation-batches", bearer = token).body.readUtf8()).jsonObject
            val original = sent.getValue("operations").jsonArray.single().jsonObject
            assertEquals(JsonPrimitive(e.f.work.a.userId), original["contributorUserIdAssertion"])
            assertEquals(JsonPrimitive(a.envelope.source.contextId), original["sourceWorkContextId"])
            val accepted = edits.draft(e.task).history.single { it.operation.state == WorkState.Accepted }
            assertEquals(b.familyId, accepted.acceptedReceipt?.uploadedPersonalSessionId)
            assertEquals(a.envelope.contentHash, accepted.operation.envelope.contentHash)
            assertEquals(7L, edits.draft(e.task).observedServerDraftVersion)
            expectWork(WorkProblem.StaleGrant) { edits.save(e.task, edits.newCapture(e.f.attempt, number)) }
            val confirmed = receipt.operations.single()
            val snapshot = e.observed(confirmed.sampleId, confirmed.clientSampleId, confirmed.operationId,
                confirmed.valueOperationId, confirmed.contributorUserId)
            e.queue(identity = b, version = 8, samples = "[$snapshot]")
            e.coordinator.open(e.f.selected())
            e.f.readRequests(token)
            val current = edits.draft(e.task)
            assertEquals(8L, current.observedServerDraftVersion)
            assertEquals(a.envelope.reference(), current.effective.single().editablePredecessor)
            val bFix = OfflineSampleEdit.Correct(command.key, SampleInput.Quantitative("1.26"), a.envelope.reference())
            val corrected = edits.save(e.task, bFix)
            val prepared = edits.prepareBatch(e.task, testId(76), listOf(bFix.operationId))
            assertEquals(listOf(a.envelope.reference()), prepared.acceptedReferences)
            assertEquals(8L, prepared.expectedDraftVersion)
            assertEquals(b.userId, corrected.envelope.source.contributorUserId)
        }
    }

    @Test fun switchingConfiguredLinesCannotRelabelOperationsOrReuseAnId() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = SampleFixture(server)
            e.arm()
            val edits = e.coordinator.offlineSamples
            val command = edits.newCapture(e.f.attempt, number)
            val a = edits.save(e.task, command)
            val second = e.task.copy(taskId = testId(44), lineId = Fixture.SECOND_LINE)
            expectWork(WorkProblem.SourceUnavailable) { edits.save(second, command) }
            e.arm(scope = second, sourceId = testId(78), login = false)
            expectWork(WorkProblem.OperationIdentityConflict) { edits.save(second, command) }
            val other = edits.save(second, edits.newCapture(e.f.attempt, number))
            assertEquals(e.task, a.envelope.source.task)
            assertEquals(second, other.envelope.source.task)
            assertEquals(1L, edits.draft(second).localEditVersion)
            assertEquals(1L, edits.draft(e.task).localEditVersion)
            expectWork(WorkProblem.ScopeMismatch) { edits.draft(e.task.copy(environmentId = "different-test-env")) }
        }
    }

    @Test fun observedPermissionLossSurvivesRestartEvenWithUnchangedReceiptTimestamp() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = SampleFixture(server)
            e.arm()
            val edits = e.coordinator.offlineSamples
            val command = edits.newCapture(e.f.attempt, number)
            val saved = edits.save(e.task, command)
            e.f.work.rotate(restart = true, contribution = false)
            server.request("/api/auth/refresh")
            server.request("/api/line-patrol/access/verify", bearer = "synthetic-access-rotated")
            assertNotNull(e.f.work.sessions.resumeOffline(e.f.work.a))
            e.f.work.restartOffline()
            val restarted = e.f.coordinator().offlineSamples
            expectIssue(Issue.UNAUTHENTICATED) { restarted.save(e.task, restarted.newCapture(e.f.attempt, number)) }
            e.f.work.rewriteFixture {
                assertEquals(saved.envelope.contentHash, operation(command.operationId)?.envelope?.contentHash)
            }
        }
    }

    @Test fun remoteCapacityAndUnverifiedPredecessorStayServerConfirmed() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = SampleFixture(server)
            e.arm()
            val records = (101..150).joinToString(",", "[", "]") { id ->
                e.observed(testId(id), testId(id + 60), testId(id + 120), testId(id + 180), e.f.work.a.userId, id)
            }
            e.queue(version = 8, samples = records)
            e.coordinator.open(e.f.selected())
            e.f.readRequests()
            val edits = e.coordinator.offlineSamples
            val projection = edits.draft(e.task)
            assertEquals(50, projection.confirmed.size)
            assertEquals(50, projection.progress.single().applicableSamples)
            assertTrue(projection.effective.all { it.status == WorkState.Accepted && it.editablePredecessor == null })
            expectWork(WorkProblem.SampleLimit) { edits.save(e.task, edits.newCapture(e.f.attempt, number)) }
            val first = projection.effective.first()
            expectWork(WorkProblem.DependencyMismatch) {
                edits.save(e.task, OfflineSampleEdit.Correct(first.key, number,
                    OperationReference(first.lastOperationId, "A".repeat(64))))
            }
        }
    }

    @Test fun verificationRefreshCannotExtendOldCaptureCapAndRevocationPreservesExactReplay() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = SampleFixture(server)
            val grant = e.arm()
            val edits = e.coordinator.offlineSamples
            val command = edits.newCapture(e.f.attempt, number)
            val a = edits.save(e.task, command)
            e.f.work.store.recordSourceRevoked(grant.access, e.f.id)
            expectWork(WorkProblem.SourceUnavailable) { edits.save(e.task, edits.newCapture(e.f.attempt, number)) }
            assertEquals(a.envelope.contentHash, edits.save(e.task, command).envelope.contentHash)
        }
        TestServer().use { server ->
            val e = SampleFixture(server)
            e.arm()
            val edits = e.coordinator.offlineSamples
            val command = edits.newCapture(e.f.attempt, number)
            val a = edits.save(e.task, command)
            e.f.work.offline.advance(60_000)
            e.f.work.rotate(restart = true)
            server.request("/api/auth/refresh")
            server.request("/api/line-patrol/access/verify", bearer = "synthetic-access-rotated")
            val restored = e.f.coordinator().offlineSamples
            assertEquals(a.envelope.contentHash, restored.save(e.task, command).envelope.contentHash)
            expectWork(WorkProblem.CaptureWindowClosed) {
                restored.save(e.task, restored.newCapture(e.f.attempt, number))
            }
        }
    }

    private class SampleFixture(val server: TestServer, val type: String = "Quantitative",
                                val count: String = "2", val minimum: Int = 2) {
        val f = OnlineWorkFixture(server, canContribute = true)
        val task = f.task
        val coordinator = f.coordinator()

        fun queue(identity: SessionIdentity = f.work.a, version: Long = 7, attempt: WorkAttempt = f.attempt,
                  disposition: String = "Applicable", scope: WorkTask = task, samples: String = "[]") {
            val pair = """{"taskItemId":"${attempt.taskItemId}","inspectionAttemptId":"${attempt.inspectionAttemptId}",
                "minimumSampleCount":$minimum}""".trimIndent()
            val item = pair.dropLast(1) + ""","disposition":"$disposition"}"""
            server.enqueue(f.detail(identity, scope).replace(""""inspectionType":"Quantitative"""", """"inspectionType":"$type"""")
                .replace(""""sampleCount":"2"""", """"sampleCount":"$count""""))
            val body = f.draft(scope, version, pairs = "[$pair]", samples = samples).dropLast(1) + ""","items":[$item]}"""
            server.enqueue(body)
        }

        fun observed(sampleId: String, clientId: String, lastId: String, valueId: String, user: String, seq: Int = 1) =
            """{"sampleId":"$sampleId","taskItemId":"${f.attempt.taskItemId}","inspectionAttemptId":"${f.attempt.inspectionAttemptId}",
                "clientSampleId":"$clientId","sequenceNo":$seq,"value":{"inspectionType":"Quantitative","inspectionValue":1.23,
                "judgment":"OK","barcode":null},"isRemoved":false,"valueOperationId":"$valueId","valueContributorUserId":"$user",
                "lastOperationId":"$lastId","isCurrentApplicable":true}"""

        suspend fun arm(identity: SessionIdentity = f.work.a, token: String = Fixture.ACCESS,
                        sourceId: String = f.id, issued: Instant = Instant.parse(Fixture.NOW), scope: WorkTask = task,
                        until: String = issued.plusSeconds(60).toString(), login: Boolean = true): OnlineWorkCoordinator.Grant {
            if (login) f.login(identity, token)
            queue(identity, scope = scope)
            val first = coordinator.open(f.selected(scope))
            f.readRequests(token, scope)
            val meta = f.context(identity, sourceId, issued.toString(), until, scope)
            server.enqueue(f.issued(meta, identity, verified = issued.toString()), 201)
            val grant = coordinator.issueOrReuse(first)
            server.request("/api/line-patrol/tasks/${scope.taskId}/work-contexts", bearer = token)
            queue(identity, scope = scope)
            coordinator.armOfflineSamples(grant)
            f.readRequests(token, scope)
            return grant
        }
    }

    private companion object {
        val ok = SampleInput.Qualitative(QualitativeJudgment.OK)
        val ng = SampleInput.Qualitative(QualitativeJudgment.NG)
        val number = SampleInput.Quantitative("1.23")
    }
}
