package com.jaxpower.ipqc.patrol.tasks

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.LineTaskDetail
import com.jaxpower.ipqc.patrol.api.LineTaskSummary
import com.jaxpower.ipqc.patrol.api.PagedLineTasks
import com.jaxpower.ipqc.patrol.api.PatrolApi
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.api.ProductionLine
import com.jaxpower.ipqc.patrol.api.ensureResponse
import com.jaxpower.ipqc.patrol.api.responseBody
import com.jaxpower.ipqc.patrol.api.utc
import com.jaxpower.ipqc.patrol.api.validGuid
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import com.jaxpower.ipqc.patrol.offline.OfflineCache

class PatrolRepository(
    private val api: PatrolApi,
    private val sessions: SessionRepository,
    private val offline: OfflineCache? = null,
) {
    suspend fun lines(): List<ProductionLine> = sessions.read(
        accepted = { identity, lines -> offline?.cacheLines(identity, lines) },
    ) { bearer, _ ->
        val items = responseBody { api.lines(bearer) }.items
        if (items.size != 2 || items.map { it.id.lowercase() }.distinct().size != 2 ||
            items.map { it.selectionOrder }.toSet() != setOf(1, 2) ||
            items.map { it.code.trim().lowercase() }.distinct().size != 2 ||
            items.any { !validGuid(it.id) || it.code.isBlank() || it.name.isBlank() })
            throw PatrolFailure(Issue.LINE_CONFIGURATION)
        items.sortedBy { it.selectionOrder }
    }

    suspend fun tasks(line: ProductionLine, page: Int): PagedLineTasks = sessions.read(
        accepted = { identity, tasks -> offline?.cachePage(identity, line, tasks) },
    ) { bearer, identity ->
        ensureResponse(validGuid(line.id) && page >= 1)
        val result = responseBody { api.tasks(bearer, line.id, page, PAGE_SIZE) }
        ensureResponse(result.page == page && result.pageSize == PAGE_SIZE && result.total >= 0 &&
            result.items.size <= PAGE_SIZE && result.items.map { it.id }.distinct().size == result.items.size)
        result.items.forEach { validate(it, line.id, identity) }
        result
    }

    suspend fun detail(line: ProductionLine, task: LineTaskSummary): LineTaskDetail = sessions.read(
        accepted = { identity, detail -> offline?.cacheDetail(identity, detail) },
    ) { bearer, identity ->
        ensureResponse(validGuid(task.id) && validGuid(line.id) && task.lineId == line.id)
        val result = responseBody { api.detail(bearer, task.id, line.id) }
        validate(result.summary, line.id, identity)
        ensureResponse(result.summary.id == task.id && !result.canExecute &&
            validGuid(result.patrolPlanId) && validGuid(result.patrolStandardId))
        if (result.summary.readFoundationStatus !in setOf("FoundationOnly", "DraftFoundation") ||
            result.summary.planningSnapshotStatus !in setOf("ValidV1", "HistoricalUnadmitted"))
            throw PatrolFailure(Issue.UNSUPPORTED_TASK)
        utc(result.sourceStandardUpdatedAtUtc)
        ensureResponse(result.items.isNotEmpty() &&
            result.items.map { it.sequenceNo }.distinct().size == result.items.size &&
            result.items.map { it.id }.distinct().size == result.items.size &&
            result.items.all { validGuid(it.id) && validGuid(it.sourcePatrolStandardItemId) && it.sequenceNo > 0 })
        result
    }

    private fun validate(task: LineTaskSummary, line: String, authority: SessionIdentity) {
        val access = task.validatedAccess
        if (task.lineId != line || access.lineId != line) throw PatrolFailure(Issue.LINE_MISMATCH)
        ensureResponse(validGuid(task.id) && task.taskNo.isNotBlank() && task.workflowVersion == 2 && !task.canExecute &&
            task.status.isNotBlank() && task.readFoundationStatus.isNotBlank() && task.currentRevisionNo >= 0 &&
            access.taskId == task.id && access.workflowVersion == 2 &&
            SessionIdentity(access.userId, access.personalSessionId, access.environmentId) == authority)
        utc(task.scheduledOccurrenceUtc)
        utc(task.generatedAtUtc)
        utc(access.validatedAtUtc)
    }

    companion object { const val PAGE_SIZE = 20 }
}
