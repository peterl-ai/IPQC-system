# R32-05 — UnableToMeasureNG draft disposition

Date: 2026-11-13. Status: scoped implementation for independent ChatGPT Final Review.
Peter authorized this single backend slice and explicitly selected correction **and
explicit withdrawal** with new immutable operations, exact predecessors and shared CAS.
This is not merge, production activation, formal Submit or subsequent-PR authorization.
[Approved Rev3.2](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md),
[Supersession](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md)
and approved business/security requirements remain authoritative. The
[identity/acceptance contract](architecture/IPQC_SHARED_INSPECTION_IDENTITY_ACCEPTANCE_CONTRACT_V1_2026-11-12.md)
does not approve unrelated proposed decisions. D01(A), actual initial attempts and
the Admin-only controlled existing-task initializer are unchanged.

Verified starting baseline: PR #22 merged reviewed HEAD
`20d6205a2135b0ccad43d0ec7199b7c88b56cfa0` as main
`423a68b87d39a47991be19a3dd1b4ba5a5c1f118`; the trees are identical.
Merged-main [CI43486785774](https://github.com/peterl-ai/IPQC-system/actions/runs/43486785774)
succeeded: backend679 PASS/277 opt-in PostgreSQL SKIP; real PostgreSQL17.12
277 PASS/0 SKIP; Web90 PASS/18 files plus lint/typecheck/build; Release0 warnings/errors.
No conflicting open PR existed at baseline. These are **base** results. Final feature
HEAD, final Push/PR runs and exact execution counts belong in the PR delivery.

## Business meaning and Peter's correction/withdrawal decision

UnableToMeasureNG records an inability to measure with mandatory NG Reason and genuine
current-attempt evidence. It is distinct from [Removed](21_R3205_ITEM_REMOVED.md).
It creates no sample, fake zero, OK, N/A, inspection attempt, measured-count fact or
formal inspection result. Current attempt identity and frozen minimum remain unchanged.
Existing sampled values, definitions, attempts, accepted operations and receipts remain
immutable history. Active UnableToMeasureNG makes those samples non-applicable in the
current draft view, without modifying them; withdrawal restores their applicability,
not a claim that the draft is complete or OK.

| Current state / operation | Required outcome |
| --- | --- |
| Ordinary unfinished draft / SetUnableToMeasureNG | New reason and exact eligible accepted photo reference; establish active disposition |
| Active / CorrectUnableToMeasureNG | New OperationId, exact current predecessor/hash, reason and evidence; retain old reason/evidence receipt |
| Active / ClearUnableToMeasureNG | New OperationId, exact current predecessor/hash; reason/evidence must be null; return to ordinary Applicable unfinished draft |
| Withdrawn / SetUnableToMeasureNG | Exact withdrawal predecessor/hash, fresh version/source and eligible reason/evidence |
| Active / fresh sample Capture, Correct, Remove or item Removed | Conflict; exact already accepted sample replay remains recoverable |
| Removed / new UnableToMeasureNG | Conflict; no implicit conversion between these dispositions |

Reason is nonblank text of at most2000 UTF-16 code units, aligned with the existing
legacy NG-reason bound. Control characters are rejected. Exact text, spaces and Unicode
are retained in canonical bytes; no silent trimming, normalization or old-receipt editing.
Clear never becomes OK/Removed and never lowers minimums. Current reason and accepted
history remain inspectable; no unannounced irreversible workflow is introduced.

## Evidence and truthful provenance

Evidence is an exact OperationId/content hash of a genuinely accepted
[AssociateAttachment](23_R3206_DRAFT_ATTACHMENT_REFERENCES.md) operation, for this
actual Task/Item/current InspectionAttempt/line/environment/workflow2. Its attachment
must still be attached through that exact latest reference operation and have actual
Ready acceptance plus available, hash/decoder-verified protected bytes. Storage Ready
alone, an unassociated reservation, arbitrary client claim, foreign item/attempt,
detached/superseded reference or unavailable/corrupt bytes cannot substitute. No old
submitted-attempt evidence is rebound to a current attempt. A real replacement/targeted
reinspection producer remains deferred; stale-pointer and mismatched actual-identity
negative tests do not claim that producer has been implemented.

The selected photo is already business accepted. Its original A/source and first storage
accepting uploader B/family, and its reference-operation contributor/actor/family, remain
the original sealed facts. Later security changes to that historical A do not erase
accepted work. The **new disposition operation's own source** is independently validated
under account-first locks. Its contributor derives from the saved context owner, while
actual accepting actor/family derives from current server authority; all can differ.
WorkContext is scoped provenance, not authentication, physical authorship, accepted
inspection work, completeness or Submit permission. Preflight accepts no business work.

Later detach, missing/corrupt/unavailable bytes or superseded applicability does not
rewrite the original disposition/evidence receipt. The dedicated read returns current
physical/reference observation and `DraftEvidenceStatus=Incomplete` for active invalid
evidence; absent attempt identity reports Unavailable/AttemptUnavailable. Reattaching a
photo does not silently repair the old evidence binding: an explicit correction chooses
the new accepted reference. Clear can recover an unfinished draft even if bytes are lost.
Future Submit must independently resolve applicable evidence and completeness again.

## Typed API, replay and bounded reads

Paths below are under `/api/line-patrol/tasks/{taskId}`. Current eligible IPQA account,
signed auth version/current personal family, active configured actual line/environment
and task workflow scope are enforced independently, including receipt recovery.

| Method / path | Contract |
| --- | --- |
| POST `/item-disposition-batches` | One immutable typed operation; current ContributeDraft authority; atomic accept or exact original receipt replay |
| GET `/shared-draft/item-dispositions?beforeDraftVersion=...` | Current per-item reason/evidence observation and bounded immutable history, no accepting write |
| GET `/operation-batches/{batchId}/receipt` | Original common receipt, ReceiptVersion4/UnableOperations for this domain |
| GET `/operations/{operationId}/receipt` | Original typed disposition receipt; existing sample/Removed/reference receipts unchanged |

Batch fields: BatchId, CanonicalizationVersion1, ExpectedTaskLineId,
ExpectedWorkflowVersion2, ExpectedEnvironmentId, ExpectedDraftVersion, Operation and
optional exact SourceProof. Fresh operations require their proof; known replay needs no
renewed proof. Operation fields: OperationId, CanonicalizationVersion1, Kind, TaskItemId,
InspectionAttemptId, SourceWorkContextId, ContributorUserIdAssertion, BaseDraftVersion,
OriginLocalEditVersion, NgReason, Evidence and Predecessor. References contain only
OperationId/content hash. One operation avoids implicit intra-batch ordering/auto-merge.

Canonical V1 has distinct unable-to-measure and batch domains, fixed field order,
canonical GUIDs, uppercase hashes, explicit nulls, exact text and immutable source,
scope, local/base version, kind, evidence and predecessor. It excludes proof/current
actor credentials; the server seals actual A/B/family/time/version in original receipt.
Known task-scoped BatchId/OperationId are resolved before fresh-source/editability or
physical availability checks. Changed accepted payload/source/version/identity conflicts.
Exact replay after security revoke or physical loss returns original facts without
reapplying acceptance, changing version/audit or declaring evidence currently valid.
A new BatchId containing only the known operation returns its old operation receipt and
no new batch row. Early404 is point-in-time only; late commit/lost response requires
original-ID receipt reconciliation, not an invented replacement operation.

Strict typed JSON rejects unknown/duplicate members and quoted numbers; inherited
128KiB/depth32 and 30 mutation/min/user limits apply. Photo request limits/deadline and
decoder concurrency also cover physical evidence reads. Responses use no-store and
expose no proofs/stored proof hashes, credentials, storage keys, filesystem paths or
anonymous/public file URLs. No request-body logging is introduced.

History returns at most100 complete accepted versions (one operation each here), an
exclusive positive HistoryBeforeDraftVersion cursor, and current version/projection
independently of the selected historical page. Old rows remain retained. The general
shared-draft read exposes disposition/last receipt but explicitly does not evaluate
physical/formal completeness; the dedicated disposition read supplies physical observation.
All shared outputs retain `canExecute=false`, `acceptsFormalInspection=false` where
present, and completeness NotEvaluatedForFormalSubmission. Receipts are facts, not grants.

## Schema, concurrency and rollback

Additive migration: `20261113190630_R3205UnableToMeasureNgDraft`.

| New table | Purpose |
| --- | --- |
| AcceptedOperationIdentities | Derived Task/Operation exclusion key across sample, Removed, reference and disposition domains |
| UnableToMeasureOperations | Immutable typed scope/provenance/reason/evidence/predecessor/canonical/hash/time/version/original receipt ledger |
| UnableToMeasureProjections | Same-attempt current last operation and active/withdrawn state |

Old registries have historical restricted Domain CHECKs. The new universal registry
backfills **only actual accepted DraftOperationKeys**; additive registration triggers
bridge old-domain and new-domain writes without changing old CHECKs, rebuilding old
tables or inventing acceptance. Domain is a bounded descriptive label, not permission.
Old-domain duplicates still reach their original PK errors; newly conflicting domains
map to409. Restrictive FKs bind real attempt/item, source owner, exact accepted evidence,
same-attempt predecessor and projection operation. Native immutable/scope/predecessor/
active-state guards supplement tracked guards; additive native sample/Removed guards
block incompatible fresh writes while leaving known receipt recovery intact.

All four write domains use the same SharedDrafts DraftVersion conditional SQL CAS.
Exactly expected v becomes v+1 with common receipt, typed ledger, exclusion key,
projection and acceptance audit in one transaction. Any failure rolls all DB effects
back, including audit/precommit failures; no partial invalid-operation acceptance.
No Task status/StartedAt/formal revision/Submit/PQE mutation occurs.

SQLite reserves a non-deferred writer before authoritative reads. PostgreSQL17 uses
ReadCommitted with sorted current actor/new-source owner accounts FOR NO KEY UPDATE,
current family, sorted configured lines FOR SHARE, Task FOR UPDATE, sorted new-source
contexts, then evidence availability FOR UPDATE. Accepted photo sources are historical
dependencies, not newly accepted source work. No planning gate or earlier account lock
is acquired after Task. Existing PR #17 AuthVersion publication fence remains unchanged.
Only known rolled-back contention is retried, bounded3 attempts/5s waits.

Real evidence bytes are read/hash/decoder checked before CAS under DB locks; this path
does not stream/write/promote/delete files. DB and filesystem are **not jointly atomic**:
external privileged tampering/hardware loss can occur after the probe. Retain truthful
history, fail closed on observed loss and revalidate later. Future F3 writers must share
availability locking; no deletion claim exists now.

Migration tests fingerprint old rows/columns/FKs/indexes/receipt JSON across populated
sample/Removed/real Ready/reference data, and inject rollback at table/backfill/trigger/
migration-history points. Old additive triggers are not replaced. Down refuses destructive
removal. Application rollback retains expanded schema and requires closed new ingress,
reviewed security/receipt reconciliation and coordinated restore before reopening.
Production migration/inventory/restore/power-cut qualification remain external gates.

## Acceptance evidence and adversarial developer review

Paths below are under `api/JaxPower.Ipqc.Api.Tests/`. Names refer to executable test
cases, not claims that deferred workflows ran. UnableToMeasurePostgresTests explicitly
executes72 real-provider cases in CI; all existing PostgreSQL security/sample/item/photo/
reference classes remain selected. PostgreSQL opt-in SKIPs never count as live evidence.

| Requested case | Executable evidence |
| --- | --- |
| 01 mandatory reason | UnableToMeasureTests.Lifecycle, Reason |
| 02 blank/null/oversized reason | Reason five inputs; Conflicts2000/2001 |
| 03 genuine current-attempt reference | Lifecycle, real protected bytes and accepted association |
| 04 Ready without reference rejected | Evidence unassociated/pending; forged client claim creates no server fact |
| 05 wrong scope | Scope actual other item/attempt/task, line/environment/workflow/hash |
| 06 stale/detached/unavailable | Scope stale; Evidence missing/corrupt/detached/superseded; LaterLoss |
| 07 no fake values/sample | Counts; Lifecycle retained snapshots and unchanged sample ledger |
| 08 minimum unchanged | UnableToMeasureReadTests.Minimum, real generated frozen minimum5 through set/correct/clear |
| 09 immutable history | Lifecycle original JSON/replay; MigrationTests.Integrity native/tracked rejection |
| 10 Removed distinct | Scope removed; Writes fresh Removed blocked, no automatic conversion |
| 11 truthful A/B/family | Lifecycle distinct new source/actor and retained photo A/uploader B/reference actors |
| 12 security both orders | ConcurrencyTests.Security eight cases, real PG blocking observation |
| 13 ordinary A lifecycle | Normal logout/expiry/self-password/refresh, security state assertions |
| 14 cross-domain CAS | ConcurrencyTests.Cas four first writers, one persisted version/receipt/audit |
| 15 replay/changed IDs | Lifecycle, Conflicts, MigrationTests.Identities four domains incl unsupported version |
| 16 lost response | ConcurrencyTests.Lost early404/late commit and injected post-commit response failure |
| 17 atomic rollback | RollbackTests.Fresh six points and Clear three points |
| 18 no formal task change | Counts, retained actual item/attempt/task facts, no Submit/review rows |
| 19 auth/role/family/direct | Authority HTTP/direct boundary; Logout both orders |
| 20 SQLite / PostgreSQL | Native Integrity, CAS/security/rollback plus explicit live PG twins |
| 21 fresh/populated migration | Both_models_and_strictly_additive_native_DDL; Upgrade/Rollback, actual fresh fixtures |
| 22 retained regressions | Full Release suite; all existing security/draft/Removed/photo/reference classes in CI |
| 23 canExecute=false | Lifecycle/Counts/read defaults; no execution activation |

Confirmed P3 self-review defect: the first implementation resolved accepted IDs from
other domains **after** rejecting unsupported canonical versions, returning400 instead
of409 for an already accepted ID. An added four-domain regression produced3 failures
and1 pass before the fix, specifically the sample/Removed/reference collisions.
Acceptance now queries the universal exclusion key before that version branch; genuine
unknown unsupported versions remain400. The unchanged negative cases run on both
SQLite and real PostgreSQL; no acceptance/auth bypass was observed in this defect.

Initial test-fixture correction paired a changed source context with its actual matching
proof instead of testing a malformed envelope. Windows restricted-identity photo ACL
failures are environmental and kept separate from actual-user execution results.
Review covers actual endpoint/service/model/native migration/tests/CI and complete diff,
including generated model delta; this document is not proof of correctness.

## Deferred work and external gates

No formal Submit/status transitions, ordinary NG/NG-to-OK completeness, PQE approval/
rejection/special confirmation, NotPerformed, new targeted reinspection producer, full
F2 sealed intent/takeover, F3 retention/cleanup/deletion, reference-image administration,
Android capture/offline sync, Web shared execution or device PKI/enrollment/mTLS.
No legacy workflow, MES/material verification or deployment change.

Private volume/ACL/service identity and production filesystem qualification, coordinated
backup/restore ownership, monitoring, proxy/TLS and two-tablet/QA evidence readability
remain external prerequisites. Only disposable test databases/files are manipulated.
No production IDs, paths, acceptance facts, attempts or credentials are invented.
Stop after this single PR for independent Final Review; only Peter decides merge.
