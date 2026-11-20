package com.jaxpower.ipqc.patrol.api

import java.io.IOException
import java.time.Instant
import java.time.format.DateTimeParseException
import java.util.concurrent.TimeUnit
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import okhttp3.CookieJar
import okhttp3.HttpUrl
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.RequestBody
import okio.BufferedSink
import retrofit2.Response
import retrofit2.Retrofit
import retrofit2.converter.kotlinx.serialization.asConverterFactory
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.Header
import retrofit2.http.POST
import retrofit2.http.Path
import retrofit2.http.Query

enum class Issue(val message: String) {
    CONFIGURATION("A valid IPQC HTTPS server must be configured."),
    LINE_CONFIGURATION("Two valid production lines must be configured on the server."),
    OFFLINE("Network unavailable. Connect to the plant network to use Patrol."),
    OFFLINE_EXPIRED("Offline access has expired. Connect to IPQC and verify your session."),
    OFFLINE_CLOCK("Offline access is locked. Check the tablet clock and verify your session online."),
    CACHE_STORAGE("Protected Patrol data is unavailable. Try online again or contact your administrator."),
    TASK_DETAIL_CHANGED("Task detail changed. Return to the task list and reload it."),
    CACHE_DETAIL_MISSING("The matching frozen task detail was not downloaded. Open it online first."),
    CONNECTION("The IPQC server could not be reached. Check the network and HTTPS configuration."),
    UNAVAILABLE("IPQC is temporarily unavailable. Try again."),
    RATE_LIMITED("Too many requests. Wait before trying again."),
    UNAUTHENTICATED("Your session expired. Sign in again."),
    INVALID_LOGIN("Sign-in failed. Check your credentials or contact your administrator."),
    FORBIDDEN("This account does not have active IPQA patrol access."),
    CHANGE_REJECTED("The server rejected the password change. Check the current password and password rules."),
    REAUTHENTICATE("The session change could not be confirmed. Sign in again online."),
    STORAGE("Secure credential storage is unavailable. Sign in after it becomes available."),
    LINE_MISMATCH("The task belongs to a different server line. Refresh your selection and task list."),
    UNSUPPORTED_TASK("This task cannot currently be opened for inspection."),
    NOT_FOUND("The server could not find this line or task. Refresh and try again."),
    CONFLICT("The server reported a conflict. Refresh and try again."),
    BAD_REQUEST("The IPQC server rejected the request."),
    RESPONSE("The IPQC server returned an unexpected response."),
}

class PatrolFailure(
    val issue: Issue,
    val canUseOffline: Boolean = issue == Issue.CONNECTION || issue == Issue.UNAVAILABLE,
) : Exception(issue.message)

fun ensureResponse(valid: Boolean) {
    if (!valid) throw PatrolFailure(Issue.RESPONSE)
}

private val guidPattern = Regex("[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}")
fun validGuid(value: String) = guidPattern.matches(value) && value != "00000000-0000-0000-0000-000000000000"

fun utc(value: String): Instant = try {
    ensureResponse(value.endsWith("Z"))
    Instant.parse(value)
} catch (_: DateTimeParseException) {
    throw PatrolFailure(Issue.RESPONSE)
}

fun httpsOrigin(value: String): HttpUrl? {
    val trimmed = value.trim()
    if (!trimmed.startsWith("https://", ignoreCase = true) || '@' in trimmed ||
        '\\' in trimmed || trimmed.any { it.isWhitespace() || it.isISOControl() }) return null
    return trimmed.toHttpUrlOrNull()?.takeIf {
        it.isHttps && it.username.isEmpty() && it.password.isEmpty() &&
            it.query == null && it.fragment == null && it.encodedPath == "/"
    }
}

interface PatrolApi {
    @POST("api/auth/login")
    suspend fun login(@Body input: NativeLogin): Response<NativeAuth>

    @POST("api/auth/refresh")
    suspend fun refresh(@Body input: NativeRefresh): Response<NativeAuth>

    @POST("api/auth/logout")
    suspend fun logout(@Header("Authorization") bearer: String, @Body input: NativeRefresh): Response<Unit>

    @POST("api/auth/change-password")
    suspend fun changePassword(@Header("Authorization") bearer: String, @Body input: PasswordChange): Response<Unit>

    @POST("api/line-patrol/access/verify")
    suspend fun verify(@Header("Authorization") bearer: String): Response<OfflineAccessReceipt>

