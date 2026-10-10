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

Phase 1G persists `UserId` as the active IPQA relationship and retains `AssigneeKey` as a historical username snapshot. Enabled plans require one active assignee. `UserId` remains nullable only for pre-1G development rows; those rows are not linked to invented users.

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

Phase 1F adds an index on `(Status, CompletedAtUtc)` for the read-only Completed Records ledger. A completed record is the existing task plus its final approved immutable submission and review; no separate Completed Record table is created.

### PatrolTaskItem / PatrolTaskItemSample
- `PatrolTaskItem` freezes the full ordered Standard Item definition, including type, limits, sample count, and photo requirement. It also holds mutable N/A answer, current judgment, item completion time, and optional execution metadata. N/A decisions receive an item completion time even though they have no samples. `(PatrolTaskId, SequenceNo)` is unique.
- `PatrolTaskItemSample` holds ordered current numeric value or qualitative judgment, calculated quantitative judgment, and server inspection time. Identical temporary saves preserve that time; changing or clearing the effective input updates or clears it. `(PatrolTaskItemId, SequenceNo)` is unique. At most 50 samples are accepted per item.

## Approval / audit

### PatrolTaskSubmission / SubmissionItem / SubmissionSample
Each successful submit persists a numbered immutable revision with Shift, overall result, server identity/time, every item N/A/judgment/inspection-time/metadata, and ordered sample values/results/times. `(PatrolTaskId, RevisionNo)` and `(PatrolTaskSubmissionItemId, SequenceNo)` are unique. Current draft changes after rejection do not change earlier submissions.

### PatrolTaskReview
Each approval or reasoned rejection stores the revision number, authenticated reviewer username, decision, optional/required reason, and review time. `(PatrolTaskId, RevisionNo)` is unique and protects competing decisions. Batch approval uses one transaction for all selected tasks.

### Phase 1G persisted identity
`ApplicationUser` has a GUID ID, username/unique normalized username, display name, password hash, Admin/PQE/IPQA role, active and forced-password-change flags, auth version, failed-attempt/lockout state, and created/updated timestamps and usernames. `RefreshTokenSession` has a GUID ID, User ID, unique SHA-256 token hash, auth version, created/expiry/revoked times, optional replacement session ID, and user agent. User foreign keys on Plan assignees, Tasks, and sessions restrict deletion; no user hard-delete API exists. Task `AssignedInspectorUserId` is nullable solely for pre-1G compatibility; new generated tasks require a valid user.

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
