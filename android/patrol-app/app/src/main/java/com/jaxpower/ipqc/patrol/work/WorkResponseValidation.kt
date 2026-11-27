package com.jaxpower.ipqc.patrol.work

import com.jaxpower.ipqc.patrol.api.Issue
import com.jaxpower.ipqc.patrol.api.LineTaskDetail
import com.jaxpower.ipqc.patrol.api.PatrolFailure
import com.jaxpower.ipqc.patrol.api.ensureResponse
import com.jaxpower.ipqc.patrol.api.utc
import com.jaxpower.ipqc.patrol.auth.SessionRepository
import java.time.Instant
import java.time.temporal.ChronoUnit

internal enum class DraftAvailability(val allowsContext: Boolean) {
    AlreadyInitialized(true), SharedDraftInitialized(true), EligibleCleanFoundationOnly(true),
    HistoricalUnadmitted(false), HistoricalInspectionData(false), InvalidPlanningSnapshot(false),
    UnsupportedSnapshot(false), UnsupportedRequirements(false), UnsupportedIdentity(false),
}

internal class OnlineDraft(
    val task: WorkTask,
    val version: Long,
    val availability: DraftAvailability,
    val attempts: Set<WorkAttempt>,
    val applicableAttempts: Set<WorkAttempt>,
    val allItemsRemoved: Boolean,
    val validPlanning: Boolean,
    val sampleBasis: OfflineSampleBasis,
    val itemIds: List<String>,
)

internal fun SharedDraftWire.validate(detail: LineTaskDetail, session: SessionRepository.WorkSession): OnlineDraft {
    val summary = detail.summary
    val state = DraftAvailability.entries.firstOrNull { it.name == availability }
    ensureResponse(state != null && taskId == summary.id && lineId == summary.lineId &&
        isWorkId(taskId) && isWorkId(lineId) && workflowVersion == 2 && draftVersion >= 0 &&
        !canExecute && completeness == "NotEvaluatedForFormalSubmission")

    val definitions = detail.items.associateBy { it.id }
    val forbiddenAttemptIds = definitions.keys + detail.items.map { it.sourcePatrolStandardItemId }
    val byItem = currentAttempts.associateBy { it.taskItemId }
    val firstAttemptScope = state in setOf(DraftAvailability.AlreadyInitialized, DraftAvailability.SharedDraftInitialized)
    ensureResponse(byItem.size == currentAttempts.size &&
        currentAttempts.map { it.inspectionAttemptId }.distinct().size == currentAttempts.size &&
        currentAttempts.all {
            isWorkId(it.taskItemId) && it.taskItemId in definitions &&
                isWorkId(it.inspectionAttemptId) && it.inspectionAttemptId !in forbiddenAttemptIds &&
                it.minimumSampleCount in 1..50 &&
                (!firstAttemptScope || definitions[it.taskItemId]?.sampleCount?.trim()?.let { count ->
                    if (count.isEmpty()) 1 else count.toIntOrNull()
                } == it.minimumSampleCount)
        })
    when (state) {
        DraftAvailability.EligibleCleanFoundationOnly -> ensureResponse(currentAttempts.isEmpty() && samples.isEmpty() && draftVersion == 0L)
        DraftAvailability.AlreadyInitialized -> ensureResponse(byItem.keys == definitions.keys && samples.isEmpty() && draftVersion == 0L)
        DraftAvailability.SharedDraftInitialized -> ensureResponse(byItem.keys == definitions.keys && draftVersion > 0L)
        else -> Unit
    }

    val dispositions = items?.associateBy { it.taskItemId }
    if (items != null) {
        ensureResponse(dispositions?.size == items.size && dispositions.keys == byItem.keys &&
            items.all {
                val pair = byItem[it.taskItemId]
                pair != null && it.inspectionAttemptId == pair.inspectionAttemptId &&
                    it.minimumSampleCount == pair.minimumSampleCount &&
                    it.disposition in setOf("Applicable", "Removed", "UnableToMeasureNG")
            })
    }
    ensureResponse(allItemsRemoved == (dispositions?.size == definitions.size &&
        dispositions.values.all { it.disposition == "Removed" }))

    ensureResponse(samples.map { it.sampleId }.distinct().size == samples.size &&
        samples.map { it.inspectionAttemptId to it.clientSampleId }.distinct().size == samples.size &&
        samples.all {
            val value = it.value.inspectionValue
            val applicable = byItem[it.taskItemId]?.inspectionAttemptId == it.inspectionAttemptId &&
                dispositions?.get(it.taskItemId)?.disposition.let { flag -> flag == null || flag == "Applicable" }
            it.taskItemId in definitions && isWorkId(it.sampleId) && isWorkId(it.clientSampleId) &&
                isWorkId(it.inspectionAttemptId) && it.sequenceNo > 0 &&
                isWorkId(it.valueOperationId) && isWorkId(it.lastOperationId) &&
                isWorkId(it.valueContributorUserId) && (!it.isCurrentApplicable || applicable) &&
                it.value.judgment in setOf("OK", "NG") && when (it.value.inspectionType) {
                    "Qualitative" -> value == null
                    "Quantitative" -> value != null && !value.isString &&
                        runCatching { DraftOperationCanonical.decimalText(value.content) }.isSuccess
                    else -> false
                }
        })

    val task = WorkTask(session.identity.environment, taskId, lineId, workflowVersion)
    val frozen = currentAttempts.map { pair ->
        val attempt = WorkAttempt(pair.taskItemId, pair.inspectionAttemptId)
        val type = FrozenSampleType.fromDefinition(definitions.getValue(pair.taskItemId).inspectionType)
        val item = dispositions?.get(pair.taskItemId)
        val disposition = item?.let { ItemDisposition.valueOf(it.disposition) }
        val receipt = item?.removal?.let {
            try {
                it.decodeItemRemovalReceipt()
            } catch (_: kotlinx.serialization.SerializationException) {
                throw PatrolFailure(Issue.RESPONSE)
            }
        }
        // Existing sample reads also accept responses lacking typed removal details.
        // No item edit is armed by such a response.
        if (receipt != null) ensureResponse(disposition == ItemDisposition.Removed &&
            receipt.matches(task, attempt, pair.minimumSampleCount) && receipt.draftVersion <= draftVersion)
        FrozenSampleAttempt(attempt, pair.minimumSampleCount, type, disposition == ItemDisposition.Applicable,
            disposition, receipt)
    }.sortedBy { it.attempt.taskItemId }
    val observed = samples.map { sample ->
        val key = LocalSampleKey(WorkAttempt(sample.taskItemId, sample.inspectionAttemptId), sample.clientSampleId)
        val value = sample.value
        ObservedSample(sample.sampleId, key, ServerSampleValue(value.inspectionType,
            value.inspectionValue?.content?.let(DraftOperationCanonical::decimalText), value.judgment, value.barcode),
            sample.valueContributorUserId, sample.valueOperationId, sample.lastOperationId,
            sample.isRemoved, sample.isCurrentApplicable)
    }.sortedWith(compareBy({ it.key.attempt.taskItemId }, { it.key.attempt.inspectionAttemptId }, { it.key.clientSampleId }))
    val validPlanning = summary.planningSnapshotStatus == "ValidV1"
    val applicable = frozen.filter { it.applicable }.associateBy { it.attempt }
    val editable = firstAttemptScope && validPlanning && draftVersion < Long.MAX_VALUE &&
        items != null && applicable.isNotEmpty() &&
        applicable.values.all { it.type != null } &&
        observed.filter { it.current && !it.removed }.groupBy { it.key.attempt }.all { (attempt, records) ->
            records.size <= MAX_CURRENT_SAMPLES && records.all { it.value.inspectionType == applicable[attempt]?.type?.name }
        }
    ensureResponse(frozen.mapNotNull { it.serverRemoval?.operationId }.let { it.distinct().size == it.size })
    val itemIds = detail.items.sortedWith(compareBy({ it.sequenceNo }, { it.id })).map { it.id }
    return OnlineDraft(task, draftVersion, requireNotNull(state), frozen.map { it.attempt }.toSet(),
        applicable.keys, allItemsRemoved, validPlanning, OfflineSampleBasis(task, draftVersion, editable, frozen, observed), itemIds)
}

