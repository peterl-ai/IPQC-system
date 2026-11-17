# R32-05 — Shared Task Basic Information draft

Audit/implementation started 2026-11-16; validation continued 2026-11-17.
Scoped backend implementation authorized by Peter for one PR.
Independent Final Review and Peter's separate merge decision remain required.
No production migration, deployment, formal Submit or subsequent PR is authorized.

## Exact-file audit before schema changes

PR #28 reviewed HEAD `2f014619c79b3df91e2db5f30078180971447058` merged as
`544765b7c9c8fc57c4389f517f5fc56b47d92594`, also verified latest main, with identical
reviewed/merged trees. No other open PR existed. Merged-main
[CI43964851432](https://github.com/peterl-ai/IPQC-system/actions/runs/43964851432)
completed SUCCESS before this branch/code/schema change: backend964PASS/557 opt-in PG
SKIP/0FAIL; real PostgreSQL17.12 job557PASS/0SKIP/0FAIL; Web127PASS/20 files with
lint/typecheck/build successful; Release0 warnings/errors. SKIPs are not PG execution.
The original checkout's branch/HEAD/upstream and all22 untracked paths/SHA-256 hashes
were preserved. Latest previous migration: `20261116145540_R3205ConfirmedPhotoPolicies`.

| Actual file / persisted domain | Audit conclusion |
|---|---|
| `api/JaxPower.Ipqc.Api/Domain/PatrolPlan.cs` — PatrolTask, submission and item/sample models | Task frozen identity/Shift and legacy item MachineCode/Series/Mold exist. Customer, Production and Product MO do not exist; none can be inferred from those unrelated fields. |
| `api/JaxPower.Ipqc.Api/Contracts/PatrolTaskContracts.cs` | Legacy draft input edits Shift/items; legacy submission/detail contracts have no equivalent three-field header. No legacy contract is repurposed. |
| `api/JaxPower.Ipqc.Api/Services/PatrolTaskExecutionService.cs` | Legacy-only task queries, workflow guard and conditional writes remain; new header facts must not enter that write surface. |
| `api/JaxPower.Ipqc.Api/Services/LineSharedTaskGenerationService.cs`, `PlanningSnapshot.cs` | New ownerless Task/line/frozen source/initial identities exist, without invented customer/production/MO values. Frozen Standard configuration is not an editable Task header. |
| `api/JaxPower.Ipqc.Api/Domain/SharedDraft.cs`, `Data/SharedDraftModel.cs` | Existing root holds TaskId/DraftVersion; sample ledger, projections, common receipt/audit are separate. No current header projection or historical header operation exists. |
| `api/JaxPower.Ipqc.Api/Services/SharedDraftService.cs`, `WorkContextStore.cs` | Reuse current IPQA/family, actual scope, source reconciliation, task-wide CAS and commit boundary. Preserve sorted account/line/Task/context locks and PR #17's existing issuance/security publication fence. |
| `api/JaxPower.Ipqc.Api/Domain/UnableToMeasure.cs`, `Data/UnableToMeasureModel.cs` and existing migration guards | AcceptedOperationIdentities already excludes operation-ID reuse across all accepted domains within a Task. Reuse it; do not alter old restricted registries or applied migrations. |
| `docs/18` through `docs/28` and existing accepted-operation implementations | Accepted provenance, current authentication, draft acceptance and formal inspection are different. Header facts must retain A/source and first actual B/family; replay must not revalidate accepted A or rewrite earlier receipts. |

Approved [Rev3.2 §§6–10](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md),
[Supersession](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md)
and approved invariants remain authoritative. The
[identity/acceptance reference](architecture/IPQC_SHARED_INSPECTION_IDENTITY_ACCEPTANCE_CONTRACT_V1_2026-11-12.md)
distinguishes historical technical proposals from approved business decisions. D01(A),
Admin-only initialization and prior photo/disposition decisions are unchanged.

## Scoped contract decisions

All appropriately authorized current IPQAs may collaborate on either configured line;
there is no first-writer header owner or new role. Existing §8 requires task-wide CAS,
409 on stale work and explicit reapply with new operation IDs; no automatic field merge.
No material business question needs a new Peter decision for this slice.

Use explicit **single-field** CaptureBasicInformationField / CorrectBasicInformationField
operations over the closed Customer / Production / ProductMo set. Each operation changes
exactly its named optional field; unchanged fields retain their own accepted provenance.
Correction references that field's exact current operation ID/hash. A new source B may
correct accepted A facts through an explicit new operation, without requiring A's old
proof/current eligibility again. New unaccepted A work still requires eligible source A.
No full replacement is disguised as a patch and no silent source relabeling occurs.

`value` must be present in JSON; explicit null clears only that field. Empty string and
whitespace are retained exactly and observed separately from null and never-captured.
Text is at most200 Unicode scalar values, with control/ill-formed Unicode rejected;
no Unicode normalization, trimming, structured Production classification or inferred MO.
These optional observations are not inspection failures or formal Submit prerequisites.

Two additive tables are needed: immutable TaskHeaderOperations and TaskHeaderProjections
(one current receipt pointer per actual Task/field). Values live in immutable operations;
the projection cannot drift into an independent value copy. No Task/legacy/SharedDraft
column is added, preserving old migration-stage EF queries. Migration seeds no rows or
initial values and changes no applied migration, original column/index/FK or identity.
Native scope/uniqueness/forward-pointer guards and EF append-only guards protect history;
Down must reject destructive downgrade.

The existing [fail-closed preflight](28_R3205_SUBMIT_PREFLIGHT.md) remains unchanged.
Header writes share its DraftVersion/Task lock; optional blanks introduce no blocker.
The dedicated header read observes optional facts separately and grants no formal
readiness. All shared execution/Submit flags stay false.

## API, canonicalization and receipt contract

All routes are under `/api/line-patrol/tasks/{taskId}`. Current database IPQA eligibility,
current auth version and personal family, configured environment and the actual Task's
active configured line/workflow remain mandatory at HTTP and direct service entry.
POST additionally requires the existing `LinePatrol.ContributeDraft` permission. Expected
line is a consistency assertion, not a new selected-line/device/owner permission.

| Route | Meaning |
|---|---|
| POST `/basic-information-batches` | One explicit field operation in an immutable typed envelope; one fresh acceptance advances global CAS once |
| GET `/shared-draft/basic-information` | Current actual values, three per-field presence/provenance observations and a bounded immutable history page |
| GET `/basic-information-operations/{operationId}/receipt` | Original typed field receipt; point-in-time404 is not proof a still-in-flight request can never commit |
| Existing GET `/operation-batches/{batchId}/receipt` | Original common batch receipt, additive ReceiptVersion6/HeaderOperations; older receipt bytes/semantics remain unchanged |

The dedicated typed field-receipt route leaves the older operation-receipt service's
historical migration-stage SQL dependencies unchanged. The universal operation namespace
remains `(TaskId, OperationId)` across sample, Removed, attachment, Unable, binding and
header domains; native registration rejects domain reuse atomically. No new globally
cross-Task identifier policy is introduced. Saved source/scope cannot be repurposed.

POST envelope: BatchId, CanonicalizationVersion1, ExpectedTaskLineId,
ExpectedWorkflowVersion2, ExpectedEnvironmentId, required ExpectedDraftVersion, one
Operation and optional SourceProof transport. Operation: OperationId, version1, Kind,
required named Field, SourceWorkContextId, ContributorUserIdAssertion, required
BaseDraftVersion/OriginLocalEditVersion, required nullable Value, optional Predecessor.
Field accepts exactly Customer, Production, ProductMo; no arbitrary or numeric field.
Capture requires no predecessor and no existing field fact (even an explicit-null fact
already exists). Correct requires the exact current same-field operation ID/content hash.
It can clear to null or explicitly change an unchanged value; earlier facts remain.

Unknown/duplicate properties, missing Value/Field, numeric coercion, invalid kinds,
control characters, malformed Unicode and oversized values fail closed. Existing30/min
mutation admission,128KiB body and depth32 limits and no-store apply; no body/proof logging.
Current B and server-resolved A/source are distinct; source proof or contributor assertion
cannot authenticate B, prove physical authorship or grant inspection/Submit authority.

Canonicalization1 fixes property order, domain separation, canonical GUIDs, unchanged
Unicode, explicit null/empty/whitespace, base/local versions, field/value and predecessor
hash normalization. The operation excludes BatchId, raw/stored proof secrets, current
transport credentials and current B/family. The batch fingerprint binds its exact envelope
and operation hash. Separate frozen JSON/SHA-256 vectors test the protocol independently.
Changing accepted data/source/scope/base/origin/version requires new IDs. Proof removal or
a different authorized recovery transporter never changes the original accepting actor.

Exact accepted BatchId/fingerprint returns the original batch and field receipt before
stale-version/source revalidation. Same accepted operation in a new envelope returns its
original receipt with ReplayOnly=true, BatchReceipt=null and no new batch/version/audit.
Changed accepted identity conflicts. Receipt6 extends the common DTO with a null-ignored
optional list; stored old receipts are never rewritten. Read/replay validates the stored
typed receipt, row, canonical payload/hash and actual Task/line/environment consistency.

Errors: malformed shape/version/value400; current authority401/403; missing/legacy/foreign
resource404; scope/version/identity/predecessor/source conflicts409; detected receipt
integrity failure503. Stable codes include InvalidBasicInformationBatch,
InvalidBasicInformationOperation, InvalidBasicInformationQuery,
BasicInformationFieldAlreadyCaptured, BasicInformationPredecessorConflict,
BasicInformationVersionConflict, BasicInformationReceiptIntegrityUnavailable, and the
existing DraftVersionConflict/BatchIdentityConflict/OperationIdentityConflict/
TaskLineMismatch/DraftScopeMismatch/SourceProvenanceConflict/SourceSecurityRevoked.
Source conflicts retain existing quarantineRequired/preserveOriginals and operation IDs.

## Current optional observations and historical safety

Read query: limit1..100 (default20), optional positive beforeDraftVersion, nonnegative
expectedDraftVersion and expectedTaskLineId. Current fields are always observed separately
from the historical page. HistoryBeforeDraftVersion provides descending global-version
pagination; it is not a past-value rewind. A pinned current version mismatch is409.

Fields distinguish Unrecorded (no accepted fact), ExplicitNull, Empty, Whitespace and
Provided. Exact strings are preserved. AcceptedFieldCount counts explicit facts including
clear/empty; NonblankFieldCount is an observation, not business approval. Every field has
IsRequired=false/BlocksSubmit=false. Completeness=OptionalHeaderFactsOnly,
CanExecute=false, CanSubmit=false and AcceptsFormalInspection=false are explicit.
No GET creates a root, projection, policy, operation, audit or accepted fact.

Fresh writes require valid frozen planning, genuine initialized initial identities and
existing editable draft-foundation eligibility. They never fabricate item/attempt IDs.
Header facts are Task-level, not measurements. Invalid current snapshot/applicability
blocks new acceptance while preserving authorized original receipt recovery/history.
Normal logout/expiry/refresh/self-password change does not security-revoke A's queued
header work. Reset/deactivation/IPQA loss/explicit source revocation blocks new A work,
stays revoked after reactivation, and does not erase accepted A. A valid new B operation
may explicitly correct accepted A through a new B context and exact predecessor.

## Additive storage, transaction and rollback

Migration `20261116221536_R3205SharedTaskBasicInformationDraft` creates only
TaskHeaderOperations and TaskHeaderProjections. Native UUID/bigint/timestamptz on PG and
the existing SQLite representations are provider-specific. Snapshot/new designer retain
the repository's provider-default type resolution and short explicit constraint/index names.
No old table/column/index/FK/applied migration changes. Both tables start empty.
Destructive Down throws; production migration is not performed.

Operations have native Task/field/operation/hash predecessor FKs, common batch/source/
actor FKs, one header fact per accepted batch/version, bounded values and closed kinds.
Triggers register the existing universal ID, validate actual source/Task/line/environment,
the current shared version and typed common batch/receipt, and require current exact field
predecessors. Projections contain only a forward receipt pointer; native scope/FKs prevent
cross-field/Task jumps and rewind. EF/native update/delete guards retain accepted history.

Reuse unchanged WorkContextStore: SQLite nondeferred transaction; PG current uploader/source
account locks sorted first, configured line share locks, actual Task update lock and source
context locks. Preserve PR #17's original publication fence and Admin API compatibility.
No planning gate, external service/file I/O or HTTP success inside an uncommitted transaction.
One conditional global version claim, typed common receipt, immutable operation, current
pointer, universal ID and audit commit atomically. Every pre-commit failure rolls back all
new effects. Unknown post-commit response failures use exact receipt recovery, not blind
new-ID write retry. Independent fields still contend against the same stale task version.

Historical tests exclude only these new additive tables from their *prior-schema*
fingerprint; this slice separately fingerprints/tests the new rows/constraints. The older
four-table policy DDL test now explicitly ends at its original migration rather than
accidentally counting every future migration. These changes retain the prior preservation
obligations and do not weaken header validation.

## Executable validation and self-review evidence

Tests use genuine generated shared Tasks, source WorkContexts, current users, accepted
operations and, for cross-domain cases, actual PNG bytes/reference/binding receipts.
No formal submission, synthetic inspection attempt or historical header is fabricated.
Coverage includes all three fields, nullable/empty/whitespace/scalar limits, canonical
vectors, exact correction/replay/recovery, accepted/unaccepted source lifecycle, strict
HTTP/direct authority, real foreign-task receipt isolation, invalid current identity,
all-domain operation namespace, no GET mutation and unchanged task/Submit behavior.

Native scope tests mutate exactly one SQL parameter in a real valid accepting request and
assert the intended database error, so an unrelated clone/batch/uniqueness failure cannot
make them pass. Rollback injection covers CAS, receipt, ledger, pointer insert/update,
audit and commit; a separate *after real commit* exception exercises lost-response recovery.
Fresh and populated SQLite/PG migrations and failed-DDL rollback are distinct tests.

Concurrency tests run Header against sample, Removed, Unable, attachment, binding and
same/different header fields in both orders. They use different current requester/source
accounts (the photo source is B too) and real separate PG connections. PG must observe an
actual pg_blocking_pids wait before releasing the holder; reads/corrections, source security
actions and current-uploader logout have both-order frontiers too.

Final exact-HEAD full-backend/Web and real-PG execution counts/URLs belong in the PR
delivery evidence. Provider opt-in skips are never counted as executed PG tests.
During development, test oracles were corrected for collection reference equality and
the existing ownerless shared Task's legacy403. No legacy authorization was changed.

Local final Release build completed with0 warnings/errors. All70 new local cases passed,
0FAIL/0SKIP (66 connected SQLite cases,2 canonical protocol cases and2 provider-model/DDL
cases); the separate real-PG class contains66 executable counterparts. These counts do
not claim PG execution before CI. The complete actual staged diff was adversarially
reviewed, including migrations/guards, nullable protocol, auth/provenance, every test,
CI and generated model metadata. The new designer's target body exactly matches the
snapshot; snapshot changes only add the two header entities/relationships. No confirmed
in-scope production defect was found locally. Test-oracle corrections above and single-
parameter native fault probes prevent false-positive evidence; full final-HEAD CI and
independent review remain separate gates.

Real-PG CI follow-up (2026-11-17): the first Push PG run was not green:
run44104279387/job134112285850 reported619PASS/4FAIL/0SKIP. P2 test-coverage defect:
the four new PG populated-upgrade/DDL-rollback wrappers started at the latest schema,
so Upgrade did not actually upgrade and three injection points could not run. They now
explicitly create the previous-migration fixture; the shared preparation asserts the
exact applied baseline on both providers before populating it. P3 historical test
compatibility defect: WorkContext's PG old-schema fingerprint did not exclude the two
new tables and incorrectly ordered them by a nonexistent Id. Exclude exactly those
new tables from its old-row/column/FK/index comparison; independent header preservation
and native guard checks remain. These are test fixes, not a production-schema or
authorization relaxation. The failed run remains evidence; final updated-HEAD CI
results must be verified separately in the PR delivery.

## Explicit exclusions

Formal Submit/frozen submission headers/revisions, PQE decisions, reinspection producers,
PIC roster/multiselect, structured Production V2, NG-photo newness/coverage approval,
F2 intent/takeover, F3 retention/deletion, Android/Web execution UI and production rollout.
Final implementation, migration, tests and exact-HEAD CI evidence follow in this PR;
this audit is not a claim that those gates have already executed.