    @GET("api/line-patrol/lines")
    suspend fun lines(@Header("Authorization") bearer: String): Response<LineDirectory>

    @GET("api/line-patrol/tasks")
    suspend fun tasks(@Header("Authorization") bearer: String, @Query("lineId") lineId: String,
                      @Query("page") page: Int, @Query("pageSize") pageSize: Int): Response<PagedLineTasks>

    @GET("api/line-patrol/tasks/{taskId}")
    suspend fun detail(@Header("Authorization") bearer: String, @Path("taskId") taskId: String,
                       @Query("expectedTaskLineId") lineId: String): Response<LineTaskDetail>
}

private val wireJson = Json { ignoreUnknownKeys = true }

fun patrolHttpClient(builder: OkHttpClient.Builder = OkHttpClient.Builder()): OkHttpClient = builder
    .cookieJar(CookieJar.NO_COOKIES)
    .retryOnConnectionFailure(false)
    .followRedirects(false)
    .followSslRedirects(false)
    .connectTimeout(8, TimeUnit.SECONDS)
    .readTimeout(15, TimeUnit.SECONDS)
    .writeTimeout(10, TimeUnit.SECONDS)
    .callTimeout(22, TimeUnit.SECONDS)
    .addInterceptor { chain ->
        val request = chain.request()
        // OkHttp may follow some HTTP retry responses independently of transport retry.
        // Mark payloads one-shot so rotating credentials and passwords cannot be resent.
        val body = request.body
        if (request.method == "POST" && body != null) {
            val once = object : RequestBody() {
                override fun contentType() = body.contentType()
                override fun contentLength() = body.contentLength()
                override fun isOneShot() = true
                override fun writeTo(sink: BufferedSink) = body.writeTo(sink)
            }
            chain.proceed(request.newBuilder().method(request.method, once).build())
        } else {
            chain.proceed(request)
        }
    }
    .build()

fun createPatrolApi(origin: HttpUrl, client: OkHttpClient = patrolHttpClient()): PatrolApi {
    require(origin.isHttps && origin == httpsOrigin(origin.toString()))
    return Retrofit.Builder()
        .baseUrl(origin)
        .client(client)
        .addConverterFactory(wireJson.asConverterFactory("application/json".toMediaType()))
        .build()
        .create(PatrolApi::class.java)
}

suspend fun <T : Any> responseBody(call: suspend () -> Response<T>): T =
    response(call).body() ?: throw PatrolFailure(Issue.RESPONSE)

suspend fun responseNoContent(call: suspend () -> Response<Unit>) {
    ensureResponse(response(call).code() == 204)
}

private suspend fun <T> response(call: suspend () -> Response<T>): Response<T> = try {
    val result = call()
    if (!result.isSuccessful) throw PatrolFailure(result.toIssue())
    result
} catch (_: SerializationException) {
    throw PatrolFailure(Issue.RESPONSE)
} catch (_: IOException) {
    throw PatrolFailure(Issue.CONNECTION)
}

private fun Response<*>.toIssue(): Issue {
    // HTTP authentication denial takes precedence over an optional application problem code.
    if (code() == 401) return Issue.UNAUTHENTICATED
    if (code() == 403) return Issue.FORBIDDEN
    val code = errorBody()?.use { body ->
        // Read only the bounded machine code, never surface server error titles or bodies.
        try {
            val source = body.source()
            source.request(4097)
            if (source.buffer.size <= 4096) wireJson.decodeFromString<ProblemCode>(source.buffer.readUtf8()).code else null
        } catch (_: IOException) {
            null
        } catch (_: SerializationException) {
            null
        }
    }
    return when (code) {
        "LineConfigurationUnavailable" -> Issue.LINE_CONFIGURATION
        "PersonalAccessConfigurationUnavailable" -> Issue.CONFIGURATION
        "TaskLineMismatch" -> Issue.LINE_MISMATCH
        "TaskSnapshotUnavailable", "TaskReadContractUnavailable" -> Issue.UNSUPPORTED_TASK
        else -> when (code()) {
            400 -> Issue.BAD_REQUEST
            404 -> Issue.NOT_FOUND
            409 -> Issue.CONFLICT
            429 -> Issue.RATE_LIMITED
            in 500..599 -> Issue.UNAVAILABLE
            else -> Issue.RESPONSE
        }
    }
}
