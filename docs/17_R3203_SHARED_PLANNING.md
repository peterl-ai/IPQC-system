# R32-03 — Shared planning foundation

Implementation date: 2026-11-11. Approved baseline: `f935ad8e4a0e5809c7845de912644edb516eb794`.

This implements the approved [R32-03 readiness report](architecture/IPQC_R32-03_IMPLEMENTATION_READINESS_REPORT_2026-11-10.md), [Rev3.2](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md) and [Handoff Supersession](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md). Those signed documents remain unchanged. This document records implemented contracts and verification; the earlier readiness matrix remains a historical proposal, not a test-results ledger.

## Scope and authority

Admin/PQE can configure scheduling for the two real server-configured lines. Generated tasks are ownerless `LineSharedV1`, `PendingInspection`, revision0, with no answers/submissions, no Shift and `canExecute=false`. Scheduling admission and `PlanningDefinitionV1` never grant execution authority. WorkContext/F1, draft writes, F2 Submit, photos/F3, PQE shared-result reporting, Android and device PKI remain excluded. R32-01 retirement/cleanup controls and R32-02 personal authentication stay in effect.

## API

New namespace `/api/line-patrol-plans`, separate from all legacy routes:

| Method/path | Contract |
| --- | --- |
| GET `/lines` | Ordered actual IDs/code/name for the two configured active ProductionLines; no Admin/device permission elevation |
| GET root | Shared-only paging/filtering: `lineId`, `planNo`, `name`, `enabled`, `page`, `pageSize` (1..100); filter before total |
| GET `/{id}` | Shared metadata including unadmitted historical rows; legacy/missing404 |
| POST root | New shared Plan revision1, no assignees; disabled is the Web default |
| PUT `/{id}` | Full shared input plus positive `expectedRevision`; atomic compare-and-swap |
| POST `/{id}/enable`, `/disable` | `{expectedRevision}`; no-op preserves revision/boundary; disable remains possible on inactive/off-configuration admitted Plans |

All routes require current personal family plus PlansRead/Write (Admin/PQE). Anonymous/invalid401, wrong role/forced-change403, old JWT without family401 on this namespace only, configuration503, missing/legacy404, historical mutation409, stale/overlap409, invalid input400. Unknown input members are rejected. No delete, conversion, repair, client generation or inspection routes were added.

Input: optional bounded `planNo`, `planName`, `patrolStandardId`, actual `lineId`, unique nonempty `rounds` subset `[Day,Night]`, `effectiveStartLocal`, optional `effectiveEndLocal`, `isEnabled`, and update `expectedRevision`. Server fixes America/New_York, Day07:00/Night19:00. Canonical daily schedule uses existing ScheduleDefinition; no cron/timezone editor or round-mask column. Dates use `yyyy-MM-ddTHH:mm`; nonexistent local time rejected, ambiguous time uses earlier UTC instant; effective end inclusive and strictly after start. Installation-wide existing PlanNo index remains authoritative.

Enabled admitted Plans on one actual line cannot overlap in both inclusive effective interval and rounds; different rounds/disjoint intervals are allowed. Disabled drafts may reference incomplete Standards. Enabled Standard definitions require nonempty valid ordered items (technical cap1000), default sample count1, count1..50 and valid quantitative limits. Raw photo policy is frozen unchanged, without claiming inspection readiness.

## Transactions, identity and boundaries

`LineSharedPlanningStore` owns the unit of work, clears pre-lock tracking, retries transient conflicts at most three attempts, bounds PostgreSQL waits to5s and returns503 after exhausted transient retries. Constraint conflicts return409. No response is returned before commit.

