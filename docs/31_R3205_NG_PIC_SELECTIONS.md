# R32-05 Optional NG PIC draft selections

2026-11-17. One backend-only PR authorized by Peter. Approved global independent
roster shared by both lines is retained. PIC is optional multiselect, not a login
account, task owner, per-sample NG owner or formal submitted assignment.

## Exact-file audit before schema

PR30 reviewed HEAD `3314c47af6547dc33d72c57927ff6e3b00827d9d` merged as current main
`d397babdcb9f596ad25f1e0c6860dfa24db4249f`, identical trees; no other open PR.
Merged-main [CI44152298353](https://github.com/peterl-ai/IPQC-system/actions/runs/44152298353)
succeeded before this branch/code/schema: backend1071 PASS/658 opt-in PG SKIP/0 FAIL;
real PG17.12 658 PASS/0 SKIP/FAIL; Web127 PASS/20 files, lint/typecheck/build;
Release0 warnings/errors. Original checkout and all22 unrelated file paths/hashes
are preserved. Previous migration `20261117161849_R3205IndependentPicRoster` is
applied history and must not be edited.

Authority: [Rev3.2 §10](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md),
[Supersession](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md),
Peter's explicit NG PIC authorization/global decision. The
[identity reference](architecture/IPQC_SHARED_INSPECTION_IDENTITY_ACCEPTANCE_CONTRACT_V1_2026-11-12.md)
retains its approved/proposed distinctions. Relevant docs18–30 remain binding.

| Actual source | Audit / retained boundary |
|---|---|
| Domain/SharedDraft.cs; SharedDraftService.Value/ReadAsync | Real sample identities and current nonremoved value projections point to immutable accepted operations. Qualitative and frozen-server-judged quantitative NG are genuine draft facts, not formal NG events. |
| SharedDraftUnableToMeasure.cs; Domain/UnableToMeasure.cs | Active accepted UnableToMeasureNG is an NG item without a fake sample. Its current photo completeness must not gate optional PIC selection. |
| SharedDraftItemRemoval.cs; InspectionAttemptService.Current/Target; InspectionAttemptModel | Item/real-current-attempt scope is mandatory; Removed/noncurrent scopes cannot accept new PIC selections. Actual reinspection producers remain deferred; never invent an attempt to make tests pass. |
| PicRosterService; PicRosterModel; docs30 | Stable global server PicId, immutable revision/name/department/active history. Admin uses account then PIC identity locks. Lookup is not write authorization. |
| WorkContextStore; SharedDraftService.Sources; PR17 security hooks | Reuse current B/family, original A/proof, accepted-frontier versus fresh-source checks and existing account/line/Task/context ordering. Proof is not authentication, physical authorship or Submit authority. |
| SharedDraftBasicInformation; SampleEvidence; Attachments; common receipts | One shared Task CAS and universal Task/Operation namespace. Preserve old JSON/SQL/migrations. Add a typed receipt extension and dedicated receipt route, not generic legacy SQL dependencies. |
| EvidenceAssessment; SubmitPreflight | Assessment has photo I/O/history limits and is unsuitable as the PIC eligibility gate. Preflight stays unchanged, missing PIC adds no blocker; no formal execution is enabled. |

## NG association and factual observation

One explicit set belongs to TaskItem/current InspectionAttempt, shared by all samples
of that NG item. Ordinary eligibility means at least one current nonremoved sample's
actual accepted value receipt is NG. Active accepted UnableToMeasureNG independently
qualifies. Partial measured minimum, absent/incomplete photo evidence or unresolved
photo newness do not prevent this optional draft fact. Removed/non-NG/unsupported
current identity cannot accept fresh selection/correction, including an empty set.

An empty set on a genuine NG is valid and distinct from never accepted. Corrections
use exact current selection predecessor and shared CAS. No sample/NG event/attempt,
formal revision/state or PIC selection is inferred from remarks or accounts.

The latest explicit set is retained per exact item/attempt; sample corrections, item
removal, Unable changes, logout or roster edits never erase or rewrite it. Reads show
current NG scope and whether its accepted NG-operation basis still matches separately
from the frozen selection. No automatic carry into another attempt, automatic clear,
formal cancellation or reactivation is invented. Business assignment disposition after
NG changes remains NotEvaluatedForFormalWorkflow. A current NG basis match is a factual
comparison, not a ruling that a historical PIC assignment is formally active.

## Storage / transaction contract

Add immutable NgPicSelectionOperations and NgPicSelectionMembers plus a scoped current
pointer NgPicSelectionProjections. Members freeze real PicId/roster revision/name/
department and bind the exact immutable roster row. No old roster migration rewrite.
Canonical V1 covers requested IDs as an ordinal GUID-text-sorted set, actual scope,
source/assertion/base/origin/predecessor; reject duplicate/empty GUIDs and null/missing
sets, accept explicit empty arrays; technical maximum100 PICs per operation.
Server-resolved NG basis and roster snapshots are original receipt facts, never client
names/active flags. Exact replay returns the original snapshots before fresh eligibility,
source or roster validation; no new effect. Current B/task read authority still applies.

Reuse account-first WorkContextStore, then sorted PIC identity FOR UPDATE locks after
Task/context locks; Admin is account→PIC and never acquires Task afterward. Resolve
latest active roster revisions only after locking. Admin deactivation first rejects
fresh selection; selection first keeps its accepted old snapshot. Admin roster changes
do not advance Task DraftVersion. SQLite retains its nondeferred writer reservation.

One fresh selection claims Task v→v+1 once, then common typed batch receipt, immutable
operation/members, current pointer and acceptance audit commit atomically. Source A and
actual accepting B/family/time stay distinct. Receipts recover lost responses, not blind
new-ID retries. Native scope/FK/uniqueness/completeness/immutable guards supplement the
application transaction. Protective Down refuses accepted-history deletion.

## Explicit exclusions / evidence boundary

No formal Submit/frozen submission PIC, PQE/reinspection producer, mandatory PIC,
notifications, Android/Web execution UI, F2/F3 or retention/deletion/production action.
canExecute=false/canSubmit=false remain. Future actual reinspection transition and
formal assignment-disposition tests remain deferred, not relabeled passing foundation
cases. Final API, executable acceptance mapping, actual provider counts and complete
diff self-review/CI evidence follow in this PR; none is claimed executed by this audit.

## Implemented API and read semantics

All routes use `/api/line-patrol/tasks/{taskId}` and require current online IPQA
personal-family authority, current DB role and configured task/line/environment.
They return `Cache-Control: no-store`. POST also requires LinePatrolContributeDraft
and the existing strict shared-mutation JSON/body limits.

| Method / suffix | Contract |
|---|---|
| POST `/ng-pic-selection-batches` | One explicit SelectNgPics or CorrectNgPics operation and one Task CAS. |
| GET `/shared-draft/ng-pics` | Current scoped selections independently of a bounded history page; no writes. |
| GET `/ng-pic-selections/{operationId}/receipt` | Original immutable typed receipt, including after NG/roster/source changes. |
| Existing GET `/operation-batches/{batchId}/receipt` | Common receipt version7 with PicSelectionOperations; previous optional fields stay null-ignored. |

POST envelope: BatchId, CanonicalizationVersion=1, ExpectedTaskLineId,
ExpectedWorkflowVersion=2, ExpectedEnvironmentId, explicit ExpectedDraftVersion,
Operation, optional SourceProof. Operation: OperationId, CanonicalizationVersion=1,
Kind, TaskItemId, InspectionAttemptId, SourceWorkContextId,
ContributorUserIdAssertion, explicit BaseDraftVersion, OriginLocalEditVersion,
PicIds and nullable Predecessor(OperationId, ContentHash). Initial selection requires
no predecessor; correction requires the exact scoped latest predecessor. Client names,
departments and active flags are unknown fields and rejected. Invalid/duplicate/missing
sets return400; stale CAS, source/identity/predecessor/scope/inactive-PIC conflicts409;
unavailable/inconsistent stored facts fail closed. Unauthorized/forbidden/not found
remain401/403/404. Known-ID changed intent cannot acquire another version.

Read parameters: page>=1/pageSize1..20(default20); historyLimit1..100(default20),
optional exclusive beforeDraftVersion>0, expectedDraftVersion>=0 and expectedTaskLineId.
HistoryBeforeDraftVersion is a cursor, not proof of complete history. The returned
Task version is one serialized point in time. HasAcceptedSelection distinguishes an
explicit empty set from no operation. NgObservation freezes the original ordinary NG
current-value operation references or active Unable disposition reference; CurrentNg
observes today's scope. MatchesAcceptedNgBasis is factual equality only (null for
non-NG/unavailable scope), and IsLatestForAttempt does not grant formal active status.
AssignmentDisposition always remains NotEvaluatedForFormalWorkflow. No former
attempt's projection substitutes for the current attempt.

