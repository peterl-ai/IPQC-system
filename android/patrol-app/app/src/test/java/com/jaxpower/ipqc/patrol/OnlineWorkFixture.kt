package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.work.OnlineWorkCoordinator
import com.jaxpower.ipqc.patrol.work.WorkTask
import java.time.Instant
import java.util.Base64
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonObject

internal class OnlineWorkFixture(val server: TestServer) {
    val work = SharedWorkTestFixture(server)
    val task = work.task
    val attempt = work.attempt
    val id = testId(80)
    val path = "/api/line-patrol/tasks/${task.taskId}"
    val proof = Base64.getUrlEncoder().withoutPadding().encodeToString(ByteArray(32) { 11 })

    fun coordinator() = OnlineWorkCoordinator(server.origin.toString(), server.patrol, server.workApi, work.sessions, work.store)

    suspend fun login(identity: SessionIdentity = work.a, token: String = Fixture.ACCESS) {
        work.login(identity, token)
        server.request("/api/auth/login")
        server.request("/api/line-patrol/access/verify", bearer = token)
    }

    fun selected(scope: WorkTask = task) = Fixture.selectedTask.copy(id = scope.taskId, lineId = scope.lineId)

    fun detail(identity: SessionIdentity = work.a, scope: WorkTask = task) =
        Fixture.detail(Fixture.summary(lineId = scope.lineId, user = identity.userId)
            .replace(Fixture.TASK, scope.taskId).replace(Fixture.FAMILY, identity.familyId))
            .replace("60000000-0000-0000-0000-000000000001", attempt.taskItemId)

    fun draft(scope: WorkTask = task, version: Long = 7,
              availability: String = "SharedDraftInitialized", pairs: String = pair, samples: String = "[]"): String =
        """{"taskId":"${scope.taskId}","lineId":"${scope.lineId}","workflowVersion":2,"draftVersion":$version,
            "availability":"$availability","currentAttempts":$pairs,"samples":$samples,"canExecute":false,
            "completeness":"NotEvaluatedForFormalSubmission","allItemsRemoved":false}""".trimIndent()

    val pair = """[{"taskItemId":"${attempt.taskItemId}","inspectionAttemptId":"${attempt.inspectionAttemptId}","minimumSampleCount":2}]"""

    fun enqueueRead(identity: SessionIdentity = work.a, body: String = draft(), scope: WorkTask = task) {
        server.enqueue(detail(identity, scope))
        server.enqueue(body)
    }

    fun readRequests(token: String = Fixture.ACCESS, scope: WorkTask = task) {
        val root = "/api/line-patrol/tasks/${scope.taskId}"
        server.request("$root?expectedTaskLineId=${scope.lineId}", "GET", token)
        server.request("$root/shared-draft", "GET", token)
    }

    fun context(identity: SessionIdentity = work.a, sourceId: String = id,
                issued: String = Fixture.NOW, until: String = "2026-11-19T14:01:00Z",
                scope: WorkTask = task, revoked: Boolean = false): String {
        val at = if (revoked) "\"2026-11-19T14:00:05Z\"" else "null"
        val reason = if (revoked) "\"synthetic security action\"" else "null"
        val actor = if (revoked) "\"${testId(1)}\"" else "null"
        return """{"id":"$sourceId","contractVersion":1,"environmentId":"${scope.environmentId}","userId":"${identity.userId}",
            "issuedPersonalSessionId":"${identity.familyId}","issuedAuthVersion":4,"taskId":"${scope.taskId}","lineId":"${scope.lineId}",
            "workflowVersion":2,"issuedAtUtc":"$issued","captureUntilUtc":"$until","personalSessionExpiresAtUtc":"${Fixture.FAMILY_EXPIRY}",
            "securityRevokedAtUtc":$at,"securityRevocationReason":$reason,"securityRevokedByUserId":$actor,
            "revision":${if (revoked) 2 else 1}}""".trimIndent()
    }

    fun issued(metadata: String = context(), identity: SessionIdentity = work.a,
               proved: String = proof, verified: String = Fixture.NOW): String {
        val receipt = Fixture.receipt(identity.userId, identity.familyId)
            .replace(Fixture.NOW, verified)
            .replace("2026-11-22T14:00:00Z", Instant.parse(verified).plusSeconds(72 * 3600).toString())
        return """{"context":$metadata,"proofSecret":"$proved","offlineAccessReceipt":$receipt,"canExecute":false}"""
    }

    fun preflight(source: com.jaxpower.ipqc.patrol.work.WorkSource, accepted: Boolean = false) =
        """{"validatedAtUtc":"${server.clock.instant()}","sources":[{"workContextId":"${source.contextId}",
            "contributorUserId":"${source.contributorUserId}","captureUntilUtc":"${source.captureUntilUtc}"}],
            "purpose":"SourcePreflightOnly","acceptsWork":$accepted}""".trimIndent()

    fun issueRequest(token: String = Fixture.ACCESS): JsonObject = Json.parseToJsonElement(
        server.request("$path/work-contexts", bearer = token).body.readUtf8()).jsonObject

    fun preflightRequest(token: String): JsonObject = Json.parseToJsonElement(
        server.request("$path/work-contexts/validate-sources", bearer = token).body.readUtf8()).jsonObject

    fun expectedScope() = mapOf("expectedTaskLineId" to JsonPrimitive(task.lineId),
        "expectedWorkflowVersion" to JsonPrimitive(task.workflowVersion), "expectedEnvironmentId" to JsonPrimitive(task.environmentId))
}
