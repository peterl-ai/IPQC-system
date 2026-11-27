package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import java.time.Instant
import java.util.UUID

// Explicit local preparation and save. No network or send state is exposed.
internal class OfflineOrdinaryNgCoordinator(private val store: SharedWorkStore) {
    suspend fun newEdit(
        task: WorkTask, owner: SessionIdentity, attempt: WorkAttempt, text: OrdinaryNgText,
        predecessor: OperationReference? = null,
    ): OrdinaryNgCommand = store.newOrdinaryNgEdit(task, owner, UUID.randomUUID().toString(), attempt, text, predecessor)

    suspend fun save(command: OrdinaryNgCommand): OrdinaryNgEnvelope = store.saveOrdinaryNg(command)

    suspend fun draft(task: WorkTask, owner: SessionIdentity, attempt: WorkAttempt): LocalOrdinaryNgDraft =
        store.offlineOrdinaryNgDraft(task, owner, attempt)
}

internal suspend fun WorkTransaction.checkedOrdinaryNg(task: WorkTask, id: String): OrdinaryNgEnvelope? {
    val record = ordinaryNgOperation(id) ?: return null
    val (scope, owner, operation) = record.command
    val source = record.source
    workCheck(record.workspaceId == workspaceId && scope == task && operation.isValid() && operation.baseDraftVersion > 0 &&
        operation.operationId == id && source.task == task && owner.environment == task.environmentId &&
        owner.userId == source.contributorUserId && owner.familyId == source.originPersonalSessionId &&
        operation.sourceWorkContextId == source.contextId && operation.contributorUserId == owner.userId &&
        record.canonicalizationVersion == CANONICAL_VERSION && record.originLocalEditVersion > 0 &&
        record.canonicalPayload == OrdinaryNgCanonical.operation(task, operation, record.originLocalEditVersion) &&
        record.contentHash == workHash(record.canonicalPayload) && record.currentNgReferences.isSortedSampleBasis() &&
        record.currentNgReferences.none { it.operationId == id || it.operationId == operation.predecessor?.operationId },
        WorkProblem.StorageUnavailable)
    val captured = Instant.parse(record.capture.atUtc)
    val deadline = Instant.parse(record.capture.untilUtc)
    workCheck(captured >= Instant.parse(source.issuedAtUtc) && captured < deadline &&
        deadline <= Instant.parse(source.captureUntilUtc), WorkProblem.StorageUnavailable)
    return record
}

internal suspend fun WorkTransaction.ordinaryNgHistory(
    task: WorkTask, owner: SessionIdentity, attempt: WorkAttempt,
): List<OrdinaryNgEnvelope> = ordinaryNgOperationIds(owner, attempt).map { id ->
    val record = checkedOrdinaryNg(task, id) ?: throw WorkFailure(WorkProblem.StorageUnavailable)
    workCheck(record.command.owner == owner && record.command.operation.attempt == attempt, WorkProblem.StorageUnavailable)
    record
}
