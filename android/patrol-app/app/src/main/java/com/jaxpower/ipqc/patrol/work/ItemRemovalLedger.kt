package com.jaxpower.ipqc.patrol.work

import java.time.Instant
import java.time.temporal.ChronoUnit

internal val uncertainItemRemovalStates = setOf(WorkState.InFlight, WorkState.OutcomeUnknown)
internal val recoverableItemRemovalStates = uncertainItemRemovalStates + WorkState.Accepted

internal suspend fun WorkTransaction.checkedItemRemoval(task: WorkTask, id: String): ItemRemovalEntry? {
    val entry = itemRemoval(id) ?: return null
    val removal = entry.removal
    val cmd = removal.command
    val source = removal.source
    workCheck(cmd.task == task && cmd.isValid() && source.task == task && cmd.contextId == source.contextId &&
        cmd.owner.environment == task.environmentId && cmd.owner.userId == source.contributorUserId &&
        cmd.owner.familyId == source.originPersonalSessionId && removal.localEditVersion > 0 &&
        removal.minimumSampleCount in 1..50 && removal.canonicalizationVersion == CANONICAL_VERSION &&
        removal.canonicalPayload == ItemRemovalCanonical.operation(cmd, removal.localEditVersion) &&
        removal.contentHash == workHash(removal.canonicalPayload), WorkProblem.StorageUnavailable)
    val captured = Instant.parse(removal.capture.atUtc)
    val until = Instant.parse(removal.capture.untilUtc)
    workCheck(captured >= Instant.parse(source.issuedAtUtc) && captured < until &&
        until <= Instant.parse(source.captureUntilUtc) &&
        (entry.batch == null || (isWorkId(entry.batch.batchId) &&
            entry.batch == ItemRemovalCanonical.batch(removal, entry.batch.batchId))) &&
        (entry.batch != null || entry.state !in recoverableItemRemovalStates) &&
        (entry.batchReceipt != null) == (entry.state == WorkState.Accepted), WorkProblem.StorageUnavailable)
    entry.individualReceipt?.let { verifyItemRemovalReceipt(entry, it, WorkProblem.StorageUnavailable) }
    entry.batchReceipt?.let { verifyItemRemovalBatchReceipt(entry, it, WorkProblem.StorageUnavailable) }
    return entry
}

internal suspend fun WorkTransaction.itemRemovals(task: WorkTask): List<ItemRemovalEntry> =
    catalog().itemRemovalIds.map { id ->
        checkedItemRemoval(task, id) ?: throw WorkFailure(WorkProblem.StorageUnavailable)
    }

// Older builds could queue more than one removal at the same version. Keep every
// row, but give the active attempt (or the earliest queued edit) the only turn.
internal suspend fun WorkTransaction.itemRemovalTurn(entries: List<ItemRemovalEntry>, version: Long): ItemRemovalEntry? =
    entries.filter {
        val command = it.removal.command
        command.expectedDraftVersion == version &&
            (it.state in uncertainItemRemovalStates || (it.state == WorkState.Queued && !sourceRevoked(command.contextId)))
    }.minWithOrNull(compareBy({ it.state !in uncertainItemRemovalStates }, { it.removal.localEditVersion }))

internal suspend fun WorkTransaction.checkSampleItemRemovals(entries: List<ItemRemovalEntry>, version: Long,
                                                             attempts: Set<WorkAttempt>) {
    val blocked = entries.any {
        it.removal.command.attempt in attempts && (it.state in uncertainItemRemovalStates || it.serverEvidence != null)
    } || entries.filter { it.removal.command.expectedDraftVersion >= version }
        .groupBy { it.removal.command.expectedDraftVersion }.any { (base, sameVersion) ->
            itemRemovalTurn(sameVersion, base)?.removal?.command?.attempt?.let { it in attempts } == true
        }
    workCheck(!blocked, WorkProblem.ItemDispositionConflict)
}

internal suspend fun WorkTransaction.checkItemRemovalBarrier(task: WorkTask, observedVersion: Long) {
    val entries = itemRemovals(task)
    workCheck(entries.none { it.state in uncertainItemRemovalStates }, WorkProblem.InvalidState)
    workCheck(entries.none { (it.batchReceipt?.draftVersion ?: 0) > observedVersion }, WorkProblem.StaleGrant)
}

