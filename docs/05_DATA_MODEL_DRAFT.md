# 05 — Data Model Draft

This is a domain draft, not a final migration/schema script.

## Core identity

### User
- Id
- EmployeeId / Username
- DisplayName
- Role (`Admin`, `PQE`, `IPQA`)
- IsActive
- CreatedAtUtc
- UpdatedAtUtc

## Patrol Standard

### PatrolStandard
- Id
- StandardNo (optional/system-generated)
- PatrolStandardName **required**
- FactoryCode
- FactoryName
- WorkshopCode
- LineCode
- LineName **required**
- MaterialCode
- CreatedByUserId
- CreatedAtUtc
- UpdatedByUserId
- UpdatedAtUtc

### PatrolStandardItem
- Id
- PatrolStandardId
- SequenceNo
- ProcessCode
- ProcessName
- InspectionItemCategory
- InspectionItem
- InspectionContent
- UpperLimitOperator
- UpperLimitValue
- LowerLimitOperator
- LowerLimitValue
- InspectionType
- SamplingPlan
- SampleCount
- PhotoRequirement
- DefectLevel

## Patrol Plan

### PatrolPlan
- Id
- PlanNo
- PlanName
- PatrolStandardId
- FactoryCode
- FactoryName
- LineCode
- LineName
- IsEnabled
- EffectiveStartUtc
- EffectiveEndUtc
- ScheduleExpression / ScheduleDefinition
- TimeZoneId
- GenerationNotBeforeUtc (exclusive boundary after creation/edit/re-enable)
- CreatedByUserId
- CreatedAtUtc
- UpdatedAtUtc

### PatrolPlanAssignee
- Id
- PatrolPlanId
- UserId (IPQA)
- IsActive

Phase 1D persists a stable development `AssigneeKey` instead of `UserId`; the production user relationship is deferred to Phase 1G. Enabled plans require one active assignee.

## Patrol Task

### PatrolTask
- Id
- TaskNo
- PatrolPlanId
- PatrolStandardId
- AssignedInspectorUserId
- ScheduledOccurrenceUtc
- GeneratedAtUtc
- StartedAtUtc
- SubmittedAtUtc
- CompletedAtUtc
- Status
- OverallInspectionResult
- CurrentRevisionNo
- CreatedAtUtc
- UpdatedAtUtc

Phase 1D persists only the generated task shell: `Id`, `TaskNo`, `PatrolPlanId`, `PatrolStandardId`, `AssignedInspectorKey`, `ScheduledOccurrenceUtc`, `GeneratedAtUtc`, `GenerationSource`, `Status=PendingInspection`, `CreatedAtUtc`, and `UpdatedAtUtc`. The remaining draft task fields and child entities belong to later phases. `(PatrolPlanId, ScheduledOccurrenceUtc)` is unique.

Suggested status state values:
- PendingInspection
- InProgress
- PendingApproval
- Rejected
- Completed

Resubmission can transition `Rejected -> InProgress/PendingApproval` while preserving review/revision history.

### PatrolTaskItem
- Id
- PatrolTaskId
- StandardItemId
- SequenceNo
- InspectionValueText
- InspectionValueNumeric
- InspectionResult
- ReinspectionResult
- InspectedAtUtc
- AbnormalCode
- AbnormalType
- AbnormalReason
- AbnormalDescription
- UpdatedAtUtc

## Approval / audit

### PatrolTaskReview
Immutable review records:
- Id
- PatrolTaskId
- RevisionNo
- ReviewerUserId
- Decision (`Approved`, `Rejected`)
- Reason (required for rejection)
- ReviewedAtUtc

### PatrolTaskRevision
Recommended if inspection edits must be fully auditable:
- Id
- PatrolTaskId
- RevisionNo
- SubmittedByUserId
- SubmittedAtUtc
- SnapshotJson or normalized revision relationship

Whether to persist full item snapshots per revision can be finalized before backend implementation. At minimum, rejection and resubmission events must not overwrite audit history.

## Attachments

### InspectionAttachment
- Id
- PatrolTaskId
- PatrolTaskItemId (nullable for task-level file)
- StorageProvider
- StorageKey
- OriginalFileName
- ContentType
- SizeBytes
- Sha256
- UploadedByUserId
- UploadedAtUtc

Do not store image binary/blob contents in the main relational database.
