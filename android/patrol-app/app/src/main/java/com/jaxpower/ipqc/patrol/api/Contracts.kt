package com.jaxpower.ipqc.patrol.api

import kotlinx.serialization.Serializable

@Serializable
class NativeLogin(val username: String, val password: String, val clientType: String)

@Serializable
class NativeRefresh(val refreshToken: String?)

@Serializable
class PasswordChange(val currentPassword: String, val newPassword: String)

@Serializable
data class CurrentUser(
    val id: String,
    val username: String,
    val displayName: String,
    val role: String,
    val isActive: Boolean,
    val mustChangePassword: Boolean,
    val permissions: List<String>,
)

@Serializable
class NativeAuth(
    val accessToken: String,
    val accessExpiresAtUtc: String,
    val user: CurrentUser,
    val refreshToken: String? = null,
    val personalSessionId: String? = null,
    val personalSessionEnvironmentId: String? = null,
    val personalSessionExpiresAtUtc: String? = null,
)

@Serializable
data class OfflineAccessReceipt(
    val version: Int,
    val userId: String,
    val personalSessionId: String,
    val environmentId: String,
    val verifiedAtUtc: String,
    val offlineAccessUntilUtc: String,
    val personalSessionExpiresAtUtc: String,
)

@Serializable
data class ProductionLine(val id: String, val code: String, val name: String, val selectionOrder: Int)

@Serializable
data class LineDirectory(val items: List<ProductionLine>)

@Serializable
data class ValidatedTaskAccess(
    val userId: String,
    val personalSessionId: String,
    val environmentId: String,
    val validatedAtUtc: String,
    val taskId: String,
    val lineId: String,
    val workflowVersion: Int,
)

@Serializable
data class LineTaskSummary(
    val id: String,
    val taskNo: String,
    val lineId: String,
    val workflowVersion: Int,
    val status: String,
    val scheduledOccurrenceUtc: String,
    val generatedAtUtc: String,
    val currentRevisionNo: Int,
    val planNoSnapshot: String? = null,
    val planNameSnapshot: String? = null,
    val standardNameSnapshot: String? = null,
    val lineNameSnapshot: String? = null,
    val readFoundationStatus: String,
    val canExecute: Boolean,
    val validatedAccess: ValidatedTaskAccess,
    val planningSnapshotVersion: Int? = null,
    val planningSnapshotStatus: String,
)

@Serializable
data class PagedLineTasks(val items: List<LineTaskSummary>, val page: Int, val pageSize: Int, val total: Int) {
    val hasNext: Boolean get() = page.toLong() * pageSize < total
}

@Serializable
data class LineTaskDefinition(
    val id: String,
    val sourcePatrolStandardItemId: String,
    val sequenceNo: Int,
    val processCode: String,
    val processName: String,
    val inspectionItemCategory: String,
    val inspectionItem: String,
    val inspectionContent: String,
    val upperLimitOperator: String,
    val upperLimitValue: String,
    val lowerLimitOperator: String,
    val lowerLimitValue: String,
    val inspectionType: String,
    val samplingPlan: String,
    val sampleCount: String,
    val photoRequirement: String,
    val defectLevel: String,
)

@Serializable
data class LineTaskDetail(
    val summary: LineTaskSummary,
    val patrolPlanId: String,
    val patrolStandardId: String,
    val sourceStandardUpdatedAtUtc: String,
    val items: List<LineTaskDefinition>,
    val canExecute: Boolean,
)

@Serializable
internal data class ProblemCode(val code: String? = null)
