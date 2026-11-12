# R32-05 initial InspectionAttempt identity foundation

Date: 2026-11-12. Implementation branch for independent review; not deployment or merge approval.
Base: `92d5a4b6009d339ff3379d41f77c83bc3a21f76a`, the actual PR #16 merge.
Its reviewed feature HEAD `99c550bebb95a5d7fac867b9079daecf81755f29` is an ancestor,
not the merge SHA. PR #17 security fix remains in this baseline.

Authority: [approved Rev3.2](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md),
[supersession](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md),
Peter's D01(A) and explicit R32-05 implementation prompt. The merged
[identity/acceptance proposal](architecture/IPQC_SHARED_INSPECTION_IDENTITY_ACCEPTANCE_CONTRACT_V1_2026-11-12.md)
remains historical design evidence; its other proposals are not globally approved.
Peter separately decided in this task: existing-task initialization is current-Admin-only,
explicit, reason-required and audited. IPQA retains the personal identity read surface.

## Implemented identity and atomic creation

`InspectionAttempts` records one real empty initial work scope per actual TaskItem:
server GUID, Initial kind, current creation UTC, creation-event grouping GUID,
TaskGeneration or ExplicitInitialization provenance, frozen minimum and source metadata.
The restrictive TaskItem FK resolves the actual Task/line/workflow through its existing
relationship; no second client-supplied Task/line key can contradict it. TaskItem and
StandardItem identities are preserved. CreationEventId groups creation audit; it is
neither an accepted-operation ID nor evidence of inspection/physical authorship.

`InspectionAttemptApplicabilities` explicitly binds each item's current attempt.
Its composite `(InspectionAttemptId, PatrolTaskItemId)` FK must resolve the same pair
in InspectionAttempts; an existing attempt ID from another item cannot satisfy it.
Initial uniqueness is a filtered unique TaskItem index. Both new tables use restrictive
FKs. Tracked attempt creation history is immutable; no application update/delete route
exists. These guards do not promise protection against privileged direct SQL.

The existing shared generator stages Task, existing frozen PlanningDefinitionV1,
TaskItems, attempts and applicability in its existing single transaction. No account
lock is added after its planning gate. Identity save/commit failure rolls everything
back. Existing occurrence uniqueness, fresh source validation and scheduling fences
remain authoritative. Rejected/duplicate generation creates no extra identities.

Minimum comes only from the actual frozen TaskItem definition validated against its
planning snapshot: missing SampleCount defaults1, configured1..50 remains unchanged,
malformed/out-of-range is unavailable, never clamped. Frozen raw text, snapshot version
and item FK explain the initial minimum. No inherited formal count is guessed.

## Maintenance inventory and explicit initialization

| Method/path | Current authority and semantics |
| --- | --- |
| GET `/api/admin/inspection-identities?page=1&pageSize=20` | Current Admin, UsersRead and current family; pageSize1..100; metadata-only inventory |
| POST `/api/admin/inspection-identities/{taskId}/initialize` | Current Admin, UsersWrite and current family; explicit strict input below |
| GET `/api/line-patrol/tasks/{taskId}/inspection-attempts?expectedTaskLineId=GUID` | Current eligible IPQA/family; actual configured active line; initialized/uninitialized/unsupported state |
| GET `/api/line-patrol/tasks/{taskId}/inspection-attempts/{attemptId}` | Same; wrong task/item/StandardItem/unknown attempt IDs return404 |

Inventory identifies EligibleCleanFoundationOnly, AlreadyInitialized,
HistoricalUnadmitted, InvalidPlanningSnapshot, HistoricalInspectionData,
LegacyPersonalV1, UnsupportedRequirements/Identity, and unavailable line scope.
It exposes Task/Line/workflow IDs, item count and SHA-256 of the stored planning JSON,
not inspection answers. It is an observation, never an initialization or acceptance fact.
No production database was inventoried; actual rollout inventory remains required.
Synthetic tests exercise the categories and verify unchanged unsupported rows.

POST input: `expectedTaskLineId`, `expectedWorkflowVersion:2`, `expectedEnvironmentId`,
`expectedPlanningSnapshotHash` from inventory, and nonblank `reason` (max2000).
Unknown members fail400. In the transaction, revalidate current Admin and family,
both configured active lines, actual task/item/frozen scope, exact snapshot hash and
clean FoundationOnly state. Stale observation/scope returns409; legacy/missing/outside
scope404; invalid input400; authority401/403; configuration or exhausted contention503.
Unsupported/partial/dirty tasks are rejected, never repaired or historically converted.

