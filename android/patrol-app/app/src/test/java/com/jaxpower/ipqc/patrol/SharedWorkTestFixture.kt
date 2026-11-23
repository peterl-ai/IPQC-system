package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import com.jaxpower.ipqc.patrol.offline.CacheCipher
import com.jaxpower.ipqc.patrol.offline.CacheRows
import com.jaxpower.ipqc.patrol.work.*
import java.time.Instant
import java.util.Base64
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.encodeToJsonElement
import kotlinx.serialization.json.jsonObject
import org.junit.Assert.assertEquals
import org.junit.Assert.fail

internal fun testId(i: Int) = "00000000-0000-0000-0000-" + i.toString().padStart(12, '0')

internal suspend fun expectWork(problem: WorkProblem, action: suspend () -> Any?) {
    try {
        action()
        fail("Expected " + problem.name)
    } catch (e: WorkFailure) {
        assertEquals(problem, e.problem)
    }
}

internal class SharedWorkTestFixture(val server: TestServer, private val canContribute: Boolean = false) {
    val a = SessionIdentity(Fixture.USER, Fixture.FAMILY, "synthetic-env")
    val b = SessionIdentity(Fixture.OTHER_USER, Fixture.OTHER_FAMILY, a.environment)
    val task = WorkTask(a.environment, Fixture.TASK, Fixture.FIRST_LINE, 2)
    val attempt = WorkAttempt(testId(4), testId(5))
    val source = sourceFor(a, testId(7))
    val proof = Base64.getUrlEncoder().withoutPadding().encodeToString(ByteArray(32) { 73 })
    private val rows = MemoryCacheRows()
    val offline = OfflineTestFixture(server)
    var sessions = offline.sessions
        private set
    val store get() = reopen()

    fun reopen(origin: String = server.origin.toString()) =
        SharedWorkStore(ProtectedWorkRows(rows, origin, TestWorkCipher), sessions)

    suspend fun rewriteFixture(block: suspend WorkTransaction.() -> Unit) {
        ProtectedWorkRows(rows, server.origin.toString(), TestWorkCipher).transaction(task, action = block)
    }

    fun sourceFor(identity: SessionIdentity, id: String, scope: WorkTask = task) =
        WorkSource(scope, id, identity.userId, identity.familyId, server.clock.instant().toString(),
            server.clock.instant().plusSeconds(60).toString(), Fixture.FAMILY_EXPIRY, 1)

    suspend fun login(identity: SessionIdentity = a, access: String = Fixture.ACCESS) {
        server.admit(auth(identity, access = access), receipt(identity))
        sessions.login("synthetic", "test-only")
    }

    fun stageRotation(at: Instant = server.clock.instant()) {
        server.admit(auth(a, "synthetic-access-rotated", "synthetic-refresh-rotated", at), receipt(a, at))
    }

    suspend fun rotate(restart: Boolean) {
        stageRotation()
        if (restart) {
            sessions = SessionRepository(server.api, server.vault, server.origin.toString(), server.clock, offline.reopen())
            requireNotNull(sessions.restore())
        } else {
            sessions.read { _, _ -> Unit }
        }
    }

    private fun auth(identity: SessionIdentity, access: String = Fixture.ACCESS, refresh: String = Fixture.REFRESH,
                     at: Instant = server.clock.instant()) =
        Fixture.auth(user = identity.userId, family = identity.familyId, access = access, refresh = refresh,
            expires = at.plusSeconds(300).toString()).replace(a.environment, identity.environment).let {
            if (canContribute) it.replace("\"LinePatrol.Read\"", "\"LinePatrol.Read\",\"LinePatrol.ContributeDraft\"") else it
        }

    private fun receipt(identity: SessionIdentity, at: Instant = server.clock.instant()) =
        Fixture.receipt(user = identity.userId, family = identity.familyId)
            .replace(a.environment, identity.environment)
            .replace(Fixture.NOW, at.toString())
            .replace("2026-11-22T14:00:00Z", at.plusSeconds(72 * 3600L).toString())

    suspend fun access(identity: WorkTask = task, version: Long = 7, checked: List<WorkSource> = listOf(source),
                       attempts: Set<WorkAttempt> = setOf(attempt), revoked: Set<String> = emptySet()): WorkAccess = WorkAccess(
        sessions.bindWorkSession(), identity, version, attempts,
        checked.map { ValidatedWorkSource(it.contextId, it.contributorUserId, it.captureUntilUtc) }.toSet(),
        revoked,
    )

    suspend fun preparedSource(): WorkAccess {
        login()
        val access = access()
        store.storeSource(access, source, proof)
        return access
    }

    fun capture(id: Int = 10, context: WorkSource = source, sample: String = testId(6)) = SampleOperation(
        testId(id), attempt, sample, context.contextId, 7, SampleKind.CaptureSample,
        SamplePayload(null, "1.2300", "fixture barcode"), emptyList(),
    )

    fun receipt(batch: BatchRecord, operations: List<WorkOperation>, uploader: SessionIdentity = b,
                references: List<ServerOperationReceipt> = emptyList()): ServerBatchReceipt {
        val at = Fixture.NOW
        val version = batch.expectedDraftVersion + 1
        val outcomes = operations.map {
            val op = it.envelope.operation
            ServerOperationReceipt(task.taskId, op.operationId, batch.batchId, 1, it.envelope.contentHash, op.kind,
                op.attempt.taskItemId, op.attempt.inspectionAttemptId, testId(90), op.clientSampleId, op.sourceWorkContextId,
                it.envelope.source.contributorUserId, uploader.userId, uploader.familyId, task.environmentId,
                task.lineId, task.workflowVersion, at, version, ServerSampleValue("Quantitative", "1.23", "OK", op.data?.barcode),
                false, op.operationId)
        }
        return ServerBatchReceipt(task.taskId, batch.batchId, batch.fingerprint, uploader.userId, uploader.familyId,
            at, batch.expectedDraftVersion, version, outcomes + references)
    }

    private class MemoryCacheRows : CacheRows {
        private val lock = Mutex()
        private val records = mutableMapOf<String, ByteArray>()

        override suspend fun <T> transaction(block: suspend () -> T): T = lock.withLock {
            val before = records.toMap()
            try {
                block()
            } catch (e: Throwable) {
                records.clear()
                records.putAll(before)
                throw e
            }
        }

        override suspend fun read(slot: String) = records[slot]?.copyOf()
        override suspend fun count() = records.size
        override suspend fun legacyWorkSlots() = records.keys.filter { it.startsWith("work-v1:") }
        override suspend fun replace(slot: String, sealed: ByteArray) { records[slot] = sealed.copyOf() }
    }

    private object TestWorkCipher : CacheCipher {
        override fun seal(slot: String, plaintext: ByteArray, allowNewKey: Boolean) = plaintext.copyOf()
        override fun open(slot: String, sealed: ByteArray) = sealed.copyOf()
    }
}

internal fun ServerBatchReceipt.wire(): String {
    val json = Json.encodeToJsonElement(this).jsonObject
    return JsonObject(json + ("operations" to JsonArray(operations.map { it.wireObject() }))).toString()
}

internal fun ServerOperationReceipt.wire() = wireObject().toString()

private fun ServerOperationReceipt.wireObject(): JsonObject {
    val json = Json.encodeToJsonElement(this).jsonObject
    val decimal = value?.inspectionValue?.toBigDecimal() ?: return json
    val fields = json.getValue("value").jsonObject
    return JsonObject(json + ("value" to JsonObject(fields + ("inspectionValue" to JsonPrimitive(decimal)))))
}
