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

Phase 1D persisted task shells. Phase 1E adds immutable Plan/Standard header snapshots and ordered item definition snapshots at generation, plus mutable Shift, result, status, timestamps, and current revision. `(PatrolPlanId, ScheduledOccurrenceUtc)` remains unique. Pre-1E local shells have null snapshot headers and are not eligible for submission; development data should be reset rather than backfilled with the current Standard.

Suggested status state values:
- PendingInspection
- InProgress
- PendingApproval
- Rejected
- Completed

Resubmission transitions `Rejected -> PendingApproval`; draft correction may retain `Rejected` for the IPQA re-inspection queue.

### PatrolTaskItem / PatrolTaskItemSample
- `PatrolTaskItem` freezes the full ordered Standard Item definition, including type, limits, sample count, and photo requirement. It also holds mutable N/A answer, current judgment, and optional execution metadata. `(PatrolTaskId, SequenceNo)` is unique.
- `PatrolTaskItemSample` holds ordered current numeric value or qualitative judgment, calculated quantitative judgment, and server inspection time. `(PatrolTaskItemId, SequenceNo)` is unique. At most 50 samples are accepted per item.

## Approval / audit

### PatrolTaskSubmission / SubmissionItem / SubmissionSample
Each successful submit persists a numbered immutable revision with Shift, overall result, server identity/time, every item N/A/judgment/metadata, and ordered sample values/results. `(PatrolTaskId, RevisionNo)` and `(PatrolTaskSubmissionItemId, SequenceNo)` are unique. Current draft changes after rejection do not change earlier submissions.

### PatrolTaskReview
Each approval or reasoned rejection stores the revision number, server-side reviewer identity, decision, optional/required reason, and review time. `(PatrolTaskId, RevisionNo)` is unique and protects competing decisions. Batch approval uses one transaction for all selected tasks. Development currently stores the role fixture as reviewer; persistent users are deferred to Phase 1G.

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
