package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.api.GuardedUploadExchange
import com.jaxpower.ipqc.patrol.api.OfflineAccessReceipt
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import okhttp3.RequestBody
import okhttp3.ResponseBody
import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.Header
import retrofit2.http.POST
import retrofit2.http.Path
import retrofit2.http.Streaming
import retrofit2.http.Tag

internal interface WorkContextApi {
    @Streaming
    @POST("api/line-patrol/tasks/{taskId}/operation-batches")
    suspend fun uploadSampleBatch(@Header("Authorization") bearer: String, @Path("taskId") taskId: String,
                                  @Body input: RequestBody, @Tag guard: GuardedUploadExchange): Response<ResponseBody>

    @Streaming
    @POST("api/line-patrol/tasks/{taskId}/item-removal-batches")
    suspend fun uploadItemRemovalBatch(@Header("Authorization") bearer: String, @Path("taskId") taskId: String,
                                       @Body input: RequestBody, @Tag guard: GuardedUploadExchange): Response<ResponseBody>

    @Streaming
    @POST("api/line-patrol/tasks/{taskId}/work-contexts/validate-sources")
    suspend fun preflightUploadSources(@Header("Authorization") bearer: String, @Path("taskId") taskId: String,
                                     @Body input: WorkContextPreflightInputWire,
                                     @Tag guard: GuardedUploadExchange): Response<ResponseBody>

    @Streaming
    @GET("api/line-patrol/tasks/{taskId}/operation-batches/{batchId}/receipt")
    suspend fun batchReceipt(@Header("Authorization") bearer: String, @Path("taskId") taskId: String,
                             @Path("batchId") batchId: String): Response<ResponseBody>

    @Streaming
    @GET("api/line-patrol/tasks/{taskId}/operations/{operationId}/receipt")
    suspend fun operationReceipt(@Header("Authorization") bearer: String, @Path("taskId") taskId: String,
                                  @Path("operationId") operationId: String): Response<ResponseBody>

    @GET("api/line-patrol/tasks/{taskId}/shared-draft")
    suspend fun sharedDraft(@Header("Authorization") bearer: String, @Path("taskId") taskId: String): Response<SharedDraftWire>

    @POST("api/line-patrol/tasks/{taskId}/work-contexts")
    suspend fun issue(@Header("Authorization") bearer: String, @Path("taskId") taskId: String,
                      @Body input: WorkContextIssueWire): Response<WorkContextIssuedWire>

    @GET("api/line-patrol/tasks/{taskId}/work-contexts/{contextId}")
    suspend fun context(@Header("Authorization") bearer: String, @Path("taskId") taskId: String,
                        @Path("contextId") contextId: String): Response<WorkContextMetadataWire>

    @POST("api/line-patrol/tasks/{taskId}/work-contexts/validate-sources")
    suspend fun preflight(@Header("Authorization") bearer: String, @Path("taskId") taskId: String,
                          @Body input: WorkContextPreflightInputWire): Response<WorkContextPreflightWire>
}

@Serializable
internal class SharedDraftWire(
    val taskId: String,
    val lineId: String,
    val workflowVersion: Int,
    val draftVersion: Long,
    val availability: String,
    val currentAttempts: List<DraftAttemptWire>,
    val samples: List<DraftSampleWire>,
    val canExecute: Boolean,
    val completeness: String,
    val items: List<DraftItemWire>? = null,
    val allItemsRemoved: Boolean,
)

@Serializable
internal class DraftAttemptWire(val taskItemId: String, val inspectionAttemptId: String, val minimumSampleCount: Int)

@Serializable
internal class DraftItemWire(
    val taskItemId: String, val inspectionAttemptId: String, val minimumSampleCount: Int, val disposition: String,
    val removal: JsonObject? = null,
)

@Serializable
internal class DraftSampleWire(
    val sampleId: String, val taskItemId: String, val inspectionAttemptId: String, val clientSampleId: String,
    val sequenceNo: Int, val value: DraftSampleValueWire, val isRemoved: Boolean, val valueOperationId: String,
    val valueContributorUserId: String, val lastOperationId: String, val isCurrentApplicable: Boolean,
)

@Serializable
internal class DraftSampleValueWire(
    val inspectionType: String, val inspectionValue: JsonPrimitive?, val judgment: String, val barcode: String?,
)

@Serializable
internal class WorkContextIssueWire(
    val expectedTaskLineId: String, val expectedWorkflowVersion: Int, val expectedEnvironmentId: String,
)

@Serializable
internal class WorkContextMetadataWire(
    val id: String, val contractVersion: Int, val environmentId: String, val userId: String,
    val issuedPersonalSessionId: String, val issuedAuthVersion: Int, val taskId: String, val lineId: String,
    val workflowVersion: Int, val issuedAtUtc: String, val captureUntilUtc: String, val personalSessionExpiresAtUtc: String,
    val securityRevokedAtUtc: String? = null, val securityRevocationReason: String? = null,
    val securityRevokedByUserId: String? = null, val revision: Long,
)

@Serializable
internal class WorkContextIssuedWire(
    val context: WorkContextMetadataWire, val proofSecret: String,
    val offlineAccessReceipt: OfflineAccessReceipt, val canExecute: Boolean,
)

@Serializable
internal class WorkContextSourceWire(
    val workContextId: String, val proofSecret: String, val contributorUserIdAssertion: String,
)

@Serializable
internal class WorkContextPreflightInputWire(
    val expectedTaskLineId: String, val expectedWorkflowVersion: Int, val expectedEnvironmentId: String,
    val sources: List<WorkContextSourceWire>,
)

@Serializable
internal class WorkContextPreflightWire(
    val validatedAtUtc: String, val sources: List<WorkContextValidatedWire>,
    val purpose: String, val acceptsWork: Boolean,
)

@Serializable
internal class WorkContextValidatedWire(
    val workContextId: String, val contributorUserId: String, val captureUntilUtc: String,
)
