package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import java.util.UUID

// Internal local entry point. Only OnlineWorkCoordinator can arm the protected
// profile through the verified task/context exchange. This class has no API.
internal class OfflineSampleCoordinator(private val store: SharedWorkStore) {
    fun newCapture(attempt: WorkAttempt, input: SampleInput): OfflineSampleEdit.Capture =
        OfflineSampleEdit.Capture(LocalSampleKey(attempt, UUID.randomUUID().toString()), input)

    suspend fun save(task: WorkTask, edit: OfflineSampleEdit, expected: SessionIdentity? = null): WorkOperation =
        store.saveOfflineSample(task, edit, expected)

    suspend fun savedOperation(task: WorkTask, operationId: String, expected: SessionIdentity? = null): WorkOperation? =
        store.findOfflineSample(task, operationId, expected)

    suspend fun draft(task: WorkTask, expected: SessionIdentity? = null): LocalSampleDraft =
        store.offlineSampleDraft(task, expected)

    suspend fun captureProblem(task: WorkTask, expected: SessionIdentity): WorkProblem? =
        store.offlineCaptureProblem(task, expected)

    suspend fun prepareBatch(task: WorkTask, batchId: String, operationIds: List<String>): BatchRecord =
        store.prepareOfflineSampleBatch(task, batchId, operationIds)
}
