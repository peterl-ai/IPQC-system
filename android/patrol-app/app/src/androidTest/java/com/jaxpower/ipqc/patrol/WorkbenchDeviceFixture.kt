package com.jaxpower.ipqc.patrol

import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.DETAIL
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.IDENTITY
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.LINE
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.NOW
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.ORIGIN
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.SECOND_LINE
import com.jaxpower.ipqc.patrol.OfflineDeviceFixture.Companion.TASK
import com.jaxpower.ipqc.patrol.api.*
import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import com.jaxpower.ipqc.patrol.offline.CacheRows
import com.jaxpower.ipqc.patrol.offline.OfflineCache
import com.jaxpower.ipqc.patrol.tasks.PatrolRepository
import com.jaxpower.ipqc.patrol.ui.PatrolViewModel
import com.jaxpower.ipqc.patrol.work.*
import java.time.Clock
import java.time.Instant
import java.time.ZoneOffset
import java.util.Base64
import java.util.concurrent.atomic.AtomicInteger
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.encodeToJsonElement
import kotlinx.serialization.json.jsonObject
import okhttp3.RequestBody
import okhttp3.ResponseBody.Companion.toResponseBody
import okhttp3.ResponseBody
import retrofit2.Response

internal class WorkbenchDeviceFixture : AutoCloseable {
    val device = OfflineDeviceFixture(canContribute = true)
    val b = SessionIdentity(benchId(41), benchId(42), IDENTITY.environment)
    val clock = Clock.fixed(Instant.parse(NOW), ZoneOffset.UTC)
    val secondTaskId = benchId(11)
    val detailCalls = AtomicInteger()
    val definitions = listOf(
        DETAIL.items.single().copy(inspectionItem = "Surface finish", inspectionContent = "Check for visible defects.",
            inspectionType = "Qualitative", sampleCount = "", samplingPlan = "One sample per round"),
        DETAIL.items.single().copy(id = benchId(22), sourcePatrolStandardItemId = benchId(21), sequenceNo = 2,
            inspectionItem = "Seal offset", inspectionContent = "Measure the seal offset at the marked point.",
            inspectionType = "Quantitative", sampleCount = "50", lowerLimitOperator = ">=", lowerLimitValue = "-1.5",
            upperLimitOperator = "<=", upperLimitValue = "2", samplingPlan = "Measure the frozen sample set"),
    )
    val wire = WorkbenchWorkApi()
    val api = object : PatrolApi by device.api {
        override suspend fun tasks(bearer: String, lineId: String, page: Int, pageSize: Int): Response<PagedLineTasks> {
            val line = listOf(LINE, SECOND_LINE).single { it.id == lineId }
            return Response.success(PagedLineTasks(listOf(task(line)), page, pageSize, 1))
        }

        override suspend fun detail(bearer: String, taskId: String, lineId: String): Response<LineTaskDetail> {
            detailCalls.incrementAndGet()
            val line = listOf(LINE, SECOND_LINE).single { it.id == lineId }
            val selected = task(line)
            check(selected.id == taskId)
            return Response.success(DETAIL.copy(summary = selected, items = definitions))
        }

        override suspend fun logout(bearer: String, input: NativeRefresh): Response<Unit> = Response.success<Unit>(204, null)
    }

    fun task(line: ProductionLine = LINE): LineTaskSummary {
        val id = if (line == LINE) TASK.id else secondTaskId
        val owner = device.api.loginSubject
        return TASK.copy(id = id, lineId = line.id, lineNameSnapshot = line.name,
            taskNo = if (line == LINE) "PILOT-001" else "PILOT-002",
            validatedAccess = ValidatedTaskAccess(owner.userId, owner.familyId, owner.environment, NOW, id, line.id, 2))
    }

    fun attempt(taskId: String, index: Int): WorkAttempt = WorkAttempt(definitions[index].id,
        benchId((if (taskId == TASK.id) 4 else 7) + index))