Migration `20261117191540_R3205NgPicDraftSelections` adds only the three NG PIC
tables, native guards/indexes and history entry. Native FKs bind real attempts,
immutable roster revisions, source users/context, common receipt and exact scoped
predecessor/projection; registry identity is shared with all six previous domains.
Projection insertion/update verifies complete frozen membership. History cannot be
updated/deleted through EF or raw SQL. Down throws. These are defenses under the
application DB role, not a claim that a DB owner cannot remove guards.

## Executable acceptance map

Tests below live in `api/JaxPower.Ipqc.Api.Tests/`. NgPicSelectionPostgresTests
executes the same actual HTTP/relational cases with separate disposable real
PostgreSQL databases. Its83 cases are appended to CI without removing any of the
prior15 suites. Model-only SQL generation is explicitly separate from real PG execution.
Final exact-HEAD Push/PR run URLs, counts and status are recorded in the PR description;
the pre-schema baseline above is historical evidence, not this PR's result.

| Authorization case | Executable evidence (NgPicSelection prefix omitted) |
|---|---|
| 01 Optional zero | Tests.Lifecycle, Tests.NoNg (empty requires genuine NG) |
| 02 One/multiple | Tests.Lifecycle qualitative/quantitative |
| 03 Duplicate/unknown | Tests.Invalid duplicate/unknown/account/empty-id/null/oversized |
| 04 Inactive | Tests.Invalid inactive; Tests.RosterHistory |
| 05 Real scope | SecurityTests.Scope; MigrationTests.Tamper scope |
| 06 Ordinary/Unable | Tests.Lifecycle quantitative/qualitative; Tests.Unable |
| 07 No fabricated facts | Tests.NoNg, Lifecycle, Unable; original immutable sample/attempt comparisons |
| 08 Removed/OK | Tests.Changes, Tests.Multiple |
| 09 Historical attempt | SecurityTests.Unavailable attempt; technical pointer fault only, no invented producer |
| 10 Frozen snapshots | Tests.Lifecycle, RosterHistory; MigrationTests.Tamper snapshot/revision |
| 11 Later Admin changes | Tests.RosterHistory; ConcurrencyTests.Roster rename/deactivate |
| 12 Deactivate race | ConcurrencyTests.Roster both orders, observed PG blocking before release |
| 13 A/B attribution | Tests.Lifecycle both directions; SecurityTests.Security |
| 14 Normal/sticky source | SecurityTests.Normal/Security four each; ConcurrencyTests.Security/Logout both orders |
| 15 All-domain CAS | ConcurrencyTests.Cas Sample/Removed/Unable/Attachment/Binding/Header/PIC, both orders |
| 16 Replay/conflicts | Tests.Replay; IdentityTests.Namespace all six domains both directions |
| 17 Lost response | Tests.Lost actual committed IOException, typed/common receipt and exact retry |
| 18 Full rollback | MigrationTests.AcceptanceRollback CAS/receipt/op/member/projection/audit/commit, correction too |
| 19 Native integrity | MigrationTests.Integrity/Tamper; same real PG cases |
| 20 Fresh/populated | Every latest-schema fixture; MigrationTests.Upgrade/MigrationRollback from exact PR30 migration |
| 21 Read safety | Tests.Paging; SecurityTests.Authority/Scope; ConcurrencyTests.ReadRace both orders |
| 22 No formal work | Tests.Lifecycle formal tables/state unchanged, no notification/UI code |
| 23 Existing regressions | Complete backend and complete existing PG suites, not just new-case filtering |
| 24 Flags false | Fixture.Accept/Read plus Lifecycle unchanged SubmitPreflight |