Each explicit creation stores the actual Admin ID/username/current family, reason,
current UTC and shared creation-event ID in immutable attempt rows. Those creation
audit facts and applicability commit or roll back together. Nonempty GUID children
of tracked TaskItems are explicitly marked Added. Concurrent/repeated initialization
returns original identities and original creation audit, not newly labeled history.
No batch initializer, migration backfill, implicit GET or WorkContext initialization.

All routes use no-store, including auth/error responses. Initialization uses the existing
authenticated mutation limiter (30/min/user, queue0,32KiB body); input contains only
scope/hash/reason, not an unbounded item payload. Existing Admin account APIs do not
gain a new personal-family requirement. Direct service calls revalidate authority.

## Transactions and compatibility

Explicit initialization/read reuse WorkContextStore: SQLite reserves a non-deferred
writer before authoritative reads; PostgreSQL current-account FOR NO KEY UPDATE →
sorted configured lines FOR SHARE → Task FOR UPDATE → attempt/applicability inserts.
No planning advisory gate is acquired on this path. Generation retains planning gate →
line → new Task/items/attempts. User audit FK KEY SHARE remains compatible with the
existing account lock. Bounded known rolled-back contention retries remain at most3
attempts/5s lock waits; no new network/unknown-commit retry policy.

Identity-aware clean eligibility admits only complete validated empty initial sets;
partial or mismatched identities fail closed. Legacy answer/sample/history exclusions
remain. WorkContext issuance/preflight load the identity graph but create none.
GET obtains consistent transaction locks without persisting task/audit/identity changes.
All shared responses remain `canExecute=false`; preflight still accepts no business work.
Ordinary logout/security changes do not delete or fabricate attempt identities.

## Additive migration and rollback

`20261112203300_R3205InitialInspectionAttemptIdentity` creates only the two new tables
and their native checks/FKs/indexes. PostgreSQL uses uuid/integer/timestamptz and bounded
text; SQLite uses its provider-native types. No old table rebuild, ALTER, default,
data backfill, changed historical FK/index or old migration rewrite.
Both-provider models and generated SQL are checked independently of live PG tests.
Fresh and populated upgrades fingerprint every old row/column/FK/index, including
personal sessions and WorkContext creation audit. Upgrade failures at new-table and
migration-history writes are tested for rollback and safe retry for this additive step.
The historical R32-03 SQLite rebuild/interruption caveat is unchanged.

Down refuses destructive history removal. Application rollback retains expanded schema
and identities; close identity ingress and the scheduler before older binaries run,
and retain the PR #17 security baseline. Use reviewed forward repair or verified backup
restore for schema recovery. Never delete identities to pretend initialization did not
happen. Production migrations, restore rehearsal and cleanup were NOT RUN.

## Acceptance and verification evidence

| Case | Executable evidence |
| --- | --- |
| ID-01 | InspectionIdentityTests.Identity: real distinct IDs, frozen item linkage, scoped reads, auth and zero sample/submission/review creation |
| ID-02 | Generation and GenerationRollback: independent concurrent generators, scheduler replay, attempt/applicability save failure and complete graph rollback |
| ID-03 | InspectionInitializationTests: inventory categories, explicit Admin audit, repeated/concurrent requests, no GET/context initialization, dirty/legacy/partial rejection, direct guards, stale hash, transaction failures, both line/logout race orders |
| ID-04 | InspectionIdentityMigrationTests and Constraints: fresh/populated schemas, both models/native SQL, full original fingerprints, composite FK, unique initial identity, restrictive delete and upgrade rollback |

InspectionIdentityPostgresTests explicitly repeats the provider-sensitive scenarios on
real disposable PostgreSQL17 and is included in CI's filter. Opt-in SKIPs from ordinary
local/backend runs are never counted as executed PG PASS. Exact full-suite counts and
final commit CI URLs belong to PR delivery, after actual logs are verified. Web remains
unchanged; complete lint/tests/typecheck/build and backend Release/security/legacy
regressions are required. No proposed operation-acceptance case is promoted to PASS.

Development checks found the tracked-GUID child insertion issue; explicit Added state
and real existing-task initialization regressions address it. Negative fixtures also
explicitly insert their historical child rows rather than assuming navigation attachment
inserted them. Logout race assertions bind the actual closing family and preserve other
families; they do not incorrectly require all Admin sessions to close.

## Deferred boundary

No captured sample/ClientSampleId mapping, SharedDraft/DraftVersion write, operation
ledger/receipt, source-manifest business acceptance, quarantine implementation, formal
Submit/review, reinspection producer/inherited formal minimum, F2/F3, attachment bytes,
Android or Web execution UI. Later real event producers require their own reviewed
scope and migration; this schema accepts only Initial today. No new role or PKI.
Stop after this one PR for independent Final Review and Peter's merge decision.