internal fun ItemRemovalEnvelope.matches(receipt: ItemRemovalReceipt): Boolean =
    receipt.matches(command.task, command.attempt, minimumSampleCount) &&
        receipt.operationId == command.operationId && receipt.contentHash == contentHash &&
        receipt.sourceWorkContextId == command.contextId && receipt.contributorUserId == command.owner.userId &&
        receipt.confirmationSource == command.confirmation && receipt.reason == command.reason &&
        receipt.draftVersion == command.expectedDraftVersion + 1 &&
        Instant.parse(receipt.acceptedAtUtc) >= Instant.parse(source.issuedAtUtc).truncatedTo(ChronoUnit.MICROS)

internal fun verifyItemRemovalReceipt(entry: ItemRemovalEntry, receipt: ItemRemovalReceipt,
                                      problem: WorkProblem = WorkProblem.ReceiptMismatch) {
    workCheck(receipt.batchId == entry.batch?.batchId && entry.removal.matches(receipt), problem)
}

internal fun verifyItemRemovalBatchReceipt(entry: ItemRemovalEntry, receipt: ItemRemovalBatchReceipt,
                                           problem: WorkProblem = WorkProblem.ReceiptMismatch) {
    val batch = entry.batch ?: throw WorkFailure(problem)
    val cmd = entry.removal.command
    val operation = receipt.itemOperations.singleOrNull() ?: throw WorkFailure(problem)
    verifyItemRemovalReceipt(entry, operation, problem)
    workCheck(receipt.receiptVersion == 2 && receipt.operations.isEmpty() &&
        receipt.taskId == cmd.task.taskId && receipt.batchId == batch.batchId &&
        receipt.requestFingerprint == batch.fingerprint && receipt.previousDraftVersion == batch.expectedDraftVersion &&
        receipt.draftVersion == operation.draftVersion && receipt.uploadedByUserId == operation.uploadedByUserId &&
        receipt.uploadedPersonalSessionId == operation.uploadedPersonalSessionId &&
        receipt.acceptedAtUtc == operation.acceptedAtUtc &&
        (entry.individualReceipt == null || entry.individualReceipt == operation), problem)
}

internal fun OfflineSampleBasis.requireItemRemovalAttempt(attempt: WorkAttempt): FrozenSampleAttempt {
    workCheck(editable && attempts.isNotEmpty() && attempts.all {
        it.serverDisposition != null && (it.serverDisposition != ItemDisposition.Removed || it.serverRemoval != null)
    }, WorkProblem.ScopeMismatch)
    val current = attempts.singleOrNull { it.attempt == attempt } ?: throw WorkFailure(WorkProblem.ScopeMismatch)
    workCheck(current.serverDisposition == ItemDisposition.Applicable, WorkProblem.ItemDispositionConflict)
    return current
}

internal fun OfflineSampleBasis.sameObservation(other: OfflineSampleBasis) =
    copy(historicalItemRemovals = emptyList()) == other.copy(historicalItemRemovals = emptyList())

// Existing A03-05 encrypted profiles have the applicability bit only.
// At equal version, upgrade solely when every previously persisted fact matches.
internal fun OfflineSampleBasis.isLegacyObservationOf(observed: OfflineSampleBasis): Boolean =
    attempts.isNotEmpty() && attempts.all { it.serverDisposition == null && it.serverRemoval == null } &&
        sameObservation(observed.copy(attempts = observed.attempts.map {
            it.copy(serverDisposition = null, serverRemoval = null)
        }))

internal fun OfflineSampleBasis.withRemovalHistoryFrom(previous: OfflineSampleBasis): OfflineSampleBasis {
    val current = attempts.mapNotNull { it.serverRemoval }.associateBy { it.operationId }
    val byAttempt = attempts.associateBy { it.attempt }
    val known = previous.historicalItemRemovals + previous.attempts.mapNotNull { it.serverRemoval }
    workCheck(known.all { old ->
        val attempt = WorkAttempt(old.taskItemId, old.inspectionAttemptId)
        current[old.operationId]?.let { it == old } != false &&
            (byAttempt[attempt] == null || byAttempt[attempt]?.serverRemoval == old)
    }, WorkProblem.ReceiptMismatch)
    return copy(historicalItemRemovals = known.filter { it.operationId !in current }.distinctBy { it.operationId })
}