    fun scope(line: ProductionLine = LINE): WorkTask = WorkTask(IDENTITY.environment, task(line).id, line.id, 2)

    fun components(connectivity: MutableStateFlow<Boolean> = MutableStateFlow(true), cache: OfflineCache = device.cache,
                   rows: CacheRows = device.rows, enabled: Boolean = true): WorkbenchComponents {
        val work = workServices(cache, rows)
        val model = PatrolViewModel(work.sessions, work.patrol, connectivity, cache, work.online.takeIf { enabled })
        return WorkbenchComponents(work.rows, work.online, model, connectivity)
    }

    fun workServices(cache: OfflineCache = device.cache, rows: CacheRows = device.rows): WorkbenchWorkServices {
        val session = SessionRepository(api, device.vault, ORIGIN, clock, cache)
        val patrol = PatrolRepository(api, session, cache)
        val ledgerRows = ProtectedWorkRows(rows, ORIGIN, device.workKey)
        val store = SharedWorkStore(ledgerRows, session)
        val online = OnlineWorkCoordinator(ORIGIN, patrol, wire, session, store)
        return WorkbenchWorkServices(ledgerRows, online, session, store, patrol)
    }

    override fun close() = device.close()

    inner class WorkbenchWorkApi : WorkContextApi {
        val issues = AtomicInteger()
        val reads = AtomicInteger()
        val sends = AtomicInteger()
        val removalPreflights = AtomicInteger()
        val removalReceiptReads = AtomicInteger()
        var explicitRemovalSource: WorkSource? = null
        var explicitRemovalReceipt: ItemRemovalBatchReceipt? = null
        var observedRemoval: ItemRemovalReceipt? = null
        var includeRemote = true
        var captureUntil = "2026-11-19T14:01:00Z"
        private val proof = Base64.getUrlEncoder().withoutPadding().encodeToString(ByteArray(32) { 29 })

        override suspend fun sharedDraft(bearer: String, taskId: String): Response<SharedDraftWire> {
            reads.incrementAndGet()
            val line = if (taskId == TASK.id) LINE else SECOND_LINE
            val attempts = definitions.indices.map { index ->
                val attempt = attempt(taskId, index)
                DraftAttemptWire(attempt.taskItemId, attempt.inspectionAttemptId, if (index == 0) 1 else 50)
            }
            val removed = observedRemoval?.takeIf { it.taskId == taskId }
            val remote = if (includeRemote) {
                val quant = attempts[1]
                listOf(DraftSampleWire(benchId(30), quant.taskItemId, quant.inspectionAttemptId, benchId(31), 1,
                    DraftSampleValueWire("Quantitative", JsonPrimitive("1.25".toBigDecimal()), "OK", null),
                    false, benchId(32), IDENTITY.userId, benchId(32), removed?.inspectionAttemptId != quant.inspectionAttemptId))
            } else emptyList()
            return Response.success(SharedDraftWire(taskId, line.id, 2, removed?.draftVersion ?: if (includeRemote) 7 else 0,
                if (includeRemote) "SharedDraftInitialized" else "AlreadyInitialized", attempts, remote, false,
                "NotEvaluatedForFormalSubmission", items = attempts.map {
                    val receipt = removed?.takeIf { r -> r.inspectionAttemptId == it.inspectionAttemptId }
                    DraftItemWire(it.taskItemId, it.inspectionAttemptId, it.minimumSampleCount,
                        receipt?.disposition?.name ?: "Applicable", receipt?.let { r -> Json.encodeToJsonElement(r).jsonObject })
                }, allItemsRemoved = false))
        }

        override suspend fun issue(bearer: String, taskId: String, input: WorkContextIssueWire): Response<WorkContextIssuedWire> {
            val owner = device.api.loginSubject
            val line = if (taskId == TASK.id) LINE else SECOND_LINE
            check(input.expectedTaskLineId == line.id && input.expectedEnvironmentId == owner.environment &&
                input.expectedWorkflowVersion == 2)
            issues.incrementAndGet()
            val context = metadata(taskId)
            val receipt = OfflineAccessReceipt(1, owner.userId, owner.familyId, owner.environment,
                NOW, "2026-11-22T14:00:00Z", FAMILY_EXPIRY)
            return Response.success(201, WorkContextIssuedWire(context, proof, receipt, false))
        }

        override suspend fun context(bearer: String, taskId: String, contextId: String): Response<WorkContextMetadataWire> {
            val recorded = metadata(taskId)
            check(recorded.id == contextId)
            return Response.success(recorded)
        }

        private fun metadata(taskId: String): WorkContextMetadataWire {
            val owner = device.api.loginSubject
            val isFirst = taskId == TASK.id
            val line = if (isFirst) LINE else SECOND_LINE
            val offset = (if (owner == IDENTITY) 0 else 2) + if (isFirst) 0 else 1
            return WorkContextMetadataWire(benchId(51 + offset), 1, owner.environment, owner.userId,
                owner.familyId, 4, taskId, line.id, 2, NOW, captureUntil, FAMILY_EXPIRY, revision = 1)
        }

        override suspend fun preflight(bearer: String, taskId: String, input: WorkContextPreflightInputWire):
            Response<WorkContextPreflightWire> {
            val original = checkNotNull(explicitRemovalSource) { "Unexpected upload preflight" }
            check(device.api.loginSubject == b && original.task.taskId == taskId)
            check(input.expectedTaskLineId == original.task.lineId && input.expectedEnvironmentId == original.task.environmentId &&
                input.expectedWorkflowVersion == original.task.workflowVersion)
            val submitted = input.sources.single()
            check(submitted.workContextId == original.contextId && submitted.contributorUserIdAssertion == original.contributorUserId &&
                submitted.proofSecret == proof)
            removalPreflights.incrementAndGet()
            return Response.success(WorkContextPreflightWire(NOW,
                listOf(WorkContextValidatedWire(original.contextId, original.contributorUserId, original.captureUntilUtc)),
                "SourcePreflightOnly", false))
        }

        override suspend fun preflightSampleBatch(bearer: String, taskId: String, input: WorkContextPreflightInputWire,
                                                  guard: GuardedSampleExchange): Response<ResponseBody> {
            sends.incrementAndGet()
            error("Unexpected sample preflight")
        }

        override suspend fun uploadSampleBatch(bearer: String, taskId: String, input: RequestBody,
                                               guard: GuardedSampleExchange): Response<ResponseBody> {
            sends.incrementAndGet()
            error("Unexpected sample upload")
        }

        override suspend fun batchReceipt(bearer: String, taskId: String, batchId: String): Response<ResponseBody> {
            val receipt = checkNotNull(explicitRemovalReceipt) { "Unexpected batch receipt query" }
            check(device.api.loginSubject == b && receipt.taskId == taskId && receipt.batchId == batchId)
            removalReceiptReads.incrementAndGet()
            return Response.success(Json.encodeToString(receipt).toResponseBody())
        }

        override suspend fun operationReceipt(bearer: String, taskId: String, operationId: String): Response<ResponseBody> =
            error("Local recovery never queries a server receipt")
    }

    private companion object { const val FAMILY_EXPIRY = "2026-11-26T14:00:00Z" }
}

internal class WorkbenchComponents(
    val rows: ProtectedWorkRows,
    val online: OnlineWorkCoordinator,
    val model: PatrolViewModel,
    val connectivity: MutableStateFlow<Boolean>,
)

internal class WorkbenchWorkServices(
    val rows: ProtectedWorkRows,
    val online: OnlineWorkCoordinator,
    val sessions: SessionRepository,
    val store: SharedWorkStore,
    val patrol: PatrolRepository,
)

internal fun benchId(value: Int) = "70000000-0000-0000-0000-" + value.toString().padStart(12, '0')
