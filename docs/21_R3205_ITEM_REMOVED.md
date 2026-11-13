# R32-05 per-item Removed draft disposition

Date: 2026-11-13. Implementation for independent review, not production activation.
Base/actual PR #19 merge: `02bf667215e2a1d04cb9ec87c7907ddd5e050a6b`, containing reviewed
HEAD `dd246b6595e7c0a90d7701d88c010ca5ad2b25a0` with an identical tree. Merged-main
[CI43442235559](https://github.com/peterl-ai/IPQC-system/actions/runs/43442235559)
succeeded before coding: backend496 PASS/127 opt-in SKIP, real PostgreSQL127 PASS/0 SKIP,
Web90 PASS; Release builds0 warnings/errors. No conflicting open PR existed.

Authority: Peter's explicit per-item Removed implementation prompt and approved
[Rev3.2 §10](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md),
[supersession](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md),
and approved business/security invariants in the
[identity/acceptance reference](architecture/IPQC_SHARED_INSPECTION_IDENTITY_ACCEPTANCE_CONTRACT_V1_2026-11-12.md).
Other proposed technical choices in that reference are not globally approved by publication.

## Business states and preservation

| Current item/attempt state | Meaning |
| --- | --- |
| Applicable | No accepted RemoveItem for this real current attempt; ordinary typed sample draft rules apply |
| Removed | Explicit per-item absence/non-applicability assertion after Production or Equipment confirmation; retained definition, attempts and sample history; not OK/NG/N/A |
| UnableToMeasureNG | Future reason/current-attempt-photo disposition; unsupported here, never synthesized as NG or a zero sample |

The server records the authenticated IPQA's explicit assertion of Production/Equipment
confirmation; it does not independently authenticate those departments or claim proof
of a physical conversation. A WorkContext establishes scoped provenance, not physical
authorship/confirmation or exact offline capture time. A/B remain distinct.

One fresh removal targets one actual TaskItem/current InspectionAttempt. No array/bulk
removal API. Existing sampled items may be explicitly Removed without changing any
sample map, projection value/tombstone, sample operation, dependency, frozen definition
or attempt minimum. Their samples remain history and read with IsCurrentApplicable=false.
CurrentAttempts still exposes the actual identity/minimum; it is not deleted/closed.
New capture/correction/sample-removal on that Removed attempt returns409. Exact old
sample operation/batch recovery remains valid and never reapplies values.

No restore/undo operation in this slice. Another new RemoveItem on that attempt conflicts;
an exact accepted ID returns its original receipt. No new reinspection attempt is made.
The initial attempt's established minimum remains immutable; future inherited/formally
measured reinspection minimum producers remain deferred and are not claimed tested.

The last applicable item may be removed: persist RemainingApplicableItemCount=0 and
WarnsLastApplicableItemRemoval=true in its original receipt. The read model then has
AllItemsRemoved=true. This is a warning, not an extra authorization or formal outcome.
A valid all-Removed task differs from a corrupt zero-item task, which is rejected by
frozen planning/identity validation. Task status, StartedAtUtc, CurrentRevisionNo,
overall result, submissions and PQE facts do not change. No PendingNotPerformedConfirmation,
NotPerformed submission or Qualified/Approved shortcut is created.

## API, strict single-item manifest and receipts

Existing [sample V1 routes/bytes](20_R3204_SHARED_DRAFT_ACCEPTANCE.md) are preserved.
New POST `/api/line-patrol/tasks/{taskId}/item-removal-batches` requires current eligible
IPQA/family and the existing LinePatrol.ContributeDraft permission. Direct service
calls revalidate current database authority in WorkContextStore. Existing Admin account
API family compatibility is unchanged.

Strict input: BatchId/version1, expected actual line/workflow/environment/version,
one Operation and optional proof transport SourceProof. Operation binds OperationId,
version1, Kind=RemoveItem, real TaskItemId/InspectionAttemptId, SourceWorkContextId,
owner assertion, BaseDraftVersion, OriginLocalEditVersion,
ConfirmedAbsentOrNotApplicable=true, ConfirmationSource=Production or Equipment,
and optional Reason (max500 UTF-16 code units; control characters rejected).
Null and empty reason are distinct immutable input encodings, both optional.
No SampleId/ClientSampleId, arbitrary full draft, photos, accepted flag or client
measurement timestamp is accepted. Unknown fields, duplicate JSON properties, quoted
numbers and bulk operations fail400. Auth precedes binding; the existing shared
30/min/user mutation limiter,128KiB/depth32 body limits and no-store apply.

The typed removal is dependency-free: it changes disposition, not any sampled value,
and does not import old measurements into new data. Task CAS binds the exact observed
draft version; no sample predecessor is inferred from latest state. SourceProof must
refer to the operation's one exact source. Fresh acceptance uses the same shared
source validation as samples, inside the final transaction, not a preflight token.
Missing/bad proof/owner/scope yields409 quarantine of original operation/context IDs;
sticky source revoke rejects new acceptance. Accepted old A measurements do not need
old A proof/current eligibility for a valid new B removal, and remain unchanged.
Normal A logout/expiry/refresh/self-password does not sticky-revoke historical sources.

Conditional SQL claims one Task DraftVersion v→v+1 and atomically stores the item
operation, derived ID registration, common accepted batch receipt and common acceptance
audit. Source account, line, Task and source-context locks/retries remain the existing
protocol; PR #17's issuance publication fence is untouched. Success follows commit.
Competing sample/item operations share the same Task CAS; one succeeds and the other409.

Same BatchId/fingerprint returns the original typed batch/operation outcome with ReplayOnly.
New BatchId/exact accepted operation returns its original operation receipt without a new
batch/version/audit. Changed payload/source/scope/version under an accepted ID conflicts.
All recovery still requires current task-read authority, independently of old editability.
Use existing GET `/operation-batches/{batchId}/receipt` and `/operations/{operationId}/receipt`.
An early404 is only a point-in-time observation; late commit must be reconciled.

Sample batch receipt JSON remains V1 byte-compatible: new nullable members are omitted.
Removal batches explicitly have ReceiptVersion=2, Operations=[] (no sample receipt),
and ItemOperations=[one typed ItemRemovalReceipt]. Individual item receipts have Kind,
Disposition=Removed and real item/attempt/source/A/B/family/UTC/version, confirmation,
reason/minimum/remaining-count/warning facts, with no sample identity or sample judgment.
Clients interpreting removal outcomes must support that explicit typed extension.
GET shared-draft adds Items with Applicable/Removed/removal receipt and AllItemsRemoved;
draft/batch canExecute remains false and no formal completeness is evaluated.

Canonical item V1 has an explicit item-removal domain, fixed field order/UTF8 escaping,
lowercase D GUIDs, actual scope, kind/source/assertion/base/local/confirmation/reason.
Batch hash has a distinct item-removal-batch domain and includes batch/expected version
and operation hash. Raw proofs/hashes, JWT/refresh credentials and current B/family are
excluded. Existing sample V1 canonical bytes are unchanged. Unknown versions are rejected.

## Additive storage choice and rollback

The merged sample ledger requires non-null SampleId and has a three-kind native CHECK.
Making it generic/nullable would require replacing SQLite's historical table/constraints
and carefully rebuilding dependent FKs/triggers. That alternative is avoided here.
Two new tables in `20261113143230_R3205PerItemRemoved`:

- ItemRemovalOperations: typed immutable sample-free ledger, restrictive existing batch,
  real attempt/item, source, actor and line FKs; unique Task/Operation, Task/Batch and attempt.
- SharedOperationIdentities: immutable derived Task/Operation uniqueness index across
  sample and item ledgers. Migration copies only actual existing operation IDs with
  Sample domain; no new accepted operation, sample, receipt, actor or timestamp is invented.
  Native insert triggers register both ledgers into its common PK atomically, so one
  operation ID cannot cross kinds. The sample insert trigger also rejects Removed attempts.

Common AcceptedBatchReceipts and SharedAcceptanceAudits remain in place. Old rows, columns,
FKs/indexes and existing triggers remain unchanged; only the new sample INSERT trigger is
added to that historical table. No ALTER/rebuild/drop or edited applied migration.
New item scope triggers validate actual TaskItem parent and source/owner/environment/
line/workflow, in addition to the composite attempt/item FK. Native UPDATE/DELETE
triggers and tracked guards retain both new histories. Privileged DB owners can still
alter protections; these are application guards, not a DB-administrator trust system.

Fresh/populated provider tests retain real generation/issuance identities and a synthetic
prior-version accepted capture/correction/dependency/receipt/audit fixture. Every old
row/column/FK/index and original trigger is fingerprinted; the one newly added INSERT
trigger is checked separately. Derived identity registration is checked one-for-one.
DDL/backfill/history-write failures must roll back this additive step and safely retry.
The earlier R32-03 SQLite rebuild caveat remains historical and unchanged.

Down refuses accepted-history deletion. Application rollback closes shared draft writes
and retains the expanded DB and PR #17 baseline; retain a version-aware receipt reader.
An older binary cannot authoritatively interpret Removed state/new typed receipts and
must not be advertised as equivalent recovery. Production inventory, migration, restore
drills, rollout and data reconciliation have NOT RUN.

## Acceptance boundaries

| Case | Implemented verification |
| --- | --- |
| RM-01 | Explicit single-item removal, both confirmation sources, truthful draft state |
| RM-02 | Actual item/attempt/task/line/workflow/environment, foreign/legacy/historical rejection |
| RM-03 | Optional reason,500 boundary contract and invalid controls/overlength |
| RM-04 | No placeholder/zero/sample/OK/N/A; no bulk/fake sample input |
| RM-05 | Frozen item/attempt/minimum and accepted sampled history unchanged |
| RM-06 | A/B/current authority, normal A lifecycle, invalid source quarantine, accepted A history after revoke |
| RM-07 | Four source-security writers, both orders; PG server blocking observed |
| RM-08 | Sample/item writers share one expected-version CAS |
| RM-09 | Exact retry versus changed payload/source/version and cross-kind IDs |
| RM-10 | Early404/late commit, ignored response, original operation/batch recovery |
| RM-11 | Repeated new removal and later sample writes rejected; old replay does not reapply |
| RM-12 | Multi-item and final applicable item warning/all-Removed draft, no formal state |
| RM-13 | Current auth/family/direct guards, unsupported frozen/legacy state |
| RM-14 | Root/CAS/batch/item ledger/audit/precommit rollback; prior sampled draft retained |
| RM-15 | Both native models, fresh/populated migration, identity index/immutability/scope and200-char environment |
| RM-16 | Full legacy/SharedDraft/WorkContext/identity/planning/backend/Web regressions |

Future formal completion, all-Removed PQE confirmation, inherited reinspection producers,
UnableToMeasureNG/photo evidence, F2/F3, Android/Web execution UI, sample reorder,
client process-death/real unknown-commit transport fault and production restore UAT are
DEFERRED / NOT RUN. Green foundation cases do not promote those requirements to PASS.

Developer checks caught and corrected a native PostgreSQL EnvironmentId width20 versus
the existing200-character contract (P2; DDL regression reproduced failure before fix,
plus a real PG200-character write twin). Security test barriers were strengthened to
exclude Admin login bookkeeping and observe pg_blocking_pids before release. An integrity
fixture originally violated batch and attempt uniqueness together; it now isolates the
intended attempt constraint. These test corrections are not application exploit claims.
Final complete-diff self-review and exact-HEAD execution counts/CI are recorded in PR delivery.

Stop after this one PR for independent ChatGPT Final Review and Peter's explicit merge
decision. No merge, deployment, production migration or subsequent PR is authorized here.
