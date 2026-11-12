# Typed shared draft operation acceptance foundation

Date: 2026-11-12. Scoped backend implementation for independent review, not deployment.
Base: `a3bab9b9a779bb36466277b32ea95392a0f59bba`, actual PR #18 merge, containing
reviewed feature HEAD `d3ae4ce61ef0f7d301ee7048e63b0fbed650e5fa`. Merged-main
[CI43307242759](https://github.com/peterl-ai/IPQC-system/actions/runs/43307242759)
completed SUCCESS before implementation. No conflicting open PR existed.

Authority: Peter's explicit typed-operation implementation prompt, approved
[Rev3.2](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md),
[supersession](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md),
and approved business/security invariants. The merged
[identity/acceptance proposal](architecture/IPQC_SHARED_INSPECTION_IDENTITY_ACCEPTANCE_CONTRACT_V1_2026-11-12.md)
is the engineering reference; publishing it did not globally approve every proposal.
[R32-05 identity](19_R3205_INSPECTION_IDENTITY.md) and Peter's D01(A)/Admin-only
initializer decision are retained. No new role, device trust or formal workflow.

## Bounded operation contract

- CaptureSample establishes a real sample map under a current real attempt and stores
  actual qualitative OK/NG or quantitative decimal value. Quantitative judgment uses
  the frozen numeric limits, not today's Standard. Optional repeatable barcode is
  quantitative-only, max200 characters, with control characters rejected.
- CorrectSample changes the same sample's typed value through a new immutable operation.
  It explicitly depends on the exact current predecessor operation ID/hash.
- RemoveSample tombstones that draft sample only. It requires the same exact predecessor,
  accepts no value and retains prior value provenance/history. It is NOT the entire
  item's future Removed business disposition. A tombstoned capture key cannot be
  resurrected; a new real capture uses a new ClientSampleId. No item disposition,
  fake zero, placeholder, measurement timestamp or reinspection event is generated.

ClientSampleId maps uniquely under InspectionAttemptId to a server SampleId. Existing
TaskItem/StandardItem/attempt IDs are never substituted. Capture sequence is durable
server acceptance order within the attempt, not proof of physical capture order/time.
No reorder operation in this slice. The final atomic projection may contain at most50
active samples per attempt; tombstones remain. Removing and capturing together is
validated on final state, independent of operation-list order. Frozen minimum metadata
is preserved, but incomplete drafts are accepted: formal completeness/evidence is NOT
evaluated and no Submit/status/revision/StartedAt transition occurs.

## API and current authority

All paths below are under `/api/line-patrol/tasks/{taskId}`.

| Method/path | Result |
| --- | --- |
| GET `/shared-draft` | Current version (0 when absent), actual current attempt/minimum scopes, sample maps/value provenance/tombstones/applicability |
| POST `/operation-batches` | Atomic fresh outcome or original durable receipts; HTTP200 does not mean formal inspection completion |
| GET `/operation-batches/{batchId}/receipt` | Original accepted batch receipt, or point-in-time404 |
| GET `/operations/{operationId}/receipt` | Original immutable operation receipt, or point-in-time404 |

Current active eligible IPQA, current auth version and personal family, actual task
workflow/line and both configured active lines are mandatory. POST additionally requires
the IPQA-only `LinePatrol.ContributeDraft` permission; read permission alone is not its
route policy. Direct service uses the same current database role/family authority under
locks. Existing Admin/PQE/legacy account API permissions and family compatibility stay
unchanged. No ownership transfer or legacy write bypass. Historical receipt recovery
requires current task-read authority, but not an editable task/current old attempt.

Every response retains `canExecute=false`; batch results also say
`acceptsFormalInspection=false`, and draft completeness is
`NotEvaluatedForFormalSubmission`. A sample's server judgment is not an item/formal
completion decision. No GET creates a draft, context, attempt, audit or acceptance fact.

Strict input fields: BatchId, CanonicalizationVersion1, expected actual line/workflow/
environment, ExpectedDraftVersion, Operations (max50), AcceptedReferences (max100),
SourceProofs (max50). New operation fields bind OperationId, version1, kind, actual
TaskItem/attempt, ClientSampleId, SourceWorkContextId, contributor assertion,
BaseDraftVersion, OriginLocalEditVersion, typed Data and Dependencies. Unknown members,
duplicate JSON properties (case-insensitive), quoted numeric coercion, duplicate IDs,
unsupported versions, missing/cyclic/wrong-scope/hash dependencies fail closed.
Auth/current-family precede binding, body parsing and the process-local limiter:
30 POSTs/min/user, queue0,128KiB including unknown-length bodies, JSON depth32.
All namespaces emit no-store on success/auth/error. No body/proof logging.

Errors: malformed shape/type/version400; authority401/403; resource scope404;
version/identity/manifest/state409; body413; limiter429; configuration/known contention503.
Invalid new source proof/owner/scope is409 SourceProvenanceConflict; sticky revoked or
ineligible new source is409 SourceSecurityRevoked. Quarantine responses include only
affected original OperationIds (including fresh dependent closure), source context IDs,
quarantineRequired and preserveOriginals. The complete new batch is rejected atomically.
No rejected payload is inserted into an accepted ledger/server quarantine vault.
Client queue preservation/re-performance remains a later client implementation.

## A/B provenance and exact manifest

A is resolved from the saved server WorkContext; the asserted contributor must match.
B is the actual first current authenticated uploader and family, separately persisted.
Proof establishes scoped issuance/proof possession, not authentication, physical
authorship or exact offline capture time. Saved eligible A work can be uploaded by B
after normal A logout/family/capture expiry/self-password change, without A login.
Admin reset/deactivation/IPQA loss/explicit source revoke block new A acceptance and
remain sticky after restoration. Accepted A facts/receipts keep their original A/B.

Resolve known operation IDs first. Accepted references need no old proof/current A
eligibility. Fresh sources require exact scoped proofs and current source-security
checks inside the final transaction. A mixed valid/invalid-source batch changes nothing,
including no deletion of previously accepted facts or conversion of A values into B values.
On sample removal, ValueOperationId/ValueContributorUserId stay with the last value
operation; LastOperationId identifies the separate removal action.

Manifest operations are a set. Capture has no dependency. Correction/removal have one
explicit current predecessor on the same sample/attempt. Fresh dependencies may point
to another operation in this batch or to an explicitly supplied accepted reference.
Cycle detection/topological order covers fresh nodes; accepted immutable records are
the sealed frontier. AcceptedReferences must equal that external frontier: no missing
dependency inferred from latest projection, no unexplained extra reference. Their
stored immutable dependency graph is authoritative; clients do not repost old source
proofs or the entire already-accepted ancestry. Fresh use requires current applicability.
Accepted matches mixed with fresh work also require current scope/applicability.
Receipt-only reconciliation does not re-evaluate editing eligibility.

## Version, durable receipts and canonical V1

SharedDraft is created only as part of a successful real fresh acceptance, never on
GET or migration. Under the Task lock, conditional SQL claims exactly expected v→v+1.
One fresh batch advances once and commits one immutable batch outcome, regardless of
operation count. Another writer expecting v conflicts; no last-write-wins.

TaskId/OperationId and TaskId/BatchId are durable replay scopes. Identical accepted
BatchId/fingerprint returns its original receipt before stale-version or fresh-source
tests. Same ID/different content/source/scope/version conflicts. A new BatchId containing
only accepted matches/references returns original operation receipts with ReplayOnly,
no new batch row/version/sample/audit. Exact accepted-batch recovery also says ReplayOnly
while returning its original BatchReceipt. A mixed fresh/known batch preserves original
per-operation B, never reapplies known operations, and stores one new batch outcome.

Operation canonical V1 has fixed field order, lowercase D GUIDs, exact environment/scope,
source/owner assertion, base/local versions, typed data and sorted dependency ID/hash
pairs. Decimal uses invariant G29 text, zero canonicalized to0. Null and empty are
distinct; Unicode is unchanged (no normalization), JSON escaping/UTF8 is specified by
the frozen byte vector. Set order is ordinal GUID text; hashes are uppercase SHA256.
Batch fingerprint covers exact scope, BatchId, expected version, sorted operation
hashes and accepted-reference set. It excludes raw proofs, stored proof hashes, JWTs,
refresh credentials and current B/family. Unsupported versions are rejected before
canonicalization rather than interpreting them as V1.

The immutable ledger stores the typed operation receipt, canonical payload/hash,
actual A/B/family, server acceptance UTC and resulting version. Batch receipt JSON is a
typed immutable snapshot of the original resolved receipt mapping, not an arbitrary
JSON write API. NotFound is only a point-in-time observation; it does not reserve an ID
or prove a delayed request can never commit. Reconcile original IDs/payloads/receipts;
do not create speculative revisions. No response is delivered before commit.

## Storage, locking and rollback

One additive migration: `20261112212127_R3204TypedSharedDraftAcceptance`, seven tables:
SharedDrafts, SharedSamples, SharedSampleProjections, ContributionOperations,
ContributionDependencies, AcceptedBatchReceipts, SharedAcceptanceAudits. No backfill,
old-table rebuild/ALTER, renamed old IDs, old migration rewrite or fabricated receipt.
Restrictive composite FKs bind sample/attempt/item/Task scopes, projection value/change
operations to the same sample, batch/operation/dependency/hash links, and audit actors.
New-table scope insert triggers additionally validate actual TaskItem parent/workflow
and WorkContext source/owner/environment/line scope. Native immutable UPDATE/DELETE
triggers plus tracked guards protect map/ledger/dependency/receipt/audit history.
No changes to historical-table constraints/indexes. Privileged DB owners remain able
to perform explicitly reviewed repairs; triggers are not a substitute for DB access control.

SQLite uses WorkContextStore's non-deferred writer reservation before authoritative
reads. PostgreSQL discovers immutable source-owner IDs, locks sorted requester/source
accounts FOR NO KEY UPDATE, revalidates current B, locks sorted configured lines FOR
SHARE, Task FOR UPDATE and contexts in order. No planning gate is taken afterward.
Source/user/explicit revoke writers serialize with that protocol. PR #17's issuance
MVCC publication fence and current-family ReadCommitted semantics are preserved.
Actual CAS, sample map, ledger, dependencies, projections, batch/operation receipts and
audit all save within that one transaction. Only known rolled-back contention receives
bounded whole-transaction retries (at most3 attempts,5s waits); no blind network/
unknown-commit retry. The client must reconcile durable receipts after uncertainty.

Native DDL review references: [PostgreSQL17 trigger functions](https://www.postgresql.org/docs/17/plpgsql-trigger.html)
and [SQLite triggers/RAISE](https://www.sqlite.org/lang_createtrigger.html).
Both-provider fresh/populated tests verify old rows/columns/FKs/indexes, including
real attempts, personal sessions and WorkContext audit. New-table/history-write fault
tests exercise additive-step rollback. Historical R32-03 SQLite rebuild/interruption
limitations remain unchanged. Down refuses destructive accepted-history removal.
Application rollback retains schema/history and closes draft ingress/workers before
older binaries; retain the source-security baseline. Restore keeps ingress closed until
account/context, version/receipt and projection history are reconciled. Production
inventory, upgrades, restore drills and deployment are NOT RUN here.

## Acceptance evidence boundaries

Executable tests are SharedDraftTests, SharedDraftSecurityTests,
SharedDraftManifestTests, SharedDraftConcurrencyTests, SharedDraftRollbackTests,
SharedDraftIntegrityTests, SharedDraftMigrationTests and SharedDraftCanonicalTests.
SharedDraftPostgresTests explicitly repeats provider-sensitive cases on disposable
PostgreSQL17 and is included in CI's filter. Exact counts/results and final-HEAD CI
URLs are verified in PR delivery; local opt-in SKIPs are never PG execution evidence.

| Case | Implemented/executable scope | Deferred / NOT RUN subcases |
| --- | --- | --- |
| OP-01 | Real capture mapping, correction/tombstone, equal values, frozen judgment, cap and actual attempts | Reorder; entire item dispositions; formal sample/evidence completeness |
| OP-02 | Typed manifest, IDs/hash/frontier, missing/duplicate/cyclic/foreign scope, dependent quarantine | Photo/evidence manifests and bytes (F3) |
| OP-03 | Exact batch/operation replay, identity conflict, original per-op uploader/time/version | Future operation kinds/canonical versions |
| OP-04 | HTTP/direct current authority, ContributeDraft, proof-not-auth, auth-before-binding, body/limiter | Android personal vault/queue |
| OP-05 | Normal A lifecycle versus all four sticky security actions, restoration does not revive | Client-side quarantine UI/re-performance |
| OP-06 | Both winner orders, mixed-source rollback, preserved accepted facts | Formal immutable submission integration |
| OP-07 | Independent same-version writers have one database CAS winner | Offline two-tablet UI/conflict preservation UAT |
| OP-08 | Early NotFound/late commit, original receipt recovery, no reapply, noneditable task recovery | Closed/replaced actual reinspection attempt producer; client process death |
| OP-09 | Root/CAS/map/ledger/dependency/projection/receipt/audit/precommit rollback and retry | Actual network unknown-commit transport fault/IT restore drill |
| OP-10 | Sorted multi-source owners, current B refresh/logout, real line lifecycle and source security, technical Task editability row transition | Formal Submit/PQE/reinspection producers and their mandatory version fences; future restore/F3 writers |
| OP-11 | Frozen bytes, decimals/order/null/Unicode, unsupported version, redaction and numeric shape | Kotlin interoperability when the Android project exists |
| OP-12 | Replay-only/mixed outcomes, same-sample FKs, native/tracked immutability and restrictive deletion | Future ledger/evidence kinds |

The technical Task transition test writes a noneditable row under the existing store
only to prove serialization; it does not fabricate a submission or claim a formal
workflow producer. Existing planning/legacy/security provider regressions remain
required. No green foundation test promotes any deferred subcase to PASS.

## Developer self-review correction

P2: the initial service copied mutable request collections only after awaiting the
store's locks, although source-owner discovery used the earlier collection. A caller
mutating those collections while waiting could make locking and validation inspect
different manifests. Acceptance now snapshots all collections, including dependency
lists, synchronously before lock discovery. Both providers run a deterministic
direct-service regression that pauses the authority read, mutates the originals,
then verifies only the frozen original operation is accepted. HTTP clients cannot
directly mutate an already-bound server DTO; no remote exploit is claimed.

Direct wrong-role/missing-family/forced-change negative checks and canonical
dependency/set order, null/empty and zero vectors were strengthened during review.
These are coverage improvements, not additional confirmed application defects.
The older development fixes for projection staging/value attribution and receipt
version/replay classification are part of the delivered implementation; no claim
is made that a green test proves the deferred business producers exist.

Stop after this one PR and verified CI for independent Final Review/Peter's merge
decision. No F2, F3, Android, formal Submit/PQE, production data reconciliation or
subsequent implementation PR is started automatically.
