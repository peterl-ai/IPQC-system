package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.LineTaskSummary
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.api.ProductionLine
import com.jaxpower.ipqc.patrol.api.PatrolApi
import com.jaxpower.ipqc.patrol.api.patrolRetrofit
import com.jaxpower.ipqc.patrol.api.patrolHttpClient
import com.jaxpower.ipqc.patrol.auth.CredentialVault
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import com.jaxpower.ipqc.patrol.auth.StoredRefresh
import com.jaxpower.ipqc.patrol.tasks.PatrolRepository
import com.jaxpower.ipqc.patrol.work.WorkContextApi
import java.net.InetAddress
import java.net.Proxy
import java.time.Clock
import java.time.Instant
import java.time.ZoneId
import java.time.ZoneOffset
import java.util.concurrent.TimeUnit
import okhttp3.OkHttpClient
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.mockwebserver.RecordedRequest
import okhttp3.tls.HandshakeCertificates
import okhttp3.tls.HeldCertificate
import org.junit.Assert.assertEquals
import org.junit.Assert.fail

internal class MemoryVault(var saved: StoredRefresh? = null) : CredentialVault {
    override fun read() = saved
    override fun replace(record: StoredRefresh) { saved = record }
    override fun clear() { saved = null }
}

internal class TestClock(var current: Instant = Instant.parse(Fixture.NOW)) : Clock() {
    override fun getZone(): ZoneId = ZoneOffset.UTC
    override fun withZone(zone: ZoneId): Clock = fixed(current, zone)
    override fun instant() = current
}

internal class TestServer : AutoCloseable {
    val web = MockWebServer()
    val clock = TestClock()
    val vault = MemoryVault()
    val api: PatrolApi
    val workApi: WorkContextApi
    val origin: okhttp3.HttpUrl
    val session: SessionRepository
    val patrol: PatrolRepository

    init {
        val loopbackHost = "127.0.0.1"
        val cert = HeldCertificate.Builder().addSubjectAlternativeName(loopbackHost).build()
        val serviceTls = HandshakeCertificates.Builder().heldCertificate(cert).build()
        val clientTls = HandshakeCertificates.Builder().addTrustedCertificate(cert.certificate).build()
        web.useHttps(serviceTls.sslSocketFactory(), false)
        // Bind an explicit IP so the TLS fixture is independent of host DNS and proxies.
        web.start(InetAddress.getByName(loopbackHost), 0)
        origin = web.url("/").newBuilder().host(loopbackHost).build()
        val retrofit = patrolRetrofit(origin, patrolHttpClient(OkHttpClient.Builder().proxy(Proxy.NO_PROXY)
            .sslSocketFactory(clientTls.sslSocketFactory(), clientTls.trustManager)))
        api = retrofit.create(PatrolApi::class.java)
        workApi = retrofit.create(WorkContextApi::class.java)
        session = newRepository()
        patrol = PatrolRepository(api, session)
    }

    fun newRepository(storage: CredentialVault = vault) =
        SessionRepository(api, storage, origin.toString(), clock)

    fun enqueue(body: String, status: Int = 200) {
        web.enqueue(MockResponse().setResponseCode(status).setHeader("Content-Type", "application/json").setBody(body))
    }

    fun admit(auth: String = Fixture.auth(), receipt: String = Fixture.receipt()) {
        enqueue(auth)
        enqueue(receipt)
    }

    suspend fun login() {
        admit()
        session.login("fixture-ipqa", "fixture-password")
    }

    fun request(path: String, verb: String = "POST", bearer: String? = null): RecordedRequest {
        val request = requireNotNull(web.takeRequest(4, TimeUnit.SECONDS)) { "Missing request for $path" }
        assertEquals(path, request.path)
        assertEquals(verb, request.method)
        assertEquals(bearer?.let { "Bearer $it" }, request.getHeader("Authorization"))
        return request
    }

    override fun close() { web.shutdown() }
}

internal suspend fun expectIssue(issue: Issue, action: suspend () -> Any?) {
    try {
        action()
        fail("Expected $issue")
    } catch (e: PatrolFailure) {
        assertEquals(issue, e.issue)
    }
}

