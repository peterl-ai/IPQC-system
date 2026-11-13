# R32-06 — Atomic shared draft attachment references

Date: 2026-11-13. Status: scoped implementation for independent Final Review;
not merge approval, production activation, formal Submit or a subsequent PR.
Peter's explicit attachment-reference implementation authorization applies.
[Approved Rev3.2](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md),
[Supersession](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md)
and approved business/security requirements take precedence. Publication of the
[identity/acceptance proposal](architecture/IPQC_SHARED_INSPECTION_IDENTITY_ACCEPTANCE_CONTRACT_V1_2026-11-12.md)
does not approve its unrelated proposals. D01(A) and the Admin-only existing-task
initializer remain unchanged.

Verified base: PR #21 merged reviewed HEAD `6ad672cb85d6da57f2f9d7e806fccec2b4d5be92`
as main `c571c40a02e4975db9d5d07f1f9c11fb461b90f0`, with identical trees.
No conflicting open PR existed. Merged-main [CI43470659917](https://github.com/peterl-ai/IPQC-system/actions/runs/43470659917)
succeeded: backend612 PASS/213 opt-in PostgreSQL SKIP; real PostgreSQL17.12
213 PASS/0 SKIP; Web90 PASS/18 files and lint/typecheck/build; Release0 warnings/errors.
These are base counts. Final feature HEAD results and Push/PR URLs belong to PR delivery.

## Meaning and actors

[Storage Ready](22_R3206_PROTECTED_PHOTOS.md) is immutable first storage acceptance,
not an accepted inspection-draft reference. A new explicit AssociateAttachment operation
incorporates an actual byte-backed Ready object into the same real Task/Item/current
InspectionAttempt. No sample, measured value, capture time, physical authorship,
inspection attempt, formal revision or completion fact is invented.

The receipt retains photo source WorkContext and contributor A, first actual storage
accepting uploader B/family, reference-operation source/contributor, and actual current
reference accepting actor/family. They may differ. Reference attribution derives from
the validated source record, never a client-selected name; current transport authority
is checked independently. WorkContext possession proves scoped provenance only, not
authentication, physical authorship, accepted work, inspection completeness or Submit.
New edits use their own originating context; eligible queued historical operations may
be transported by another currently authorized IPQA. Capture/EXIF clocks are not authority.

## API and bounded typed V1 contract

All paths below are under `/api/line-patrol/tasks/{taskId}`. Current eligible IPQA,
signed auth version, current personal family, configured environment/active actual lines
and task workflow scope remain mandatory. POST also requires `LinePatrol.ContributeDraft`.
Existing legacy inspection/auth/Admin endpoints retain their original behavior.

| Method / path | Meaning |
| --- | --- |
| POST `/attachment-reference-batches` | Atomic typed fresh association/detachment, or original receipts for exact accepted replay |
| GET `/shared-draft/attachments?beforeDraftVersion=...` | Current associations and a historical page, with actual physical availability; no acceptance on GET |
| GET `/operation-batches/{batchId}/receipt` | Original common batch receipt, unchanged stored JSON semantics |
| GET `/operations/{operationId}/receipt` | Original typed sample, item-removal or attachment-reference receipt |

Batch input: BatchId, CanonicalizationVersion1, expected actual Task line/workflow2/
environment, ExpectedDraftVersion, Operations1..10 and exact fresh SourceProofs0..20.
Only one operation per AttachmentId in a batch. Multiple operations on that same object
require separate explicit version transitions; no inferred intra-batch chain/merge.

Operation input: OperationId, CanonicalizationVersion1, Kind=AssociateAttachment or
DetachAttachment, actual TaskItemId/InspectionAttemptId, AttachmentId/ClientPhotoId/
SHA256, SourceWorkContextId, matching contributor assertion, BaseDraftVersion,
OriginLocalEditVersion, and optional exact Predecessor OperationId/content hash.
Association starts without a predecessor. Detach/reassociation must name the exact
current accepted predecessor for this same object; duplicate active association,
stale/foreign/wrong-hash predecessor and undeclared guessed history conflict.

Unknown members, duplicate JSON names/IDs/objects, quoted numbers, missing source proof,
wrong owner/source/scope/attempt/hash, unsupported versions and oversized operation/body
sets fail closed. Auth/current-family precede binding. Shared JSON limit128KiB/depth32,
30 mutations/min/user, photo20 requests/min/user, four photo request slots, two decoder
slots and cooperative two-minute deadline apply. Reads decode one bounded object at a
time, not all image bytes concurrently. Secrets, stored proof hashes, storage keys,
filesystem paths and reusable credentials are absent from responses/canonical facts.
No public file URL, anonymous read or request-body logging is introduced.

History pages include at most100 complete accepted attachment-batch versions (at most
1000 operation receipts). `HistoryBeforeDraftVersion` is the exclusive cursor for the
next older page, null at the end; positive cursors only. Each page separately resolves
the actual latest association projection, including when its last operation lies outside
the requested page. No historical rows are discarded. Stable individual receipts remain
recoverable independently. The Task/version returned is the current snapshot, not a claim
that a historical page represents today's complete draft.

## Source-security acceptance boundary

First business association validates both the new reference operation's source and the
photo's immutable original source A, under the final lock set. The client cannot omit A
from lock discovery or rely on an earlier preflight/Ready timestamp. Ready-before-security-
revoke but association-after-revoke is rejected/quarantined. Mixed invalid work changes
nothing; responses name original affected OperationIds/source IDs, preserveOriginals and
quarantineRequired. Bytes and previously accepted facts are retained, not relabeled.
No server quarantine vault, automatic client isolation or re-performance UI is claimed.

Explicit source revoke, Admin password reset, deactivation and IPQA loss block new
unaccepted A business work. Normal A logout/refresh/capture or family expiry/self-password
change do not sticky-revoke historical provenance. Current actor role/auth/family is
independently revalidated, even on accepted replay/receipt recovery.

Known accepted IDs/hashes are resolved before fresh-source/editability/readiness checks.
Exact accepted replay returns original A/B/reference actor/time/version, even after later
source security changes or physical loss; it does not reapply a reference. An explicit
new detach uses its new operation source, preserving accepted photo provenance without
renewed A eligibility. Reassociation to the same current attempt may use that object's
exact accepted detached predecessor as a sealed business frontier: validate new operation
source, current state/version and actual Ready bytes, preserving old accepted A/B.
This cannot incorporate a never-business-accepted photo from revoked A or reuse it in
another/new inspection attempt. Future Submit must independently resolve applicable
accepted evidence and required current-attempt/new-evidence rules.

## Lifecycle, CAS and immutable receipts

| State / event | Behavior |
| --- | --- |
| Ready, no reference | Existing safe photo metadata; no new projection/operation is manufactured |
| First explicit association | Genuinely Ready/available actual bytes, exact immutable scope, eligible current attempt/source and shared version required |
| Attached draft | Current projection points to an immutable AssociateAttachment operation |
| Detach | New immutable operation and detached projection; no file delete, upload/acceptance/history rewrite or minimum change |
| Item subsequently Removed | Old association/history retained, IsCurrentApplicable=false; no new active association; explicit detach remains permitted |
| Stale attempt / noneditable task | No new acceptance; original receipts/history remain readable under current task-read authority |
| Missing/corrupt bytes | New association fails; read reports MissingUnexpected/Corrupt; old acceptance remains historical fact |

Detachment does not require bytes to still exist and does not repair missing bytes.
This read endpoint reports its physical observation without mutating accepted history;
the existing photo metadata/content probes retain their availability/audit protocol.
Reservation cap5/item/attempt (including pending/failed) remains; reference projection
also independently enforces at most5 active objects per attempt. Detach does not release
a reservation slot or create a new photo identity.

Sample Capture/Correct/Remove, Item Removed and references share actual SharedDrafts.
One fresh batch conditionally claims exactly expected v→v+1, then atomically commits
common batch receipt, reference ledger, current projection, ID registration and audit.
Any failure rolls all DB effects back; existing accepted facts remain intact. No
last-write-wins, empty accepting CAS, formal status/StartedAt/revision/Submit/PQE action.
Shared outputs keep `canExecute=false`, `acceptsFormalInspection=false` and completeness
NotEvaluatedForFormalSubmission. Individual receipts describe facts, not permissions.

Replay scope stays `(TaskId,OperationId)` / `(TaskId,BatchId)`, matching existing typed
ledgers; stable client IDs must be globally generated, not sequential display indices.
Cross-kind reuse of the same task-scoped OperationId conflicts. Changed canonical
payload/source/scope/version under an accepted ID conflicts. A new BatchId containing
only exact accepted operations returns their old receipts, no new batch/version/audit.
Mixed fresh/known operations accept only fresh ones and preserve all old per-op actors.
404 is point-in-time only; late commit and lost response require original ID/payload
receipt reconciliation. Unknown outcomes are not blindly retried/rebased.

Canonical V1 has a distinct draft-attachment domain, fixed field order, canonical GUIDs,
uppercase SHA256, exact environment/scope/source/local/base/typed predecessor fields,
explicit null, unchanged Unicode and UTF8 JSON escaping. Batch fingerprint covers sorted
operation hashes and frozen envelope/expected version. It excludes proofs/current actor
credentials; server-derived immutable photo identity binds retained original provenance.
Batch receipts use ReceiptVersion3 and AttachmentOperations; existing nullable extensions
are omitted on old sample/item receipts. Old persisted ReceiptJson is never rewritten.

## Transactions and additive migration

`20261113172351_R3206AtomicDraftAttachmentReferences` adds three tables:

| Table | Purpose |
| --- | --- |
| DraftOperationKeys | Derived uniqueness registry across Sample/ItemRemoval/AttachmentReference; backfill only actual old registered IDs |
| DraftAttachmentOperations | Immutable typed accepted ledger, actual scope/A/B/reference actors, predecessor, canonical/hash/version/time/original receipt |
| DraftAttachmentProjections | Atomic current attached/detached state with restrictive same-object accepted last-operation FK |

No applied migration or existing Domain CHECK is rewritten; no historical table rebuild.
New registration triggers on old SharedOperationIdentities and new reference inserts
provide cross-domain uniqueness without changing old accepted rows. Identity-boundary
handling translates only this registry's actual native conflict to409 after rollback.
New restrictive FKs and native scope checks bind actual photo/acceptance/item/attempt/
Task/source/provenance/batch version; exact predecessor cannot cross objects. Native
immutable UPDATE/DELETE and projection scope/history guards supplement tracked guards.
History index supports bounded task/version queries. Down refuses destructive removal.

SQLite reserves its non-deferred writer before authoritative reads, bounded wait/retries.
PostgreSQL discovers immutable photo/source owners, locks sorted current actor/source
accounts FOR NO KEY UPDATE, current family, sorted configured lines FOR SHARE, Task FOR
UPDATE, sorted source contexts, then sorted attachment availability rows FOR UPDATE.
Reference sources and photo A can be different; both owner sets are locked. No planning
gate or earlier account lock is acquired after Task. PR #17's publication fence is intact.
Photo Ready/recovery and new references use compatible Task→availability order.
Conditional SQL still fences externally observed draft versions; no last-write-wins.

Read-only physical file/hash/full image verification occurs under the acceptance lock
set before CAS. No file streaming/write/promotion/delete occurs in this DB transaction.
Only known rolled-back contention retries, at most3/5s waits. Commit precedes success.
DB and filesystem are **not atomic together**: future hardware loss/privileged external
tampering can invalidate bytes after a successful probe. Fail closed on observed loss,
retain truthful history and require later explicit maintenance/recovery. Future F3
reference/deletion writers must share these attachment locks; no deletion claim exists now.

Fresh/populated SQLite and real PostgreSQL tests fingerprint all old rows/columns/FKs/
indexes, including byte-backed Ready photos and exact old receipts. SQLite fingerprints
exclude only explicitly reviewed new additive registration triggers; original triggers
are not replaced. Migration fault tests cover table, registry backfill, trigger and
history-write rollback. Retain expanded schema on application rollback, close ingress
before older binaries and reconcile source security/versions/known receipts before any
restore reopening. Production migration, inventory, coordinated restore and power-cut
qualification are external gates, not tests claimed here.

## Acceptance evidence and developer review

Paths below are under `api/JaxPower.Ipqc.Api.Tests/`. Exact execution results and final
HEAD CI belong in the PR delivery; opt-in PostgreSQL SKIPs are never live PG evidence.
DraftAttachmentPostgresTests is explicitly included in CI's PostgreSQL17 filter;
all previous security/identity/sample/item/photo classes remain included.

| Requested case | Executable evidence |
| --- | --- |
| 01 | DraftAttachmentTests.Lifecycle, real Ready bytes and current projection |
| 02 | Lifecycle before first acceptance; no accepting row/version |
| 03 | Readiness pending/missing/corrupt/unavailable; actual bytes checked |
| 04 | Scope actual other item/attempt/task/client/hash/line/environment/workflow |
| 05 | Scope stale current pointer; real reinspection/replacement producer remains deferred |
| 06 | Scope Removed; UnavailableDetach retains but cannot reacquire active association |
| 07 | Lifecycle original photo A/first B/family retained |
| 08 | Lifecycle distinct reference source and accepting actor/family |
| 09 | Revoked plus eight Security race cases and preserved accepted replay |
| 10 | Normal logout/expiry/self-password/refresh, actual security state assertions |
| 11 | Authority HTTP/direct/family/role/logout and Logout both orders |
| 12 | Lifecycle exact batch/op replay; no reapplication/version bump |
| 13 | Conflicts payload/source/version/predecessor; Identities three domains |
| 14 | Cas three possible first winners, persisted one version/receipt/audit |
| 15 | Lifecycle detach preserves ledger, source and immutable original receipts |
| 16 | Lost early404/late commit and injected post-commit response failure |
| 17 | Fresh seven fault points; Detach projection/audit/precommit rollback |
| 18 | Limits five Ready references one batch; Authority bounds and duplicates |
| 19 | PG Security actual pg_blocking_pids, CAS, Logout and ReadyRace |
| 20 | Both_provider_models/Upgrade/Rollback/Integrity, fresh and populated providers |
| 21 | Lifecycle byte-identical file after detach; no delete API introduced |
| 22 | Counts and retained item/attempt/sample snapshots, no Submit/PQE/results |
| 23 | Full retained WorkContext/security classes in CI |
| 24 | Full SharedDraft/ItemRemoval/Photo regressions in CI |
| 25 | Lifecycle/Limits current outputs remain canExecute=false |

Developer adversarial review confirmed P2: the initial history reader materialized the
whole ever-growing ledger using an unbounded ToListAsync. A101-real-operation regression
returned101 versions instead of the chosen100-version resource bound (1 FAIL before fix).
The corrected reader pages complete versions, uses a task/version index and independently
resolves latest projection. The same test now checks both pages, current last operation,
no discarded rows and invalid cursors; a live-PG twin is required in CI. This is bounded
resource prevention, not a claim that an out-of-memory exploit was demonstrated.

Initial development model-name mismatch was corrected before migration/use; both-provider
model equality and native DDL regressions guard it. Cross-task negatives use another
actually generated/initialized readable task. Security barriers exclude Admin login and
observe real PostgreSQL blocking; no failing race is rerun merely to obtain green.
P3 compatibility finding: the first full regression run exposed that the additive ID trigger preempted the old
SharedOperationIdentities PK error for sample/item duplicates. Registration now defers
to that original PK when an old-domain identity already exists; only reference-only
collisions use the new guard. The original integrity regression is retained unchanged.
No self-written summary substitutes for reviewing the final complete diff.

## Deferred requirements and external gates

No Android camera/gallery/offline coordination, Web shared execution, formal Submit,
PQE Approve/Reject/special confirmation, new reinspection producer, NG/NG→OK/photo
completeness, UnableToMeasureNG, F2 sealed intent/takeover, reference-image administration,
five-year worker, physical deletion, F3 cleanup claims/leases/tombstones or automatic
restore reconciliation. Green foundation cases do not promote these to PASS.

Existing private-volume/root/ACL/service identity, production OS/filesystem qualification,
backup ownership/coordinated restore, disk monitoring, proxy/TLS, actual deployment and
two-tablet/QA evidence readability remain external prerequisites. No production paths,
identifiers, device PKI/enrollment/mTLS, production files or credentials are invented.
Only Peter decides merge and separately authorizes production operations. Stop after
this PR for independent ChatGPT Final Review; no automatic subsequent branch/PR.