## Developer adversarial review and validation classification

The actual application, migration, endpoint, complete test and CI diff is reviewed
before handoff; plans and this summary are not used as correctness evidence.
Pre-commit review corrected two provider-boundary issues: SQLite extracted numeric
receiptVersion must be compared numerically (explicit BIGINT cast), and SQL NULL
must not make a missing NG basis bypass the native guard (NOT COALESCE(valid,false)).
The malformed-basis tests keep operation/common receipts mutually consistent, ensuring
they challenge genuine NG validation rather than fail only receipt equality.
Request-owned PicIds are synchronously copied before authority waits; a direct-service
mutation test verifies the accepted intent stays unchanged. Native insertion tampering
asserts specific provider error/state plus complete rollback, rather than accepting any500.

The PG race harness observes different live backend PIDs and pg_blocking_pids before
releasing a real relational barrier. Task races use distinct uploader/source accounts
so account contention cannot masquerade as Task locking. Account barriers recognize
both FOR UPDATE and FOR NO KEY UPDATE. SQLite exercises the actual nondeferred
writer reservation; neither provider race is asserted merely from Task.Delay.

Initial local evidence:17 core PASS;41 security/concurrency PASS;26 identity/migration
PASS. Release build0 warnings/errors. Additional read-order and strengthened null-basis
tests plus full historical regressions are run in final validation; these earlier counts
must not be added to the final full-suite count. Local opt-in PG skips are not PG PASS.
Final CI must include real PG17, full Release backend and Web tests/lint/typecheck/build.

No new material business decision was required: Peter's global optional multiselect
decision remains authoritative. Formal submitted PIC eligibility/freeze, future real
reinspection producers, new-attempt creation/carry-forward, disposition after NG changes,
notifications and execution UI remain deferred. No deployment/production migration or
automatic merge/next PR is authorized by this implementation.