internal object Fixture {
    const val USER = "10000000-0000-0000-0000-000000000001"
    const val OTHER_USER = "10000000-0000-0000-0000-000000000002"
    const val FAMILY = "20000000-0000-0000-0000-000000000001"
    const val OTHER_FAMILY = "20000000-0000-0000-0000-000000000002"
    const val FIRST_LINE = "30000000-0000-0000-0000-000000000001"
    const val SECOND_LINE = "30000000-0000-0000-0000-000000000002"
    const val TASK = "40000000-0000-0000-0000-000000000001"
    const val NOW = "2026-11-19T14:00:00Z"
    const val VALID_ACCESS = "2026-11-19T14:05:00Z"
    const val FAMILY_EXPIRY = "2026-11-26T14:00:00Z"
    const val ACCESS = "synthetic-access-a"
    const val REFRESH = "synthetic-refresh-a"
    val line = ProductionLine(FIRST_LINE, "CELL-A", "Assembly East", 1)
    val secondLine = ProductionLine(SECOND_LINE, "CELL-B", "Assembly West", 2)

    fun auth(role: String = "IPQA", active: Boolean = true, forced: Boolean = false,
             access: String = ACCESS, refresh: String = REFRESH, expires: String = VALID_ACCESS,
             user: String = USER, family: String = FAMILY, permission: Boolean = true): String {
        val perms = if (permission && !forced && role == "IPQA") """["LinePatrol.Read"]""" else "[]"
        return """
            {"accessToken":"$access","accessExpiresAtUtc":"$expires","refreshToken":"$refresh",
             "personalSessionId":"$family","personalSessionEnvironmentId":"synthetic-env",
             "personalSessionExpiresAtUtc":"$FAMILY_EXPIRY",
             "user":{"id":"$user","username":"fixture-ipqa","displayName":"Fixture Inspector",
              "role":"$role","isActive":$active,"mustChangePassword":$forced,"permissions":$perms}}
        """.trimIndent()
    }

    fun receipt(user: String = USER, family: String = FAMILY): String = """
        {"version":1,"userId":"$user","personalSessionId":"$family","environmentId":"synthetic-env",
         "verifiedAtUtc":"$NOW","offlineAccessUntilUtc":"2026-11-22T14:00:00Z",
         "personalSessionExpiresAtUtc":"$FAMILY_EXPIRY"}
    """.trimIndent()

    val directory = """
        {"items":[{"id":"$SECOND_LINE","code":"CELL-B","name":"Assembly West","selectionOrder":2},
                  {"id":"$FIRST_LINE","code":"CELL-A","name":"Assembly East","selectionOrder":1}]}
    """.trimIndent()

    fun summary(lineId: String = FIRST_LINE, workflow: Int = 2, executable: Boolean = false,
                user: String = USER, status: String = "FoundationOnly") = """
        {"id":"$TASK","taskNo":"FIXTURE-01","lineId":"$lineId","workflowVersion":$workflow,
         "status":"PendingInspection","scheduledOccurrenceUtc":"$NOW","generatedAtUtc":"$NOW","currentRevisionNo":0,
         "planNoSnapshot":"SYNTHETIC-PLAN","planNameSnapshot":"Fixture plan","standardNameSnapshot":"Fixture standard",
         "lineNameSnapshot":"Assembly East","readFoundationStatus":"$status","canExecute":$executable,
         "validatedAccess":{"userId":"$user","personalSessionId":"$FAMILY","environmentId":"synthetic-env",
          "validatedAtUtc":"$NOW","taskId":"$TASK","lineId":"$lineId","workflowVersion":$workflow},
         "planningSnapshotVersion":1,"planningSnapshotStatus":"ValidV1"}
    """.trimIndent()

    fun page(items: String = summary(), page: Int = 1, total: Int = 41) =
        """{"items":[$items],"page":$page,"pageSize":20,"total":$total}"""

    val selectedTask: LineTaskSummary = kotlinx.serialization.json.Json.decodeFromString(summary())

    fun detail(summary: String = summary(), executable: Boolean = false) = """
        {"summary":$summary,"patrolPlanId":"50000000-0000-0000-0000-000000000001",
         "patrolStandardId":"50000000-0000-0000-0000-000000000002",
         "sourceStandardUpdatedAtUtc":"$NOW","canExecute":$executable,
         "items":[{"id":"60000000-0000-0000-0000-000000000001",
            "sourcePatrolStandardItemId":"60000000-0000-0000-0000-000000000002",
            "sequenceNo":1,"processCode":"FI","processName":"Inspection","inspectionItemCategory":"Visual",
            "inspectionItem":"Fixture measurement","inspectionContent":"Check the fixture",
            "upperLimitOperator":"<=","upperLimitValue":"2","lowerLimitOperator":">=","lowerLimitValue":"1",
            "inspectionType":"Quantitative","samplingPlan":"Per round","sampleCount":"2","photoRequirement":"Raw fixture text",
            "defectLevel":"Minor"}]}
    """.trimIndent()
}
