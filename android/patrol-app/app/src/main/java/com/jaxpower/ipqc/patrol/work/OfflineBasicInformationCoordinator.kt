package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.auth.SessionIdentity
import java.time.Instant
import java.util.UUID

// Explicit local edits only. This coordinator has no API or batch state transitions.
internal class OfflineBasicInformationCoordinator(private val store: SharedWorkStore) {
    suspend fun newEdit(
        task: WorkTask, owner: SessionIdentity, field: BasicInformationField, value: String?,
        predecessor: OperationReference? = null,
    ): BasicInformationCommand = store.newBasicInformationEdit(task, owner, UUID.randomUUID().toString(),
        field, value, predecessor)

    suspend fun save(command: BasicInformationCommand): BasicInformationEnvelope = store.saveBasicInformation(command)

    suspend fun draft(task: WorkTask, owner: SessionIdentity): LocalBasicInformationDraft =
        store.offlineBasicInformationDraft(task, owner)
}

internal suspend fun WorkTransaction.checkedBasicInformation(task: WorkTask, id: String): BasicInformationEnvelope? {
    val saved = basicInformationOperation(id) ?: return null
    val (scope, owner, op) = saved.command
    val source = saved.source
    workCheck(saved.workspaceId == workspaceId && scope == task && op.isValid() &&
        op.operationId == id && source.task == task && owner.environment == task.environmentId &&
        owner.userId == source.contributorUserId && owner.familyId == source.originPersonalSessionId &&
        op.sourceWorkContextId == source.contextId && op.contributorUserId == owner.userId &&
        saved.canonicalizationVersion == CANONICAL_VERSION && saved.originLocalEditVersion > 0 &&
        saved.canonicalPayload == BasicInformationCanonical.operation(task, op, saved.originLocalEditVersion) &&
        saved.contentHash == workHash(saved.canonicalPayload), WorkProblem.StorageUnavailable)
    val at = Instant.parse(saved.capture.atUtc)
    val until = Instant.parse(saved.capture.untilUtc)
    workCheck(at >= Instant.parse(source.issuedAtUtc) && at < until &&
        until <= Instant.parse(source.captureUntilUtc), WorkProblem.StorageUnavailable)
    return saved
}

internal suspend fun WorkTransaction.basicInformationHistory(task: WorkTask, owner: SessionIdentity):
    List<BasicInformationEnvelope> = catalog().basicInformationIds.mapNotNull { id ->
    val entry = checkedBasicInformation(task, id) ?: throw WorkFailure(WorkProblem.StorageUnavailable)
    entry.takeIf { it.command.owner == owner }
}