* SQLite obtains a non-deferred writer transaction before authoritative reads; no PostgreSQL row-lock/advisory SQL is executed there.
* PostgreSQL lock order: actor `FOR NO KEY UPDATE` (new user writes), two-int `pg_advisory_xact_lock(495051,3203)`, sorted affected ProductionLine roots `FOR UPDATE`, then Plan/conditional claim. Current account/family/environment/auth-version/role/forced-change state is checked under account lock. Security mutations retain their existing conflicting account `FOR UPDATE` order. NO KEY UPDATE permits provisioning audit FK KEY SHARE and avoids the same-actor line/audit cycle.
* Shared management, shared generation and existing Standard update/delete use the common planning gate. Standard loads/reconciliation happen inside it, preserving GUIDs and two-save sequence moves. Direct service mutation also checks enabled shared dependencies. Legacy Standard authorization does not acquire a new personal-family requirement.
* ProductionLine changes retain ProvisioningStore's existing gate/audit transaction and reservation409. Actual activity transitions lock line before Plan fences, increment every admitted shared revision and advance its boundary monotonically. Rename/no-op activity does not re-fence Plans. Audit failure rolls all those changes back. No device reservation is silently released.

New shared timestamps use UTC microseconds. Enabled creation sets boundary CreatedAt-1microsecond and requires occurrence>=CreatedAt. Later source/line/round/window changes and reenable fence at max(previous,now), strictly exclusive. Metadata-only edits increment revision without moving boundary. Clock rollback cannot lower an existing fence. Existing Task.WorkflowVersion/LineId and frozen bytes are never rewritten by parent changes.

## Scheduler and read snapshots

The existing hosted scheduler dispatches a separate shared generator after its unchanged legacy pass. Discovery uses IDs; bounded candidates and every insertion reload all authoritative facts inside short transactions, including a fresh server clock. Existing catch-up setting defaults24hours, clamped1..168; no future generation or inactive/disabled/unadmitted admission. One malformed shared Plan is logged/skipped without stopping later valid Plans. Missing new configuration cannot stop legacy generation.

Uniqueness is both existing `(PlanId,UTC)` and new admitted `(LineId,UTC)`. Any historical unadmitted same-line/same-UTC collision is skipped with `HistoricalOccurrenceConflict`; it is never adopted. A bounded frozen calendar-key check prevents remapping an existing local-date/round after timezone-rule changes (`CalendarRuleDrift`). Do not infer historical round from Shift.

Planning JSON atomically freezes Task/Plan/line/workflow/source-revision identities, Plan labels/schedule/effective/fence facts, line code/name/revision, StandardId/UpdatedAt/header and complete ordered item definitions, occurrence/local date/round/zone/offset. It reflects generation-time coherent source facts, not invented source history as of occurrence. Reads validate identities, header/items and calendar consistency using the frozen UTC offset, without mutable parent reconstruction.

R32-02 DTO extensions are trailing nullable metadata plus `PlanningSnapshotStatus`:

* `ValidV1`: coherent marker1/PlanningDefinitionV1, FoundationOnly, canExecute=false.
* `HistoricalUnadmitted`: both new columns null; original compatible R32-02 subset remains readable, new metadata null.
* `Invalid`: malformed/unknown/inconsistent marked snapshots remain visible once in list as unavailable; detail409 `TaskSnapshotUnavailable`.

Personal task routes remain IPQA-only/current-family; no new Admin/PQE result API. Legacy execution/review/report isolation is retained.

## Migration safety

Exactly one migration: `20261111142052_R3203LineSharedPlanningSnapshots` (+ generated Designer and model snapshot). Three nullable columns only: Plan.SharedPlanRevision, Task.PlanningSnapshotVersion/Json; checks restrict admitted values and ownerless version1 snapshots; partial unique admitted-line occurrence index. The old LineId index, Plan/UTC unique key, restrictive FKs and all nine historical migrations are retained. No defaults/backfill/conversion/new tables. PostgreSQL uses bigint/integer/text; SQLite rebuilds Plan/Task using the EF target model. Provider-neutral snapshot normalization is preserved. Down throws, preserving history.

Upgrade tests compare every original column across all existing tables, old FKs/indexes, historical duplicate groups, personal sessions and retained enrollment/pending-revocation/audit history. Fresh schemas and historical upgrade sources are exercised. `PRAGMA foreign_key_check` must be empty.

