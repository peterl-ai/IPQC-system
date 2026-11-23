package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.work.*
import java.util.concurrent.TimeUnit.SECONDS
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.encodeToJsonElement
import kotlinx.serialization.json.jsonObject
import okhttp3.mockwebserver.MockResponse
import org.junit.Assert.*
import org.junit.Test

class ReceiptRecoveryTest {
    private val mismatch = ReceiptRecovery.Conflict(ReceiptConflictReason.ResponseMismatch)
    private val missing = ReceiptRecovery.Unresolved(ReceiptUncertainty.NotObserved)

    @Test fun bUsesCurrentBearerAfterLogoutAndPersistsTheActualActorsAndExactReceipt() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = Exchange(server)
            val a = e.prepare()
            val recorded = e.operation.envelope
            val reader = e.f.work.b
            val token = "synthetic-reader-b"
            val uploader = SessionIdentity(testId(71), testId(72), e.f.task.environmentId)
            val receipt = e.f.work.receipt(e.batch, listOf(e.operation), uploader)
            e.f.work.sessions.logout(attemptRemote = false)
            assertNull(server.vault.saved)
            e.f.login(reader, token)
            assertEquals(ReceiptRecovery.AuthenticationFailure(Issue.UNAUTHENTICATED),
                e.coordinator.recoverBatchReceipt(a, e.batch.batchId))
            val b = e.open(reader, token, version = 12)
            assertTrue(e.f.work.store.operations(b.access).isEmpty())
            server.enqueue(receipt.wire())
            assertEquals(ReceiptRecovery.BatchConfirmed(receipt), e.recoverBatch(b, token))
            val stored = requireNotNull(e.f.work.reopen().operation(b.access, recorded.operation.operationId))
            assertEquals(WorkState.Accepted, stored.state)
            assertEquals(recorded.workspaceId, stored.envelope.workspaceId)
            assertEquals(recorded.canonicalPayload, stored.envelope.canonicalPayload)
            assertEquals(recorded.contentHash, stored.envelope.contentHash)
            assertEquals(recorded.source, stored.envelope.source)
            assertEquals(e.f.work.a.userId, stored.envelope.source.contributorUserId)
            assertEquals(e.f.work.a.familyId, stored.envelope.source.originPersonalSessionId)
            val saved = requireNotNull(e.f.work.reopen().batch(b.access, e.batch.batchId)?.receipt)
            assertEquals(uploader.userId, saved.uploadedByUserId)
            assertEquals(uploader.familyId, saved.uploadedPersonalSessionId)
            assertEquals(8L, saved.draftVersion)
            assertEquals(Fixture.NOW, saved.acceptedAtUtc)
            assertEquals(9, server.web.requestCount)
        }
    }

    @Test fun batchesRejectWrongIdentityManifestScopeAndValuesWithoutPartialAcceptance() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = Exchange(server)
            val grant = e.prepare()
            val r = e.receipt
            val op = r.operations.single()
            val batches = listOf(
                r.copy(taskId = testId(46)), r.copy(batchId = testId(47)),
                r.copy(requestFingerprint = "F".repeat(64)), r.copy(previousDraftVersion = 6),
                r.copy(draftVersion = 9), r.copy(acceptedAtUtc = "yesterday"),
                r.copy(uploadedByUserId = testId(48)), r.copy(uploadedPersonalSessionId = testId(49)),
                r.copy(operations = emptyList()), r.copy(operations = listOf(op, op)),
                r.copy(operations = listOf(op, op.copy(operationId = testId(99)))),
            )
            val operations = listOf(
                op.copy(taskId = testId(46)), op.copy(lineId = Fixture.SECOND_LINE),
                op.copy(environmentId = "synthetic-other-env"), op.copy(workflowVersion = 1),
                op.copy(batchId = testId(47)), op.copy(canonicalizationVersion = 2),
                op.copy(contentHash = "F".repeat(64)), op.copy(taskItemId = testId(50)),
                op.copy(inspectionAttemptId = testId(51)), op.copy(clientSampleId = testId(52)),
                op.copy(sampleId = "not-a-uuid"), op.copy(sourceWorkContextId = testId(53)),
                op.copy(contributorUserId = e.f.work.b.userId), op.copy(draftVersion = 9),
                op.copy(acceptedAtUtc = "2026-11-19T13:00:00Z"), op.copy(valueOperationId = testId(50)),
                op.copy(isRemoved = true), op.copy(value = null),
                op.copy(value = op.value!!.copy(inspectionValue = "8.5")),
                op.copy(value = op.value.copy(judgment = "Pending")),
                op.copy(value = op.value.copy(barcode = "wrong")),
            )
            for (bad in batches + operations.map { r.copy(operations = listOf(it)) }) {
                server.enqueue(bad.wire())
                assertEquals(mismatch, e.recoverBatch(grant))
                assertEquals(WorkState.OutcomeUnknown, e.state(grant))
            }
            server.enqueue(r.wire())
            assertEquals(ReceiptRecovery.BatchConfirmed(r), e.recoverBatch(grant))
            server.enqueue(r.copy(operations = listOf(op.copy(sampleId = testId(98)))).wire())
            assertEquals(mismatch, e.recoverBatch(grant))
            assertEquals(r, e.f.work.store.batch(grant.access, e.batch.batchId)?.receipt)
            assertEquals(4 + batches.size + operations.size + 2, server.web.requestCount)
        }
    }

    @Test fun operationReceiptDoesNotSetBatchStateOrReleaseBarrierEvenWhenAcceptedByAnotherBatch() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = Exchange(server)
            val grant = e.prepare()
            val op = e.receipt.operations.single().copy(batchId = testId(61))
            server.enqueue(op.wire())
            assertEquals(ReceiptRecovery.OperationConfirmed(op), e.recoverOperation(grant))
            server.enqueue(op.wire())
            assertEquals(ReceiptRecovery.OperationConfirmed(op), e.recoverOperation(grant))
            server.enqueue(e.receipt.wire())
            assertEquals(mismatch, e.recoverBatch(grant))
            server.enqueue(op.copy(contentHash = "A".repeat(64)).wire())
            assertEquals(mismatch, e.recoverOperation(grant))
            server.enqueue("{}", 404)
            assertEquals(missing, e.recoverBatch(grant))
            val next = e.f.work.capture(64, sample = testId(65))
            e.f.work.store.enqueue(e.access, next)
            val queued = e.f.work.store.prepareBatch(e.access, testId(66), listOf(next.operationId))
            expectWork(WorkProblem.InvalidState) { e.f.work.store.startAttempt(e.access, queued.batchId) }
            assertEquals(WorkState.OutcomeUnknown, e.state(grant))
            assertEquals(9, server.web.requestCount)
        }
    }

    @Test fun correctionRequiresItsVerifiedPredecessorAndWholeBatchMustMatchSampleIdentity() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = Exchange(server)
            val grant = e.prepare(withCorrection = true)
            val receipts = e.receipt.operations
            val first = receipts[0]
            val correction = receipts[1]
            server.enqueue(correction.wire())
            assertEquals(ReceiptRecovery.Unresolved(ReceiptUncertainty.MissingPredecessor),
                e.recoverOperation(grant, id = correction.operationId))
            server.enqueue(first.wire())
            assertEquals(ReceiptRecovery.OperationConfirmed(first), e.recoverOperation(grant))
            server.enqueue(correction.copy(sampleId = testId(68)).wire())
            assertEquals(mismatch, e.recoverOperation(grant, id = correction.operationId))
            server.enqueue(correction.wire())
            assertEquals(ReceiptRecovery.OperationConfirmed(correction), e.recoverOperation(grant, id = correction.operationId))
            assertEquals(WorkState.OutcomeUnknown, e.state(grant))
            server.enqueue(e.receipt.wire())
            assertEquals(ReceiptRecovery.BatchConfirmed(e.receipt), e.recoverBatch(grant))
        }
    }

    @Test fun externalPredecessorMustKeepItsEarlierReceiptAndItsValueOperationOnRemoval() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = Exchange(server)
            val grant = e.prepare()
            val first = e.receipt.operations.single()
            server.enqueue(e.receipt.wire())
            assertEquals(ReceiptRecovery.BatchConfirmed(e.receipt), e.recoverBatch(grant))

            val removal = e.f.work.capture(13).copy(baseDraftVersion = 8, kind = SampleKind.RemoveSample,
                data = null, dependencies = listOf(e.operation.envelope.reference()))
            val later = e.f.work.access(version = 8)
            val queued = e.f.work.store.enqueue(later, removal)
            e.batch = e.f.work.store.prepareBatch(later, testId(24), listOf(removal.operationId))
            e.f.work.store.startAttempt(later, e.batch.batchId)
            e.f.work.store.recoverInterrupted(later)
            val fresh = e.open(version = 9)
            val raw = e.f.work.receipt(e.batch, listOf(queued), uploader = e.f.work.a)
            val removed = raw.operations.single().copy(value = null, isRemoved = true, valueOperationId = first.operationId)
            val expected = raw.copy(operations = listOf(first, removed))
            server.enqueue(expected.copy(operations = listOf(first.copy(uploadedByUserId = e.f.work.a.userId), removed)).wire())
            assertEquals(mismatch, e.recoverBatch(fresh))
            server.enqueue(expected.copy(operations = listOf(first, removed.copy(valueOperationId = removal.operationId))).wire())
            assertEquals(mismatch, e.recoverBatch(fresh))
            assertEquals(WorkState.OutcomeUnknown, e.state(fresh))
            server.enqueue(expected.wire().replace("1.23", "1.2300"))
            assertEquals(ReceiptRecovery.BatchConfirmed(expected), e.recoverBatch(fresh))
        }
    }

    @Test fun errorsAndRepeatedNotFoundCannotResendOrChangeTheLedger() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = Exchange(server)
            val grant = e.prepare()
            repeat(2) {
                server.enqueue("{}", 404)
                assertEquals(missing, e.recoverBatch(grant))
                server.enqueue("{}", 404)
                assertEquals(missing, e.recoverOperation(grant))
            }
            server.enqueue("{}", 409)
            assertEquals(ReceiptRecovery.Conflict(ReceiptConflictReason.QueryConflict), e.recoverBatch(grant))
            for ((status, issue) in listOf(429 to Issue.RATE_LIMITED, 500 to Issue.UNAVAILABLE)) {
                server.enqueue("{}", status)
                assertEquals(ReceiptRecovery.TransientFailure(issue), e.recoverBatch(grant))
            }
            server.web.enqueue(MockResponse().setBody(e.receipt.wire()).setBodyDelay(18, SECONDS))
            assertEquals(ReceiptRecovery.TransientFailure(Issue.CONNECTION), e.recoverBatch(grant))
            assertEquals(WorkState.OutcomeUnknown, e.state(grant))
            assertEquals(12, server.web.requestCount)
        }
    }

    @Test fun rejectsMalformedUnknownDuplicatePolymorphicAndNonNumericWireFields() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = Exchange(server)
            val grant = e.prepare()
            val wire = e.receipt.wire()
            for (body in listOf(
                "{", "[]", wire + "{}", wire.replaceFirst("{", """{"receiptVersion":2,"""),
                wire.replaceFirst("{", """{"taskId":"old","""),
                wire.replaceFirst("{", """{"task\u0049d":"old","""),
                wire.replace("1.23", """"1.23""""),
                wire.replace("1.23", "1e100"),
                wire.replace(""""draftVersion":8""", """"draftVersion":"8""""),
                wire.replace(""""isRemoved":false""", """"isRemoved":"false""""),
                wire.replace(""""judgment":"OK"""", """"judgment":true"""),
                " ".repeat(512 * 1024) + wire,
            )) {
                server.enqueue(body)
                assertEquals(ReceiptRecovery.TransientFailure(Issue.RESPONSE), e.recoverBatch(grant))
            }
            server.enqueue("""{"kind":"RemoveItem","receiptVersion":2}""")
            assertEquals(ReceiptRecovery.TransientFailure(Issue.RESPONSE), e.recoverOperation(grant))
            assertEquals(WorkState.OutcomeUnknown, e.state(grant))
        }
    }

    @Test fun authenticationDenialUsesExistingSessionRevocationAndLeavesTheLedgerUnchanged() = runBlocking<Unit> {
        for ((status, issue) in listOf(401 to Issue.UNAUTHENTICATED, 403 to Issue.FORBIDDEN)) {
            TestServer().use { server ->
                val e = Exchange(server)
                val grant = e.prepare()
                server.enqueue("{}", status)
                assertEquals(ReceiptRecovery.AuthenticationFailure(issue), e.recoverBatch(grant))
                assertNull(e.f.work.sessions.resumeOffline())
                expectWork(WorkProblem.StaleGrant) { e.coordinator.recoverBatchReceipt(grant, e.batch.batchId) }
                e.f.work.sessions.logout(attemptRemote = false)
                e.f.login()
                assertEquals(WorkState.OutcomeUnknown, e.state(e.open()))
                assertEquals(9, server.web.requestCount)
            }
        }
    }

    @Test fun logoutAndTokenRotationDuringResponseCannotAccept() = runBlocking<Unit> {
        for (rotate in listOf(false, true)) {
            TestServer().use { server ->
                val e = Exchange(server)
                val grant = e.prepare()
                val token = if (rotate) "synthetic-access-rotated" else Fixture.ACCESS
                if (rotate) {
                    e.f.work.stageRotation(server.clock.instant().plusSeconds(300))
                }
                withHeldResponse(server, e.batchPath, MockResponse().setBody(e.receipt.wire())) { release ->
                    val waiting = async(Dispatchers.IO) { e.coordinator.recoverBatchReceipt(grant, e.batch.batchId) }
                    server.request(e.batchPath, "GET", Fixture.ACCESS)
                    if (rotate) {
                        e.f.work.offline.advance(300_000)
                        e.f.work.sessions.read { _, _ -> Unit }
                        server.request("/api/auth/refresh")
                        server.request("/api/line-patrol/access/verify", bearer = token)
                    } else {
                        e.f.work.sessions.logout(attemptRemote = false)
                    }
                    release()
                    assertEquals(ReceiptRecovery.AuthenticationFailure(Issue.UNAUTHENTICATED), waiting.await())
                }
                if (!rotate) e.f.login()
                assertEquals(WorkState.OutcomeUnknown, e.state(e.open(token = token)))
                assertEquals(9, server.web.requestCount)
            }
        }
    }

    @Test fun selectionSwitchDropsLateReceiptAndAnotherScopeCannotQueryOldIdentities() = runBlocking<Unit> {
        TestServer().use { server ->
            val e = Exchange(server)
            val grant = e.prepare()
            withHeldResponse(server, e.batchPath, MockResponse().setBody(e.receipt.wire())) { release ->
                val waiting = async(Dispatchers.IO) {
                    runCatching { e.coordinator.recoverBatchReceipt(grant, e.batch.batchId) }
                }
                server.request(e.batchPath, "GET", Fixture.ACCESS)
                e.coordinator.clearSelection()
                release()
                assertEquals(WorkProblem.StaleGrant, (waiting.await().exceptionOrNull() as? WorkFailure)?.problem)
            }
            val other = e.open(scope = e.f.task.copy(taskId = testId(73), lineId = Fixture.SECOND_LINE))
            assertEquals(ReceiptRecovery.Unresolved(ReceiptUncertainty.NoLocalAttempt),
                e.coordinator.recoverBatchReceipt(other, e.batch.batchId))
            assertEquals(ReceiptRecovery.Unresolved(ReceiptUncertainty.NoLocalAttempt),
                e.coordinator.recoverOperationReceipt(other, e.operation.envelope.operation.operationId))
            assertEquals(WorkState.OutcomeUnknown, e.state(e.open()))
            assertEquals(9, server.web.requestCount)
        }
    }

    private class Exchange(val server: TestServer) {
        val f = OnlineWorkFixture(server)
        val coordinator = f.coordinator()
        lateinit var access: WorkAccess
        lateinit var operation: WorkOperation
        lateinit var batch: BatchRecord
        lateinit var receipt: ServerBatchReceipt
        val batchPath get() = "${f.path}/operation-batches/${batch.batchId}/receipt"

        suspend fun prepare(withCorrection: Boolean = false): OnlineWorkCoordinator.Grant {
            access = f.work.preparedSource()
            server.request("/api/auth/login")
            server.request("/api/line-patrol/access/verify", bearer = Fixture.ACCESS)
            operation = f.work.store.enqueue(access, f.work.capture())
            val operations = mutableListOf(operation)
            if (withCorrection) {
                val correction = f.work.capture(11).copy(kind = SampleKind.CorrectSample,
                    dependencies = listOf(operation.envelope.reference()))
                operations += f.work.store.enqueue(access, correction)
            }
            batch = f.work.store.prepareBatch(access, testId(20), operations.map { it.envelope.operation.operationId })
            f.work.store.startAttempt(access, batch.batchId)
            f.work.store.recoverInterrupted(access)
            receipt = f.work.receipt(batch, operations)
            return open()
        }

        suspend fun open(identity: SessionIdentity = f.work.a, token: String = Fixture.ACCESS,
                         scope: WorkTask = f.task, version: Long = 7): OnlineWorkCoordinator.Grant {
            f.enqueueRead(identity, body = f.draft(scope, version), scope = scope)
            return coordinator.open(f.selected(scope)).also { f.readRequests(token, scope) }
        }

        suspend fun state(grant: OnlineWorkCoordinator.Grant) = f.work.store.batch(grant.access, batch.batchId)?.state

        suspend fun recoverBatch(grant: OnlineWorkCoordinator.Grant, token: String = Fixture.ACCESS): ReceiptRecovery =
            coordinator.recoverBatchReceipt(grant, batch.batchId).also {
                assertEquals(0L, server.request(batchPath, "GET", token).bodySize)
            }

        suspend fun recoverOperation(grant: OnlineWorkCoordinator.Grant,
                                     id: String = operation.envelope.operation.operationId): ReceiptRecovery =
            coordinator.recoverOperationReceipt(grant, id).also {
                assertEquals(0L, server.request("${f.path}/operations/$id/receipt", "GET", Fixture.ACCESS).bodySize)
            }
    }

    private fun ServerBatchReceipt.wire(): String {
        val json = Json.encodeToJsonElement(this).jsonObject
        return JsonObject(json + ("operations" to JsonArray(operations.map { it.wireObject() }))).toString()
    }

    private fun ServerOperationReceipt.wire() = wireObject().toString()

    private fun ServerOperationReceipt.wireObject(): JsonObject {
        val json = Json.encodeToJsonElement(this).jsonObject
        val decimal = value?.inspectionValue?.toBigDecimal() ?: return json
        val fields = json.getValue("value").jsonObject
        return JsonObject(json + ("value" to JsonObject(fields + ("inspectionValue" to JsonPrimitive(decimal)))))
    }
}