internal fun WorkContextMetadataWire.validatedSource(task: WorkTask): WorkSource {
    val issued = utc(issuedAtUtc)
    val deadline = utc(captureUntilUtc)
    val expiry = utc(personalSessionExpiresAtUtc)
    ensureResponse(contractVersion == 1 && isWorkId(id) && isWorkId(userId) &&
        isWorkId(issuedPersonalSessionId) && issuedAuthVersion >= 1 &&
        taskId == task.taskId && lineId == task.lineId && workflowVersion == task.workflowVersion &&
        environmentId == task.environmentId && issued < deadline && deadline <= expiry &&
        deadline <= issued.plusSeconds(72 * 3600L))
    val revoked = securityRevokedAtUtc
    if (revoked == null) {
        ensureResponse(revision == 1L && securityRevocationReason == null && securityRevokedByUserId == null)
    } else {
        ensureResponse(revision == 2L && utc(revoked) >= issued &&
            !securityRevocationReason.isNullOrBlank() && securityRevocationReason.length <= 2000 &&
            securityRevokedByUserId?.let(::isWorkId) == true)
    }
    return WorkSource(task, id, userId, issuedPersonalSessionId, issuedAtUtc, captureUntilUtc,
        personalSessionExpiresAtUtc, contractVersion)
}

internal fun WorkContextIssuedWire.validate(task: WorkTask, session: SessionRepository.WorkSession,
                                             currentTime: Instant): StoredWorkSource {
    val source = context.validatedSource(task)
    val identity = session.identity
    val receipt = offlineAccessReceipt
    val verified = utc(receipt.verifiedAtUtc)
    val until = utc(receipt.offlineAccessUntilUtc)
    val family = utc(receipt.personalSessionExpiresAtUtc)
    val captureUntil = utc(source.captureUntilUtc)
    ensureResponse(!canExecute && context.securityRevokedAtUtc == null &&
        source.contributorUserId == identity.userId && source.originPersonalSessionId == identity.familyId &&
        receipt.version == 1 && receipt.userId == identity.userId && receipt.personalSessionId == identity.familyId &&
        receipt.environmentId == identity.environment && verified == utc(source.issuedAtUtc) &&
        family == session.familyExpiry && family == utc(source.personalSessionExpiresAtUtc) &&
        until > verified && until <= minOf(verified.plusSeconds(72 * 3600L), family) &&
        captureUntil <= until && currentTime < captureUntil && verified <= currentTime.plusSeconds(2))
    return StoredWorkSource(source, proofSecret, context.issuedAuthVersion, context.revision)
}

// Issuance serializes server ticks before the PostgreSQL row is read again at microsecond precision.
internal fun matchesStoredTimestamp(stored: String, returned: String): Boolean {
    val original = utc(stored)
    val observed = utc(returned)
    return observed == original || observed == original.truncatedTo(ChronoUnit.MICROS)
}
