package com.jaxpower.ipqc.patrol.work

import java.util.UUID

// Internal local entry point. Only OnlineWorkCoordinator can arm the protected
// profile through the verified task/context exchange. This class has no API.
internal class OfflineSampleCoordinator(private val store: SharedWorkStore) {
    fun newCapture(attempt: WorkAttempt, input: SampleInput): OfflineSampleEdit.Capture =
        OfflineSampleEdit.Capture(LocalSampleKey(attempt, UUID.randomUUID().toString()), input)

    suspend fun save(task: WorkTask, edit: OfflineSampleEdit): WorkOperation = store.saveOfflineSample(task, edit)

    suspend fun savedOperation(task: WorkTask, operationId: String): WorkOperation? =
        store.findOfflineSample(task, operationId)

    suspend fun draft(task: WorkTask): LocalSampleDraft = store.offlineSampleDraft(task)

    suspend fun prepareBatch(task: WorkTask, batchId: String, operationIds: List<String>): BatchRecord =
        store.prepareOfflineSampleBatch(task, batchId, operationIds)
}