**SQLite interruption caveat:** EF's generated rebuild has multiple transaction boundaries; schema can commit before __EFMigrationsHistory. A failure before the first rebuild commit rolls back and can retry (tested). A later history-write failure preserves original values but requires restoring the verified pre-upgrade backup and retrying while application writers remain stopped (tested); blind retry can fail on existing columns. Do not claim whole-upgrade crash atomicity. Use reviewed offline backup/restore procedure and preserve DB/WAL consistency; no production migration or restore was performed here.

## Verification ledger

The local ledger below records executed results. The independently verified CI source run is linked explicitly; final evidence-commit HEAD checks are verified at delivery on [PR14 checks](https://github.com/peterl-ai/IPQC-system/pull/14/checks). Local PostgreSQL tests require explicit disposable opt-in and are SKIPPED without it; they are not simulated PostgreSQL PASS. CI uses a real PostgreSQL17 service and isolated per-test databases, running both PersonalSessionPostgresTests and LineSharedPlanningPostgresTests.

| Gate | Result |
| --- | --- |
| Release restore/build/full backend | PASS: restore/build (0 warnings/errors);296 PASS/0 FAIL/14 local PostgreSQL SKIP |
| New focused planning/race tests | Final focused acceptance64 PASS/0 FAIL/10 local PostgreSQL SKIP, including13 SQLite concurrency/timeout cases |
| Web lint/test/typecheck/build | lint/typecheck PASS;77 tests PASS/0 FAIL; build PASS (existing large-bundle warning) |
| SQLite fresh/populated/historical/recovery | PASS:8 migration cases including both interruption/recovery paths; old column/FK/index comparisons |
| Both EF models / PostgreSQL SQL | PASS:2 CLI model checks; native additive idempotent PostgreSQL SQL reviewed |
| Real disposable PostgreSQL | Local14 SKIPPED; real PostgreSQL17 CI14 PASS/0 FAIL/0 SKIP (10 new shared cases +4 existing personal-session cases) |
| Git whitespace / scoped secret / remote content | PASS: whitespace/private-key-token scan;40 remote source/document blobs matched; signed docs/nine old migrations unchanged;22 unrelated file fingerprints preserved |
| UAT/PAD/production rollout/restore | SKIPPED / NOT RUN; outside this authorization |

CI source evidence: [run43042194302](https://github.com/peterl-ai/IPQC-system/actions/runs/43042194302) at `d35612156c896f8c6fb523bc67b0def1ccbe4b75` passed all three jobs: backend295/0/14 local-opt-in skips, Web77/0, real PostgreSQL14/0/0. The subsequent bounded-SQLite-timeout cap adds one local regression (full296/0/14); it does not change the PostgreSQL path. Final HEAD CI is independently checked after this evidence commit and linked in the delivery/PR status. The initial PostgreSQL run exposed an overstrong no-due-occurrence test assumption; the corrected fixture advances to a genuinely due Day07 occurrence and refreshes its synthetic Admin token. The corrected real-provider test passed.

Commands: `dotnet restore api/JaxPower.Ipqc.slnx`; `dotnet build api/JaxPower.Ipqc.slnx --configuration Release --no-restore`; `dotnet test api/JaxPower.Ipqc.slnx --configuration Release --no-build` (TRX retained outside the repository); focused filter `FullyQualifiedName~LineShared`; Web `npm run lint`, `npm run test`, `npm run typecheck`, `npm run build`; EF `migrations has-pending-model-changes` for Sqlite/Postgres and `migrations script` with native PostgreSQL provider/idempotent output. One overlapping local build encountered a Windows testhost file lock; sequential rerun built with0 warnings/errors. No failing product verification remains in the completed local ledger.

## R03-01..36 evidence map

Test classes reside in `api/JaxPower.Ipqc.Api.Tests/`; Web evidence resides in `web/src/features/patrol-plans/`. Named tests encode scenario IDs; the following maps combined tests without treating a test-name number as proof by itself.

| Case | Executable evidence |
| --- | --- |
|01|LineSharedPlanTests.Role_and_current_family_permissions_are_combined; existing personal lifecycle tests|
|02|Actual_configured_directory_and_missing_settings_leave_legacy_usable; legacy scheduler regressions|
|03|Admitted_ownerless_create; Definition_readiness; Bad_and_unadmitted_plans|
|04|Admitted_ownerless_create_strict_contract_and_global_number_uniqueness|
|05|Dst_bounds; Creation_instant_microseconds_future_and_disabled_boundaries|
|06|Dst_bounds_overlap_inclusive_end_and_disjoint_rounds; concurrent admission|
|07|Cas_noop_metadata_source_changes_and_monotonic_fences; concurrent CAS; DB concurrency token|
|08|Cas_noop...; disabled boundaries; StaleDiscovery; LineFence|
|09|Frozen_bytes_identity_definitions_and_personal_read_contract|
|10|Definition_readiness_allows_large_standard; existing InspectionJudgment/Standard tests|
|11|Two_lines_four_tasks_daily_and_restart_is_idempotent|
|12|Dst_day_round; Stored_calendar_key; existing legacy DST tests|
|13|Bad_and_unadmitted_plans_are_isolated_catchup_is_bounded; existing scheduler catch-up|
|14|Creation_instant...; Bad_and_unadmitted...; LineFence; configured-directory errors|
|15|AdmissionAndWorkers; Transient_retries; Unbounded_sqlite_connection_timeout_is_capped_and_restored; SnapshotRollback|
|16|Cross_plan_winner_and_historical_collision|
|17|StaleDiscovery pauses after committed candidate discovery and changes source facts|
|18|StaleDiscovery both winner orders; concurrent CAS|
|19|StandardRace coherent reorder and retained IDs; CommitRollback; existing Standard tests|
|20|StandardRace enable-vs-incomplete-source in both orders, transaction guard|
|21|LineFence failure/queued generation/reenable/no-op; existing reservation tests|
|22|ActorRace account security writer vs request; existing all-security-mutation tests|
|23|SameActorLineAndPlan with audit barrier; live PostgreSQL equivalent|
|24|SnapshotRollback; CommitRollback; frozen contract verification|
|25|New Shift null; Historical_unadmitted_never_infers_round; existing legacy shift/report suites|
|26|Historical metadata readonly; old collision; migration duplicate preservation; adversarial legacy workflow tests|
|27|Frozen read; seven malformed marked cases; original historical subset; existing LinePatrolReadTests|
|28|Existing WorkflowLineFoundationTests guards and legacy review/report suites; generated read-only assertions|
|29|Frozen_bytes... read effects; all new summaries/details canExecute=false; existing device-independent reads|
|30|Fresh_checks_partial_unique_restrict_and_revision_concurrency_token; real provider races|
|31|LineSharedPlanningMigrationTests fresh/latest/history/pending audit/index/FK/interrupt/recovery; live PG populated upgrade|
|32|Both_models_and_postgres_native_additive_sql_preserve_history; both EF CLI model checks|
|33|LineSharedPlanningPostgresTests (real opt-in CI) and equivalent SQLite concurrency tests|
|34|lineSharedApi.test.ts; LineSharedPlansPanel.test.ts (both languages/conflict/input retention); full Web regression|
|35|Full existing backend/Web suites and real PersonalSessionPostgresTests|
|36|Release/Web/provider/CI/whitespace/secret/remote verification ledger; no deployment|

## Remaining gates and limits

No production host/FQDN/port/CA or confirmed tablet HTTPS trust is invented. IT must supply reviewed environment/line IDs, internal HTTPS/DNS/firewall/CA trust and connectivity evidence on SM-X210/API35 and SM-T500/API30; old patch assessment remains IT's responsibility. No Android/UAT was run. Before future rollout: read-only inventory of existing shared rows/duplicates, offline migration/restore rehearsal on representative synthetic/de-identified data and a restore/session-revocation runbook. No historical conversion is authorized.

The old legacy scheduler stale-configuration race remains documented; the new shared generator does not inherit its cached-source path. Shared planning uses a low-volume common gate rather than a distributed scheduling platform. This PR does not enforce future planning-JSON immutability against a DBA; application paths never rewrite snapshots and marked corruption fails closed in reads. Future execution must independently implement approved F1/F2/F3 without treating this planning snapshot as execution authority.

Stop for independent PR review. Do not merge/deploy or begin R32-04.

## Changed files

- `.github/workflows/ci.yml`
- `README.md`
- `api/JaxPower.Ipqc.Api.Tests/LineSharedPlanTests.cs`
- `api/JaxPower.Ipqc.Api.Tests/LineSharedPlanningConcurrencyTests.cs`
- `api/JaxPower.Ipqc.Api.Tests/LineSharedPlanningFixture.cs`
- `api/JaxPower.Ipqc.Api.Tests/LineSharedPlanningMigrationTests.cs`
- `api/JaxPower.Ipqc.Api.Tests/LineSharedPlanningPostgresTests.cs`
- `api/JaxPower.Ipqc.Api.Tests/LineSharedSchedulerTests.cs`
- `api/JaxPower.Ipqc.Api.Tests/LineSharedSnapshotTests.cs`
- `api/JaxPower.Ipqc.Api.Tests/PersonalSessionFixture.cs`
- `api/JaxPower.Ipqc.Api/Contracts/LinePatrolContracts.cs`
- `api/JaxPower.Ipqc.Api/Contracts/LineSharedPlanContracts.cs`
- `api/JaxPower.Ipqc.Api/Contracts/PlanningSnapshotContracts.cs`
- `api/JaxPower.Ipqc.Api/Data/IpqcDbContext.cs`
- `api/JaxPower.Ipqc.Api/Data/Migrations/20261111142052_R3203LineSharedPlanningSnapshots.Designer.cs`
- `api/JaxPower.Ipqc.Api/Data/Migrations/20261111142052_R3203LineSharedPlanningSnapshots.cs`
- `api/JaxPower.Ipqc.Api/Data/Migrations/IpqcDbContextModelSnapshot.cs`
- `api/JaxPower.Ipqc.Api/Domain/PatrolPlan.cs`
- `api/JaxPower.Ipqc.Api/Endpoints/LineSharedPlanEndpoints.cs`
- `api/JaxPower.Ipqc.Api/Program.cs`
- `api/JaxPower.Ipqc.Api/Services/ConfiguredLineDirectory.cs`
- `api/JaxPower.Ipqc.Api/Services/LinePatrolReadService.cs`
- `api/JaxPower.Ipqc.Api/Services/LineSharedPlanService.cs`
- `api/JaxPower.Ipqc.Api/Services/LineSharedPlanningStore.cs`
- `api/JaxPower.Ipqc.Api/Services/LineSharedTaskGenerationService.cs`
- `api/JaxPower.Ipqc.Api/Services/PatrolStandardService.cs`
- `api/JaxPower.Ipqc.Api/Services/PatrolTaskGenerationService.cs`
- `api/JaxPower.Ipqc.Api/Services/PlanningSnapshot.cs`
- `api/JaxPower.Ipqc.Api/Services/ProductionLineService.cs`
- `docs/04_ARCHITECTURE.md`
- `docs/10_DEPLOYMENT_CHECKLIST.md`
- `docs/17_R3203_SHARED_PLANNING.md`
- `web/src/features/patrol-plans/LineSharedPlansPanel.test.ts`
- `web/src/features/patrol-plans/LineSharedPlansPanel.vue`
- `web/src/features/patrol-plans/PatrolPlansPage.vue`
- `web/src/features/patrol-plans/lineSharedApi.test.ts`
- `web/src/features/patrol-plans/lineSharedApi.ts`
- `web/src/features/patrol-plans/lineSharedValidation.ts`
- `web/src/i18n/locales/en.ts`
- `web/src/i18n/locales/zh-CN.ts`
