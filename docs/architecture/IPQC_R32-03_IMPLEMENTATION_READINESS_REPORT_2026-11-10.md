# R32-03 implementation-readiness audit and next implementation prompt

Date: 2026-11-10. Repository: peterl-ai/IPQC-system.

**Status: completed read-only application audit; proposed implementation scope and acceptance cases, NOT RUN.** Peter authorized merging R32-02 and publishing this completed report to main, not implementing R32-03. No R32-03 application code, migration or implementation branch was created. Architecture Rev3.2 and its Handoff Supersession remain approved and unchanged. The implementation prompt below requires Peter's separate implementation authorization.

## 1. Verified merge and audit baseline

| Item | Observed result |
| --- | --- |
| R32-02 PR | [#13](https://github.com/peterl-ai/IPQC-system/pull/13), MERGED |
| Reviewed/current PR HEAD before merge | `bfe9758353fdcec13fb09a3a6e53045280d03208`, unchanged |
| Final pre-merge checks | Six backend/frontend/PostgreSQL push/PR checks successful; mergeable/CLEAN; no submitted blocking reviews or unresolved review threads |
| Protection | Main protection was not configured; applicable rules empty; no override/bypass used |
| Scope | Same reviewed 30 files; only the approved R3202 migration/Designer/snapshot changes; no unexpected additions |
| Merge convention / time | Merge commit, expected-head guard, `2026-11-10T22:49:47Z` |
| Merge SHA / audited source main | `69c800c010f03f4450876ca6f8c93525bb2d681e` |
| Merge parents | `d6dc88a93dc8b7ff3e8a6c8293eba2a79c20c05c`, `bfe9758353fdcec13fb09a3a6e53045280d03208` |
| Local main | Safely fast-forwarded to the merge SHA; unrelated untracked work preserved |
| R32-02 contract | `docs/16_R3202_PERSONAL_ACCESS.md` verified tracked on merged main |
| Post-merge CI | [Run 42918109084](https://github.com/peterl-ai/IPQC-system/actions/runs/42918109084), all three jobs PASS: backend 239 passed / 0 failed / 4 skipped; separate real PostgreSQL 4 passed / 0 failed / 0 skipped; Web 70 passed |
| Production | No deployment, production migration, cleanup activation or configuration change |

The four backend skips are the opt-in live PostgreSQL class without a local/server opt-in in that job; the separate PostgreSQL job actually executed those four cases. None of these R32-02 results proves the R32-03 cases proposed here. Publishing this report will add a documentation-only main commit; its independently verified SHA/permalink is supplied in the completion report, distinct from the source audit/merge SHA above.

## 2. Requirements and smallest practical PR

Read AGENTS.md, CLAUDE.md, README.md, the approved [Rev3.2 architecture](IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md), [Handoff Supersession](IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md), [R32-02 readiness report](IPQC_R32-02_IMPLEMENTATION_READINESS_REPORT_2026-11-10.md), [merged R32-02 contracts](../16_R3202_PERSONAL_ACCESS.md), and the actual Plan/Standard/scheduler/task/read/auth/model/migration/Web/test sources described below. Historical instructions to begin older phases are historical; Peter's current instruction controls this audit/publication. No repeat architecture sign-off is requested.

Recommend **one scoped R32-03 PR**: explicit shared Plan configuration, ownerless server generation, immutable versioned planning snapshots, additive personal task-read metadata and a small bilingual shared-Plan panel within the existing Plans module. Use reviewable commits for schema/contracts, transactional services/generation, then UI/tests/evidence. No separate Android increment or inspection-state implementation is needed.

A migration is necessary. The smallest proposed extension is three nullable columns, compatible checks and one admitted-task unique index (§6); use existing Plan schedule/effective fields and existing frozen Task/header/item tables. A versioned JSON planning snapshot avoids a new Plan-revision-history platform and many duplicated scalar columns. It contains metadata only, not image bytes or credentials. Do not introduce an exclusion-extension dependency, distributed scheduler platform, device trust, WorkContext or draft/Submit schema.

Generation-enabled means scheduling only. Every new task remains PendingInspection with no personal owner, no answers/submissions, and `canExecute=false`. This is not inspection activation. Future F1/F2/F3, exact forms/states and PQE submitted-history APIs remain separately required.

## 3. Confirmed existing-file findings

Paths are repository-relative. Findings are source observations/inferences, not reproduced R32-03 race results.

| Actual file | Finding / required impact |
| --- | --- |
| `api/JaxPower.Ipqc.Api/Contracts/PatrolPlanContracts.cs` | PlanInput is legacy-assignee/generic-schedule based, without WorkflowVersion/LineId/Revision. New shared DTOs must be separate. |
| `api/JaxPower.Ipqc.Api/Endpoints/PatrolPlanEndpoints.cs` | Existing PlansRead/Write belongs to Admin/PQE. Legacy list/detail isolate version1; mutation filter checks workflow before legacy assignee validation. Preserve these endpoints and statuses. |
| `api/JaxPower.Ipqc.Api/Services/PatrolPlanService.cs` | Enabled legacy plans require one active IPQA; generic recurrences and exclusive GenerationNotBefore boundary exist. Conditional legacy claims protect workflow, but there is no shared configuration revision/overlap protocol. |
| `api/JaxPower.Ipqc.Api/Domain/PatrolPlan.cs` | Contains Plan, Task and item/submission classes. Plan has no revision. Task already owns frozen WorkflowVersion/LineId, occurrence and header/items. Shift is a mutable legacy draft/submission field, not a generation-time round snapshot. |
| `api/JaxPower.Ipqc.Api/Services/PatrolTaskGenerationService.cs` | Poll loads enabled plans and caches Plan/assignee/Standard; unsupported workflow is skipped. Insertion rechecks only legacy workflow, LineId and IsEnabled, not every mutable scheduling/source field. This documented legacy stale-configuration race remains; the new shared path must reload/revalidate all generation facts inside its transaction. |
| `api/JaxPower.Ipqc.Api/Services/PatrolSchedule.cs` | Existing New York local-time normalization, invalid-DST rejection, earlier-instant ambiguous-time rule, bounded UTC occurrence enumeration and uniqueness-by-SortedSet are reusable. Preserve arbitrary legacy daily/weekly/hourly recurrences. |
| `api/JaxPower.Ipqc.Api/Services/PatrolStandardService.cs` | Update loads header/children before beginning its transaction, shifts sequence numbers, then reconciles children across saves. Shared generation must not read a hybrid header/item version. New shared planning and existing Standard update/delete need a common serialization boundary and fresh in-transaction reads; retain explicit Add for new GUID children. |
| `api/JaxPower.Ipqc.Api/Program.cs` | Standard PUT has an enabled-Plan/ExecutabilityIssue pre-check outside its service transaction. It is not sufficient for enable-versus-Standard-edit races. New shared guard must also run inside the common write transaction. |
| `api/JaxPower.Ipqc.Api/Services/InspectionJudgmentService.cs` | Reusable definition checks: known qualitative/quantitative type, missing sample count defaults1, count1..50 and parseable numeric limits. Its Calculate/Overall implement legacy N/A/results and must not be used as a new shared execution engine. |
| `api/JaxPower.Ipqc.Api/Services/ProductionLineService.cs` | Admin mutations use ProvisioningStore, revisions and append-only line audit. Deactivation rejects existing device reservations. It does not currently fence Plan generation across inactive intervals. Preserve the reservation rule; add only shared-Plan generation-boundary/revision fencing on real activity transitions (§5). |
| `api/JaxPower.Ipqc.Api/Services/ProvisioningStore.cs` | SQLite non-deferred writer transaction; PostgreSQL advisory lock49505102. Preserve this transaction/audit contract; do not make personal planning depend on device reservations. |
| `api/JaxPower.Ipqc.Api/Services/PersonalSessionStore.cs`; `Security/PersonalAccess.cs` | Current-family checks and user-row security serialization exist. New shared management routes need current family plus Admin/PQE permission, not the IPQA-only business helper. Actor authority must be rechecked under an account lock for new configuration writes. |
| `api/JaxPower.Ipqc.Api/Data/IpqcDbContext.cs` | Existing workflow/line checks and restrictive FKs; unique task key is (PatrolPlanId, ScheduledOccurrenceUtc), which cannot prevent two different plans generating the same line occurrence. No shared admission/snapshot marker exists. |
| `api/JaxPower.Ipqc.Api/Services/LinePatrolReadService.cs` | Personal task projection is deliberately non-executing. It filters persisted line/version before paging and rejects unsupported state/snapshots. Compatible older shared shapes can currently return FoundationOnly without a new planning-contract marker; do not relabel them as new generation output. |
| `api/JaxPower.Ipqc.Api/Contracts/LinePatrolContracts.cs` | Existing summaries/details/definitions are reusable; extend additively with nullable versioned planning metadata, preserving old fields and personal access markers. |
| `web/src/features/patrol-plans/PatrolPlansPage.vue`; `api.ts`; `validation.ts` | Current UI/API types are legacy-only, with free-text line and assignee/generic recurrence controls. Actual types are in api.ts; no patrol-plans/model.ts exists. Add a separate shared panel/client, not a reinterpretation of existing forms. |
| Existing tests / CI | PatrolPlansAndSchedulerTests already cover legacy CRUD, DST, catch-up, Standard changes, concurrent workers and unique occurrence; WorkflowLineFoundationTests cover adversarial shared rows/owner guards and frozen identity. R32-02 added disposable PostgreSQL infrastructure, but CI currently filters only PersonalSessionPostgresTests: extend the filter explicitly for the new live class. |

Source inspection does not establish what rows exist in a deployed database. No production/local application database was queried. A read-only deployed inventory remains an external activation gate: list existing shared Plans/Tasks, versions, markers, line/occurrence duplicates and inconsistent snapshots. Never infer historical LineIds from text or repair historical shared rows during this migration.

## 4. Proposed APIs, DTOs and configuration rules

### 4.1 Management API and RBAC

Use a new `/api/line-patrol-plans` namespace, separate from legacy `/api/patrol-plans` and IPQA `/api/line-patrol`:

| Route | Proposed contract |
| --- | --- |
| GET `/lines` | Current personal family plus PlansRead; two configured active lines with actual id/code/name/order. PQE does not gain Admin ProductionLines.Read or device/audit access. |
| GET root | Current family plus PlansRead; paged shared Plan metadata, optional lineId/planNo/name/enabled filters before total/page; include historical unadmitted rows as read-only, with a truthful configuration status. |
| GET `/{id}` | Same read policy; explicit shared configuration/revision/source metadata; legacy target404. Existing admitted/off-configuration plans remain diagnosable without inventing eligible lines. |
| POST root | Current family plus PlansWrite; explicit new version2 Plan, revision1; no assignee rows. Default UI is scheduling-disabled; an explicit enabled create runs the full admission/overlap checks. |
| PUT `/{id}` | Same write policy; complete new shared input plus expectedRevision. Re-read/CAS workflow2, admitted revision and current line. Future-only configuration change; no Task rewrites. |
| POST `/{id}/enable` or `/disable` | Same write policy, expectedRevision required; stale409. Disable must remain available for an admitted Plan whose line is now inactive/outside configuration, after current user/family authorization. |

No shared hard-delete/conversion/repair endpoint is needed in the smallest PR. Retain legacy Delete as-is; shared history is kept by disabling. Do not accept caller-supplied actors, workflow conversion, task snapshots or assignees. Use strict new input contracts and reject unsupported fields instead of implying they were applied.

Input: optional bounded PlanNo, required bounded PlanName, PatrolStandardId, actual LineId, nonempty unique rounds subset of Day/Night, effectiveStartLocal and optional effectiveEndLocal (`yyyy-MM-ddTHH:mm`), and isEnabled. Update adds positive expectedRevision. Retain installation-wide PlanNo uniqueness using the existing index and server-generated PLN identifier when omitted. TimeZone and round times are server constants: America/New_York, Day07:00, Night19:00. Default round selection is both; separate Day-only and Night-only Plans are allowed and do not overlap merely by sharing a line. Store the canonical subset in existing ScheduleDefinition as the corresponding supported daily times; do not add an independent cron/timezone editor or a second round mask column.

Use existing PlansRead/PlansWrite roles, each combined with R32-02 current-family validation in non-IPQA mode. Anonymous/invalid/currently revoked JWT401; authenticated wrong role/forced-change403; missing/invalid family401 after normal online reauthentication; missing environment/line configuration503 on affected operations. Older JWTs retain all old endpoints, but cannot silently enter a new shared route. Missing target404; legacy target through the new namespace404; historical unadmitted shared mutation409 SharedPlanUnadmitted; stale revision/overlap409; invalid input400. Resource/workflow checks must not be masked by legacy assignee validation. The existing legacy endpoints retain their own 401/403/404/409 ordering.

Recheck the new write actor's active state, AuthVersion, Admin/PQE permission and current family/environment/expiry inside the write transaction. Do not trust only a pre-read permission claim. Preserve server username audit fields and add structured, secret-free PlanId/revision/actor-ID diagnostics; do not fabricate historical stable actor IDs or misuse provisioning audit aggregates for Plans. No new login/session platform or Admin session console.

### 4.2 Plan validity, overlap and generation boundaries

Create/update/enable validates both configured IDs exist and are active, the chosen line is one of them, the Standard exists and an enabled Plan has nonempty ordered valid measurement definitions. Disabled draft Plans may retain an incomplete Standard, but cannot be enabled/generated until it is ready. Preserve the 1000-item technical limit and 1..50 sample rules; approximately28 is not a limit. Freeze raw PhotoRequirement and other source text; do not convert unknown photo strings to Optional or claim future inspection/evidence readiness.

Effective intervals retain the existing inclusive UTC end convention after New York conversion; end must be strictly later than start when supplied. Reject nonexistent local times; ambiguous effective-bound times choose the existing earlier UTC instant. For enabled admitted shared Plans on the same actual line, intersecting selected rounds and intersecting effective intervals conflict: startA <= endB AND startB <= endA, with null end unbounded. Exclude self on update. Checking only existing task rows or a pre-transaction overlap query is insufficient. Different rounds or disjoint intervals may coexist. Do not impose these new restrictions on legacy personal Plans.

SharedPlanRevision is monotonic and used as an explicit concurrency token/CAS. Effective configuration changes increment it; source/line/round/schedule/effective-window changes or disabled-to-enabled also advance GenerationNotBeforeUtc to at least current server UTC, so disabled/old-configuration time is not reinterpreted. Name/label-only edits increment revision and affect future snapshots without changing historical task bytes. No-op enable/disable must still validate the expected revision; a no-op must not keep resetting the generation deadline.

Use UTC instants normalized to PostgreSQL-compatible microsecond precision for new shared planning on both providers; do not alter legacy precision. Newly created explicitly enabled Plans may include an occurrence exactly at creation: normalize CreatedAtUtc to server UTC microseconds, use a creation boundary one microsecond earlier, and also require occurrence >= CreatedAtUtc. Later mutation/reenable boundaries are strictly exclusive. Clock rollback must never move an existing boundary backwards. Do not rely on a 100ns tick surviving PostgreSQL storage.

## 5. Provider-specific consistency and scheduler protocol

### 5.1 Small shared-planning transaction store

Introduce a separate LineSharedPlanningStore for low-volume shared planning/generation and coherent existing Standard update/delete. Do not repurpose the personal-session or provisioning stores. Proposed PostgreSQL transaction-scoped shared-planning gate: `pg_advisory_xact_lock(495051, 3203)` (two-int namespace, distinct from existing single-key provisioning lock). All participating shared configuration, generation and Standard mutation code paths must use it; an advisory lock ignored by a writer gives no protection. SQLite instead acquires its non-deferred writer transaction before authoritative reads, never PostgreSQL row-lock SQL.

For new user-authored Plan writes, PostgreSQL lock order is actor account **FOR NO KEY UPDATE**, then shared-planning gate, then affected ProductionLine rows in sorted ID order, then Plan, then source reads/snapshot insert. Revalidate family/role under the actor lock. Existing PersonalSessionStore FOR UPDATE security mutations conflict with this account lock. NO KEY UPDATE avoids unnecessarily blocking the actor FK KEY SHARE checks in the existing provisioning audit path; blindly holding actor FOR UPDATE while waiting for a provisioned line can create a cycle with its audit insert. System generation has no personal actor lock; Standard legacy mutations retain existing JWT compatibility and use the planning gate before loading/mutating source rows. Do not call session/provisioning mutation services while holding the planning gate.

Use explicit bounded lock/busy/serialization/deadlock handling and whole-transaction rollback/retry, no success before commit and no 401 for database contention. Configuration conflicts are409; exhausted infrastructure contention503. Cancellation, failed item insert, snapshot serialization failure or unique conflict cannot leave a shell or partial child set. PostgreSQL and SQLite must prove the protocol independently.

Provider mechanics were checked against [PostgreSQL explicit locking](https://www.postgresql.org/docs/current/explicit-locking.html). The selected account-lock compatibility and full application lock graph remain proposed and require real-server tests; this reference is not proof of implemented concurrency.

### 5.2 Standard and line integration are necessary, bounded changes

Move Standard Update/Delete authoritative loads into the common planning transaction. Keep the current child-ID reconciliation, temporary sequence shift and two-save atomicity, and existing API/Excel contracts. An enabled admitted shared Plan and a Standard edit that would make it invalid must serialize: either enable sees the invalid source and fails, or the edit sees the enabled dependent Plan and fails atomically. Retain existing legacy executability validation; the endpoint pre-check alone is not authoritative. A valid Standard edit does not rewrite existing tasks or invent a historical Standard version. Catch-up tasks freeze the latest coherent Standard **at generation time**, recording source time and exact definitions; they do not claim to reconstruct the Standard as it existed at an earlier occurrence.

Preserve ProductionLineService's Admin permissions, revisions, normalization, reservation-based deactivation rule and append-only line audit. On an actual active/inactive transition, in that same existing provisioning transaction, monotonically fence GenerationNotBeforeUtc for admitted shared Plans of the line and increment their shared revisions. Otherwise reactivation could backfill the inactive interval. Keep actor/time metadata truthful and correlate diagnostics with the existing line event/revision; audit failure must roll back the line and every Plan fence. No-op activity requests do not repeatedly fence; line rename affects future snapshots only. Do not add an active-Plan deactivation veto, release reservations, remove device history or change legacy Plan generation boundaries.

PostgreSQL shared workers lock the line row before Plan claim, coordinating with the actual line UPDATE in the existing provisioning transaction, without acquiring its advisory lock. If generation commits first, its old active-line snapshot remains historical; if deactivation commits first, generation skips it. Line activation later requires a fresh Plan reread/boundary. SQLite's same-database writer reservation serializes both stores. Preserve lock order line before Plan fences; test the same Admin doing line and Plan operations concurrently with provisioning audit insertion.

### 5.3 Server generation and uniqueness

Keep the current legacy generator path and all owner/schedule guards. Add a separate LineSharedTaskGenerationService dispatched by the existing scheduler; no second independently polling worker or public generation endpoint is required. If the new configuration is absent/invalid, shared generation logs a bounded truthful skip while legacy generation still runs. Scheduler.Enabled and existing poll/catch-up settings remain authoritative.

Poll only candidate IDs, not authoritative cached Plan/Standard objects. Each short occurrence transaction acquires the planning gate/line lock, reloads Plan and source, samples server now inside the transaction, validates configured active line, workflow2, positive admission revision, enabled state, canonical fixed rounds, dates, boundary and complete definitions. Compute/recheck occurrences using the current values. A pre-read Plan move/revision change requires reread/retry, never reuse of its cached identity. At insertion conditionally claim the same workflow/revision/line/enabled Plan in the database; locks and CAS are complementary. Do not increment configuration revision just because generation succeeded.

Existing catch-up default24h, clamped1..168h, is retained. Candidate o must satisfy: effectiveStart <= o <= effectiveEnd if present, now-window <= o <= fresh server now, o > GenerationNotBefore, and o >= creation time for new Plans. No future task, disabled-period backfill or resurrection of an old configuration. One malformed Plan or historical occurrence conflict must not stop later valid plans. Two lines with both rounds covered normally produce four tasks/day across the plant, independent of logged-in users/devices.

Keep existing unique (PlanId, ScheduledOccurrenceUtc). Add a partial unique (LineId, ScheduledOccurrenceUtc) for workflow2 tasks marked PlanningSnapshotVersion1; fixed rounds map to distinct instants, so this enforces one admitted task per actual line/round occurrence across different Plans. An existing admitted line occurrence is an idempotent winner even if its source Plan is now disabled/replaced. An **unadmitted historical shared** row at that line/occurrence is a HistoricalOccurrenceConflict: skip/log for explicit investigation, do not adopt, overwrite, convert or create a competing admitted copy. The index excludes those historical rows so migration does not rewrite or fail on old duplicate groups. Application checks of those rows run under the shared protocol; no public API may manufacture them. Legacy version1 rows remain their own workflow and are not silently merged or disabled.

## 6. Frozen snapshot and migration proposal

### 6.1 Versioned planning snapshot

Add Task.PlanningSnapshotVersion nullable integer and Task.PlanningSnapshotJson nullable text. For new shared generation only, version1 JSON freezes:

- Kind PlanningDefinitionV1; source PlanId and positive SharedPlanRevision; PlanNo/name, schedule/round selection, effective bounds and generation boundary.
- Actual WorkflowVersion2, Task.LineId, trusted ProductionLine.Code/Name/Revision at generation; never legacy text inferred as identity.
- Exact scheduled UTC occurrence, stored local calendar date, selected Day/Night round, America/New_York timezone and the UTC offset observed for that occurrence; do not reuse mutable Task.Shift.
- Source StandardId/source UpdatedAtUtc and relevant header/policy text, consistent with existing Task header/ordered item-definition snapshots. Existing item rows retain stable task/source item IDs and complete raw definition strings. No answers, samples, inspections, owner, Submit, photo readiness or contribution context is created.

Existing Task.LineId/WorkflowVersion/PlanId/StandardId/occurrence remain the authoritative relational facts. JSON is compared against them; it cannot override access or move a task. Use an explicit versioned serializer/parser and reject malformed/unknown/inconsistent marked snapshots. Require positive source revisions and prove stored local date + fixed round time - frozen offset equals the relational scheduled UTC instant. Reads use the frozen timezone/offset facts rather than recalculating historical labels from later timezone-rule updates. This is planning metadata, not F2 canonical Submit content, a cryptographic capability or proof of execution readiness. Keep existing Task.Shift null for new planning-only tasks. AssignedInspectorUserId=null and AssignedInspectorKey="" satisfy the current schema without a fictitious person.

All frozen header/item/JSON facts are written in the same task insert transaction and never rebuilt from current parents. Later Plan moves/edits, Standard reorder/delete/edit, line rename/deactivation and user changes must leave them byte/value unchanged. New public APIs accept no client-controlled snapshot marker/JSON. No broad task-identity trigger/refactor of legacy writes is needed in this phase; existing shared write restrictions stay closed.

### 6.2 Necessary additive migration

Recommend one future migration named `R3203LineSharedPlanningSnapshots`, timestamp generated only after separate implementation approval, following the nine current migrations ending `20261110215126_R3202PersonalSessionReadFoundation`:

| Addition | Required behavior |
| --- | --- |
| PatrolPlans.SharedPlanRevision nullable bigint / SQLite INTEGER | Null for every historical row; positive only for newly admitted shared configurations. Concurrency token, no default/backfill. |
| PatrolTasks.PlanningSnapshotVersion nullable integer | Null for every historical row; supported value1 only for newly generated planning definitions. |
| PatrolTasks.PlanningSnapshotJson nullable text | Null historical values; bounded validated metadata, no binary/secrets. |
| Plan check | Revision null OR workflow2 and revision>=1. Do not require old shared rows to acquire a revision. |
| Task check | Both new fields null, OR workflow2/version1/nonempty JSON and no personal assignee. Unknown marked versions/inconsistent nullable pairs rejected. |
| Partial unique index | (LineId, ScheduledOccurrenceUtc) WHERE WorkflowVersion=2 AND PlanningSnapshotVersion=1. Keep both existing occurrence and paging indexes/FKs. |

No new table, copied historical identity, rewritten old migration, backfilled snapshot, automatic enrollment cleanup or change to existing owner columns. JSON structural/relational equality validation remains application-side; do not depend on different provider JSON functions or extensions for admission.

Adding checks means **SQLite Plan/Task table rebuilds may be necessary** even though the business change is additive; do not promise an ALTER-only migration. EF must preserve every original column, FK, check and index, including dependent items/submissions/reviews. Review generated SQL and actual populated upgrades. PostgreSQL uses native nullable columns/checks/index, with planned lock duration/backup review. This follows [EF SQLite migration limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations); no migration was scaffolded or executed in this audit.

Retain provider-default snapshot normalization and historical long constraint names; run both pending-model checks without suppression. Down must not erase configuration/snapshot history. Operational rollback retains schema, disables shared scheduling/new Plan writes if necessary, and never reverts to a binary that treats every workflow2 row as eligible. Legacy scheduling can remain available. DB restore/replay, duplicate repair and actual production migration require their own operating controls; no action on a production database is authorized here.

## 7. R32-02 DTO compatibility and Web boundary

Extend LineTaskSummary additively with nullable PlanningSnapshotVersion, SourcePlanRevision, RoundSnapshot and ScheduledLocalDateSnapshot, plus PlanningSnapshotStatus. Add nullable typed PlanningSnapshot to LineTaskDetail. Existing IDs, fields, pagination and validatedAccess markers remain. States:

- HistoricalUnadmitted: both new columns null; preserve R32-02's existing FoundationOnly old-subset read behavior when its original state/definition checks pass; new metadata stays null. Never fill it from today's Plan/Shift/Standard or classify it as a new planning snapshot.
- ValidV1: a coherent newly generated planning snapshot. Existing ReadFoundationStatus remains FoundationOnly and canExecute=false; validity describes this planning contract only.
- Invalid: marked but malformed/unsupported/inconsistent. List includes the row once with unavailable diagnostics; detail409 TaskSnapshotUnavailable. Existing legacy-answer/submission/incompatible-state rejection remains.

No synthetic DraftVersion, inspection-attempt IDs, future result mapping or legacy final-Inspector interpretation. New reads still require current eligible IPQA family/environment and actual configured Task.LineId; filter before totals. Keep Admin/PQE off those IPQA routes. The small configuration panel manages Plans only; formal PQE shared task/revision/photo/history/report APIs remain later scoped work and must exist before actual Submit activation. R32-03 cannot produce those submissions and does not weaken their approved visibility requirement.

Add a lazily loaded shared-Plan panel/tab under the existing Admin/PQE Plans route. Old legacy form/table/client continue unchanged, including assignee and arbitrary recurrence controls. Shared UI uses actual server line IDs, Day/Night selections with fixed times, expected revision, create/update/enable/disable, configuration/historical status and an explicit planning-only notice; no assignee, workflow-conversion, free cron, task execution or shared Delete button. A new-feature configuration/reauthentication error must not break the legacy tab. Render all visible notices/errors in EN/ZH using stable API codes; no raw English backend message as the new UI contract. On409 refresh/reconcile visibly, never silently retry with a newer revision or overwrite the form.

## 8. Exact proposed file impact

This is an implementation inventory, not a claim these files were edited during the audit. Paths below are repository-relative.

| Existing application file | Bounded proposed change |
| --- | --- |
| api/JaxPower.Ipqc.Api/Domain/PatrolPlan.cs | Nullable shared Plan revision and two planning-snapshot Task fields only |
| api/JaxPower.Ipqc.Api/Data/IpqcDbContext.cs | Revision concurrency/checks/partial occurrence index |
| api/JaxPower.Ipqc.Api/Data/Migrations/IpqcDbContextModelSnapshot.cs | Additive target model; preserve both-provider normalization |
| api/JaxPower.Ipqc.Api/Services/PatrolTaskGenerationService.cs | Dispatch the separate shared generator while retaining legacy behavior/guards |
| api/JaxPower.Ipqc.Api/Services/PatrolStandardService.cs | In-transaction fresh reads, common planning serialization, atomic enabled-shared readiness protection; preserve child identities/reorder |
| api/JaxPower.Ipqc.Api/Services/ProductionLineService.cs | Only admitted shared boundary/revision fences within existing activity-change/audit transaction |
| api/JaxPower.Ipqc.Api/Services/LinePatrolReadService.cs | Add coherent marked snapshot reads and historical-null distinction; no permission broadening |
| api/JaxPower.Ipqc.Api/Contracts/LinePatrolContracts.cs | Nullable additive planning metadata/typed detail |
| api/JaxPower.Ipqc.Api/Program.cs | Register/map new services/routes and shared-domain error handling; preserve legacy Standard validation/statuses and auth-before-binding |

| New application file | Purpose |
| --- | --- |
| api/JaxPower.Ipqc.Api/Contracts/LineSharedPlanContracts.cs | Shared inputs, revision/state/list/detail; separate from legacy DTOs |
| api/JaxPower.Ipqc.Api/Contracts/PlanningSnapshotContracts.cs | Explicit metadata version1 serializer/read shape |
| api/JaxPower.Ipqc.Api/Services/LineSharedPlanningStore.cs | Provider transactions, actor/role/family check, planning gate/line lock/CAS order and bounded errors |
| api/JaxPower.Ipqc.Api/Services/LineSharedPlanService.cs | Admission/read/overlap/configuration mutation; no legacy conversion |
| api/JaxPower.Ipqc.Api/Services/LineSharedTaskGenerationService.cs | Short, fresh-authority occurrence transactions and frozen inserts |
| api/JaxPower.Ipqc.Api/Services/PlanningSnapshot.cs | Server snapshot factory/parser/consistency checks; no execution engine |
| api/JaxPower.Ipqc.Api/Services/ConfiguredLineDirectory.cs | Shared configured-line projection reused by personal reads and permission-separated management callers |
| api/JaxPower.Ipqc.Api/Endpoints/LineSharedPlanEndpoints.cs | Current-family plus PlansRead/Write routes, normal error ordering |
| api/JaxPower.Ipqc.Api/Data/Migrations/<timestamp>_R3203LineSharedPlanningSnapshots.cs | Future single reviewed migration, not generated now |
| api/JaxPower.Ipqc.Api/Data/Migrations/<timestamp>_R3203LineSharedPlanningSnapshots.Designer.cs | Matching target model |

Reuse PatrolSchedule's existing normalization/occurrence functions and InspectionJudgmentService definition validation without changing legacy semantics. PersonalAccess/current permission constants can be composed as-is for management non-IPQA mode; do not add a new role or silently relax IPQA guards. Keep legacy Plan service/contracts/endpoints, task execution/review/report services, ProvisioningStore, account/session service implementations and all nine historical migrations unchanged unless an explicitly demonstrated dependency requires a narrowly explained adjustment.

| Web file | Proposed change |
| --- | --- |
| web/src/features/patrol-plans/PatrolPlansPage.vue | Lazy shared panel entry; old legacy behavior retained |
| web/src/features/patrol-plans/LineSharedPlansPanel.vue (new) | Small bilingual shared configuration UI |
| web/src/features/patrol-plans/lineSharedApi.ts (new) | Strict separate shared contracts/request/error-code mapping |
| web/src/features/patrol-plans/lineSharedValidation.ts (new) | New shared form shape; server authoritative |
| web/src/i18n/locales/en.ts; web/src/i18n/locales/zh-CN.ts | New visible shared configuration/notice/conflict strings |

No new navigation role route, Android file, new task execution page or broad standards editor redesign. Existing patrol-plans/api.ts and validation.ts remain the legacy client.

Test additions: `api/JaxPower.Ipqc.Api.Tests/LineSharedPlanningFixture.cs`, `LineSharedPlanTests.cs`, `LineSharedSchedulerTests.cs`, `LineSharedPlanningConcurrencyTests.cs`, `LineSharedSnapshotTests.cs`, `LineSharedPlanningMigrationTests.cs`, `LineSharedPlanningPostgresTests.cs`; `web/src/features/patrol-plans/LineSharedPlansPanel.test.ts`, `lineSharedApi.test.ts`, `lineSharedValidation.test.ts`, and a small `PatrolPlansPage.test.ts` legacy/new-tab isolation test. Extend existing LinePatrolReadTests, ProductionLineManagementTests and PatrolStandardsApiTests only for additive metadata/atomic fence/Standard integration cases; retain all old assertions. Reuse current disposable PostgreSQL and synthetic account fixtures by composition, without broad renaming or weaker skip rules.

Other intended changes: `.github/workflows/ci.yml` expands the real PG class filter to include LineSharedPlanningPostgresTests while retaining PersonalSessionPostgresTests; README.md, docs/04_ARCHITECTURE.md and docs/10_DEPLOYMENT_CHECKLIST.md receive bounded implemented-contract notes; new docs/17_R3203_SHARED_PLANNING.md records actual later evidence. Do not edit the approved architecture/Handoffs, R32-02 readiness baseline or its historical verification to imply new tests passed.

## 9. Focused acceptance/test matrix

**All 36 R03 cases are proposed / NOT RUN.** Documentation validation and already-green R32-02 CI are not implementation evidence. Assert database/snapshot/audit side effects, not only HTTP codes. Race tests use deterministic barriers/faults and separate connections on each real provider.

| ID | Proposed scenario / required result |
| --- | --- |
| R03-01 | Admin/PQE current-family management succeeds; anonymous/invalid401, IPQA/wrong-role/forced-change403, old/no-family new calls401; old legacy calls unchanged |
| R03-02 | Management two-line directory uses actual configured IDs; missing/invalid/inactive pair503; no PQE Admin/device permission elevation; bad new config leaves legacy scheduling/UI usable |
| R03-03 | Explicit new shared Plan starts admitted revision1, no assignee rows; disabled/incomplete draft cannot generate; enabled create receives full validation |
| R03-04 | Invalid line/Standard/round subset, unsupported schedule/timezone/workflow/assignee fields and duplicate PlanNo rejected with no writes; database uniqueness authoritative |
| R03-05 | Inclusive effective bounds, end<=start, nonexistent/ambiguous local bounds, microsecond-exact creation and later exclusive boundary behavior |
| R03-06 | Same line/round/intersecting enabled interval conflicts; disjoint rounds/intervals allowed, null ends handled; no new legacy overlap restriction |
| R03-07 | Stale/missing/invalid expectedRevision and no-op mutation; database conditions enforce current shared admission/version with no lost update |
| R03-08 | Disable/re-enable and source/line/round/effective edits advance monotonic fence; metadata-only edit/no-op semantics explicit; no disabled/old-configuration backfill |
| R03-09 | Later Plan move/Standard change/line rename cannot rewrite Task relational identity, frozen JSON/header/item bytes or source facts |
| R03-10 | Standard readiness: nonempty valid ordered definitions, default1/count1..50, quantitative limits and 1000-item technical cap; raw unknown photo strings retained, no false inspection readiness |
| R03-11 | Both configured lines/both rounds generate exactly four ownerless tasks per covered day, correct local round/date and UTC, no device/person requirement |
| R03-12 | Spring/fall 07/19 UTC vectors, New York timezone independent of server timezone, legacy 02:30 invalid/01:30 earlier-instant recurrence tests unchanged |
| R03-13 | Restart/catch-up window boundaries/clamp and effective-end inclusivity; long outage bounded, malformed Plan does not stop valid later plans |
| R03-14 | Future occurrence, disabled Plan, inactive/unconfigured line, unknown workflow or unadmitted Plan cannot generate; no client/public generation path |
| R03-15 | Multiple workers/restarts/retries produce one task per Plan occurrence and admitted line occurrence; losing unique insert leaves no shell/children |
| R03-16 | Different/replacement Plans competing for same line occurrence honor one immutable winner; old unadmitted collision skips with diagnostic, no adoption/overwrite |
| R03-17 | Poll cache becomes stale before claim; transaction reload/CAS catches every generation-relevant field, fresh server time sampled under lock |
| R03-18 | Generation versus Plan update/disable/move, both orders: coherent committed old snapshot or eligible new configuration, never mixed/disabled-generation leakage |
| R03-19 | Standard header/item edit/reorder/delete versus generation: complete old or new committed definition, no hybrid; retained GUIDs and two-save rollback |
| R03-20 | Enable versus source becoming invalid, both orders: one valid configuration or clear conflict; direct service path cannot bypass in-transaction readiness |
| R03-21 | Deactivate/reactivate versus generation, no inactive-period catch-up; old reservation409 unchanged; line audit failure rolls back activity and every shared fence/revision |
| R03-22 | New Plan mutation versus actor logout/reset/role/deactivation: shared account lock/current-family recheck serializes; new config cannot commit after security closure wins |
| R03-23 | Same actor concurrently changes line and Plan while audit inserts: test proposed NO KEY UPDATE/key-share lock graph, bounded wait/deadlock handling and no partial write |
| R03-24 | Every new Task atomically freezes Plan/line/workflow/source/round/local-date/timezone/occurrence and complete item definitions; snapshot serialization/save/commit failure rolls back all |
| R03-25 | Round comes from immutable planning snapshot, not mutable legacy Shift; new Shift remains null, legacy shift edits/reports unchanged |
| R03-26 | Historical shared Plans/Tasks, valid fake legacy assignee/status and duplicate old line occurrences remain unadmitted; no migration repair or service adoption |
| R03-27 | R32-02 DTO extensions additive; older FoundationOnly subset preserved with null new metadata/HistoricalUnadmitted; invalid marked snapshot detail409, no parent reconstruction |
| R03-28 | New generated and historical shared rows remain blocked by legacy draft/Submit/review/batch/report guards; PQE future read obligations untouched |
| R03-29 | All new reads remain no-side-effect and canExecute=false even with valid Plan/snapshot/session; no WorkContext, answers, Submit, photos, fake revision/state or device authority |
| R03-30 | New check/partial-index/CAS enforcement: raw malformed admitted rows rejected; historical nulls legal; restrictive FKs and old occurrence constraints retained |
| R03-31 | Fresh/populated SQLite latest and historical upgrades compare every original column/FK/index, including old duplicate groups and device/session/audit history; rebuild interruption/retry safety |
| R03-32 | Both EF models clean; PostgreSQL native additive SQL reviewed; no old migration rewrite/backfill/destructive Down; runtime planning JSON schema/version checks |
| R03-33 | Real disposable PostgreSQL fresh/populated upgrade and deterministic overlap/actor/line/Standard/generation races plus rollback; SQLite equivalent uses no row-lock SQL; absent server explicitly SKIPPED |
| R03-34 | Web EN/ZH shared forms/status/errors/409 reconciliation, actual IDs/fixed rounds/no assignee; old Plans tab and all existing navigation/RBAC remain unchanged |
| R03-35 | Full R32-01/02, account/cookie/legacy Plan/Standard/task/scheduler/review/Completed/HTML/XLSX suites and real PersonalSessionPostgresTests unchanged/pass |
| R03-36 | Release/Web/provider/whitespace/secret/final-head CI evidence; production rollout/cleanup off, external inventory/HTTPS/PAD/restore gates truthful, one review PR only |

Expected DST vectors (application tests still NOT RUN): 2026-03-07 Day07=>12:00Z, Night19=>2026-03-08T00:00Z; 2026-03-08 Day07=>11:00Z, Night19=>23:00Z. 2026-10-31 Day07=>11:00Z/Night19=>23:00Z; 2026-11-01 Day07=>12:00Z/Night19=>2026-11-02T00:00Z. Fixed rounds are not themselves ambiguous/nonexistent; test effective-bound parsing separately, including the legacy earlier-instant rule. Never implement fixed UTC hours or approximate New York as UTC-5 all year.

## 10. Verification requirements and remaining gates

After separately approved implementation: backend Release restore/build/focused/full tests; Web npm ci/lint/test/typecheck/build; fresh and populated SQLite latest/historical migrations; both EF pending-model checks; native PostgreSQL SQL review; real disposable PostgreSQL migration/lock/race/rollback tests; Git whitespace/scoped secret checks; exact-final-HEAD GitHub backend/frontend/PostgreSQL CI. Do not run production update commands. Use the existing explicit disposable test opt-in, synthetic credentials and verified test-database cleanup boundaries. Extend CI's live-class filter so new PG tests actually execute; returning early as a vacuous PASS is prohibited.

Historical nine-migration files/checksums, existing columns/reports and typed audit need pre/post comparison. Existing helper copying a fixed table list is not sufficient for complete latest-schema session/device/enrollment history: use the all-table remembered-schema approach. Migrations must test old duplicate shared occurrences surviving while newly admitted uniqueness is enforced. Tests of planning JSON involve actual persistence/reload and parent mutations, not only serializer round-trips.

This audit did not run any R32-03 tests or database inventory. No local/production database mutation or migration scaffolding was performed. Observed PASS evidence is only merge checks/post-merge R32-02 CI and later document integrity/publication verification. Real R32-03 PostgreSQL/PAD/IT execution remains NOT RUN/PENDING, irrespective of the existing working PostgreSQL CI harness.

External inputs: the actual two line IDs and environment, deployed legacy/shared Plan/task inventory, ordinary server HTTPS/DNS/TLS reachability, target PostgreSQL major/operating ownership, backup/restore controls and both-tablet patch/UAT checks. Do not invent hostnames/ports/CA/trust or use DHCP/MAC/SSID/model/DeviceId as identity. Old reservations may still block Admin line deactivation; use only existing controlled audited closure under appropriate authority, never silent release. No production rollout is implied by config/generation code. F1/F2/F3, Android and shared PQE reporting remain later dependencies before inspection/Submit activation.

No unresolved architecture decision blocks coding the proposed scope after Peter approves it. Main engineering risks are the cross-service lock graph, atomic line-reactivation fences, complete SQLite rebuild preservation, coherent Standard mutation and preventing an admission marker from being mistaken for execution authority. These are explicit implementation/test obligations, not reasons to restore cancelled PKI or redesign the approved architecture.

## 11. Ready-to-use next implementation authorization prompt

The following prompt is prepared for Peter to issue after reviewing this report. Its inclusion/publication is not implementation authorization.

```text
# Codex — R32-03 Stage B Implementation Authorization

Peter approves the scope and R03-01..36 acceptance criteria in
docs/architecture/IPQC_R32-03_IMPLEMENTATION_READINESS_REPORT_2026-11-10.md.

Repository: peterl-ai/IPQC-system.
Source audit baseline: merged main 69c800c010f03f4450876ca6f8c93525bb2d681e,
plus the published documentation-only readiness commit on main.
Fetch/verify current main and the canonical report; inspect intervening changes.
Preserve unrelated local files. Create codex/r32-03-shared-planning-foundation
from verified current origin/main, only now under this implementation approval.

Read AGENTS.md, CLAUDE.md, README.md, approved Rev3.2/Handoff Supersession,
the published R32-03 report and merged R32-02 source/contracts. Do not reopen
architecture sign-off or modify signed-off historical documents.

Implement one scoped review PR in reviewable commits:
1. Separate current-family + Admin/PQE PlansRead/Write shared Plan APIs;
   actual configured line IDs, canonical Day07/Night19 New York rounds,
   effective intervals, no personal assignee, revision/CAS and atomic overlap
   validation. Preserve every legacy endpoint/owner/permission contract.
2. Provider-appropriate shared planning transactions, actor authority recheck
   and account/gate/line/Plan lock order from the report. Include coherent
   Standard update/delete/readiness and bounded shared-only line activity
   fences within the existing provisioning/audit transaction. Keep reservation
   restrictions, old audit, personal sessions and legacy recurrence behavior.
3. Separate shared server generator dispatched by the existing scheduler:
   IDs-only discovery, fresh in-transaction configuration/source/time,
   strict boundaries/catch-up/no future tasks, both occurrence unique guards,
   historical collision diagnostics and atomic ownerless frozen task inserts.
4. One additive migration: nullable SharedPlanRevision, PlanningSnapshotVersion
   and PlanningSnapshotJson; approved compatible checks and admitted line/
   occurrence partial unique index. Preserve all nine historical migrations,
   rows/FKs/indexes/audit and both-provider model normalization. No backfill,
   auto-conversion, inferred identities or destructive Down. Prove SQLite
   rebuilds on populated history and actual disposable PostgreSQL upgrades.
5. Frozen PlanningDefinitionV1 metadata plus existing frozen header/item fields,
   authoritative relational task scope, immutable round/local date/timezone,
   source Plan revision and coherent Standard facts. Legacy Shift stays separate.
   Add only nullable planning DTO metadata and a historical-unadmitted status;
   preserve R32-02 old-subset reads and all new canExecute=false capabilities.
6. A minimal EN/ZH lazy shared-Plan configuration panel in the existing Plans
   module, actual line IDs/fixed rounds/revision conflicts and no assignee or
   execution controls. Existing legacy UI works if new configuration is absent.

Use the exact file inventory, contracts, lock/fence protocol and all proposed
R03-01..36 criteria in the report. Compose existing R32-02 family validation
without broadening the IPQA policy or silently upgrading legacy JWTs. New
user writes recheck current actor/family/role inside the transaction; system
generation creates no personal/device authority. No fake actor/history.

Run Release build/focused/full backend tests; complete Web lint/test/typecheck/
build; SQLite fresh/latest-populated/historical migrations and FK/history
comparison; both EF models; PostgreSQL SQL review; real disposable PostgreSQL
upgrade/overlap/Standard/line/actor/generation races and rollback. Extend the
existing CI live filter to execute new PG tests alongside personal-session
tests. Use deterministic barriers and separate connections, not only timing
or SQL-string assertions. Report missing live environments SKIPPED precisely.
Run whitespace/secret checks and verify all CI against final PR HEAD.

Excluded: R32-04 WorkContext/F1, shared draft/answer writes, formal Submit/F2,
new inspection states/attempts/PIC, photos/reference/F3/retention, shared PQE
result/report implementation, Android screens/app, device enrollment/mTLS,
production migrations/deployment/cleanup or automatic legacy conversion.
Unknown photo text remains raw and never becomes Optional automatically.
Planning validity and IsEnabled never grant patrol execution.

Update implemented-contract/verification documentation with actual evidence,
not proposed-test PASS claims. Commit/push, independently verify remote file
versions, and open one PR for review. Return branch/final SHA/PR permalink,
exact changed files, migration/legacy safety and R03 evidence, PASS/FAIL/SKIP
counts/commands and unresolved IT/PAD/restore gates. Clearly distinguish
PUSHED_TO_BRANCH implementation from MERGED_TO_MAIN documentation.

Do not merge the implementation PR, deploy, enable production configuration
or begin the next phase. Stop for independent technical review.
```

## 12. Audit stop/publication boundary

Application audit stopped here. Only this complete report may be committed/published under the current authorization, directly on main if permitted or via a normal documentation-only PR if required. Do not execute the future prompt above, scaffold a migration, or create the R32-03 implementation branch now. Final completion must independently verify remote main bytes/blob, give the actual main/report commit and permalink, distinguish it from merge SHA69c800c, and report all R03 cases as proposed/NOT RUN. Preserve unrelated untracked files.
