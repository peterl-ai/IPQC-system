# R32-05 — Explicit sample-transition photo evidence bindings

Date: 2026-11-13. Status: scoped implementation for independent ChatGPT Final Review.
Peter's attached Explicit Sample-Transition Photo Evidence Binding authorization and
his two explicit A decisions apply. This document grants no merge, production,
formal Submit or subsequent-phase authorization.

Authoritative requirements remain [approved Rev3.2](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md),
[Supersession](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md),
and approved decisions in the [identity/acceptance contract](architecture/IPQC_SHARED_INSPECTION_IDENTITY_ACCEPTANCE_CONTRACT_V1_2026-11-12.md).
Other proposed decisions are not newly approved by this implementation.

## Exact starting gate

PR [#24](https://github.com/peterl-ai/IPQC-system/pull/24) merged reviewed HEAD
`c2a6efd6badaac6957c982e18eaa2a316d262ebf` as actual merge/latest main
`1dd00797af4fa68b44411956ea7736a457326455`, with identical trees. No open PR
existed before this slice. Merged-main [CI43506232225](https://github.com/peterl-ai/IPQC-system/actions/runs/43506232225)
completed successfully before coding: Release zero warnings/errors; backend792 PASS,
389 opt-in PostgreSQL SKIP, zero FAIL; actual PostgreSQL17.12 389 PASS, zero SKIP/FAIL;
Web90 PASS/18 files, lint/typecheck/build. These are baseline results. Final exact-HEAD
Push and PR run/job links and counts are reported in this PR's delivery, not inferred
from this baseline or the test inventory below.

Branch: `codex/r32-05-sample-evidence-bindings`. The original checkout remains on its
own branch with its unrelated22 untracked files; baseline names/hashes were recorded.

## Peter's capacity and amendment decisions

1. Accept explicit association facts only. One genuine current Item/Attempt photo can
   associate with multiple genuine sample operations. Five reserved photos remain
   five, even for fifty samples. A binding does not establish coverage, newness,
   physical capture time, authenticity, physical authorship or a formal inspection.
   No one-photo-per-sample rule or extra photo capacity is invented.
2. Incorrect bindings can be corrected or explicitly withdrawn with a new OperationId,
   exact predecessor ID/hash and shared CAS. History remains immutable. No sample,
   photo-reference, frozen minimum or reserved slot changes as a side effect.

Each logical BindingId fixes its target sample operation and is the initial bind's
OperationId. Correction may select another eligible photo reference. A wrong target
is withdrawn and a new binding root is explicitly created; correction cannot turn
the old fact into another sample/transition. Withdrawal is terminal for that root;
a new association needs a new root. Withdrawal may follow Removed, active Unable,
detach or lost bytes, within a still-current editable attempt. It cannot revive a
sample, attach a photo or convert a result to OK.

## Genuine identity and server classification

A fresh bind/correction references one actual immutable ContributionOperation ID/hash
and one actual immutable AssociateAttachment ID/hash. No client sample/attempt,
acceptance claim, photo ID alone, Ready reservation alone, timestamp or classification
can substitute. Task, Item, current real Attempt, line, environment and workflow2
must agree. An accepted historical sample operation in the current attempt is a
valid target fact; `IsCurrentSampleValue` separately reports whether it is still the
current nonremoved value. Later sample correction/removal never silently retargets it.

Server classification reads actual accepted receipts and exact dependency edges:

| Accepted target | Derived association classification |
| --- | --- |
| CaptureSample, actual server judgment NG, no dependency | InitialNGCapture |
| CorrectSample, exactly one real same-sample predecessor NG, target OK | NGToOKCorrection |
| CorrectSample, exactly one real same-sample predecessor NG, target NG | NGToNGCorrection |
| CorrectSample, exactly one real same-sample predecessor OK, target NG | OKToNGCorrection |
| OK capture, OK-to-OK correction, RemoveSample, invented or mismatched operation | Rejected; no transition fabricated |

Dependencies can be in the same previously accepted batch/version, with reverse GUID
order. Classification never relies on GUID sort, an increasing timestamp, client
judgment, or on calling a draft correction a physical improvement/reinspection event.

Fresh nonwithdrawal requires a current exact attached reference, original Ready
acceptance, Ready availability and actual protected bytes passing size/hash/type/full
decode verification. Detached, superseded, corrupt, missing or another scope fails.
Original sample/photo/reference source A, contributor, uploader B/family and actor
receipts remain unchanged. The binding records its own source contributor and its
own independently authorized online actor/family; these need not be the same people.

## API, canonical identity and recovery

All routes are under `/api/line-patrol/tasks/{taskId}` and require current eligible
IPQA/current personal family plus actual active configured Task/Line/workflow scope.
Direct service calls enforce the same authority. WorkContext proof is provenance,
not authentication, permission, physical authorship or proof of accepted work.

POST `/sample-evidence-batches` accepts exactly one immutable typed operation:

- BatchId, CanonicalizationVersion1, ExpectedTaskLineId, ExpectedWorkflowVersion2,
  ExpectedEnvironmentId, ExpectedDraftVersion and optional SourceProof.
- OperationId, CanonicalizationVersion1, Kind (`BindSampleEvidence`,
  `CorrectSampleEvidence`, `WithdrawSampleEvidence`), BindingId, TaskItemId,
  InspectionAttemptId, SampleOperation `{operationId,contentHash}`,
  PhotoReference of the same reference shape (null only for withdrawal),
  SourceWorkContextId, ContributorUserIdAssertion, BaseDraftVersion,
  OriginLocalEditVersion and exact optional Predecessor reference.

Unknown/duplicate JSON fields, strings in numeric positions, bad hashes/IDs, excess
body size and production rate limits remain rejected. No classification, coverage or
Submit field is accepted. CanonicalV1 uses fixed domain-separated JSON, lower GUIDs,
upper hashes and explicit nulls. Task/scope/root/kind/target/photo/source/contributor/
base/local/predecessor are hashed. Proof secrets/current retry actor credentials are
excluded. Batch fingerprint separately includes batch/expected version/operation hash.

Known exact batch/operation returns the immutable original receipt before fresh
source, CAS, eligibility or physical checks; current reader authority is still
required. A different batch can recover an exact operation without another batch
receipt or version increase. Changed payload/source/scope under an accepted ID is a
conflict. Unknown unsupported canonical version is400; known identity/version change
is409. The native universal AcceptedOperationIdentities PK excludes accepted IDs
across sample, Removed, photo reference, Unable and binding domains.

Common batch ReceiptVersion5 adds optional `sampleEvidenceOperations`; historical
JSON and old optional fields are untouched. Binding receipts embed original target,
predecessor and photo-reference receipts, own source/contributor/actor/family,
accepted UTC/version and binding predecessor. Every receipt explicitly reports
AssociationFactOnly=true, ProvesNewEvidence/ProvesCoverage/ProvesPhysicalCaptureTime=false.

GET `/operation-batches/{batchId}/receipt` and `/operations/{operationId}/receipt`
recover durable exact original facts. An early404 is not proof of rejection; a late
commit/lost response is reconciled with the same IDs, never a fresh fabricated fact.

GET `/shared-draft/evidence-bindings` returns latest binding observations and immutable
history separately. page>=1, pageSize1..100/default50; history<=100 complete accepted
versions, optional positive exclusive beforeDraftVersion and next cursor. Each batch
contains one operation. Pagination does not change history or current projections.
Current attempt/sample-value/photo eligibility flags are observations, not rewrites of
the last accepted receipt. Responses remain no-store/canExecute=false/
acceptsFormalInspection=false.

## Schema, locking, rollback and source security

Additive migration `20261113211600_R3205SampleTransitionEvidenceBindings` creates only:

- SampleEvidenceOperations: immutable `(TaskId,OperationId)` ledger, fixed root,
  actual SampleId/operation/hash/derived transition, optional exact photo-reference/
  attachment/hash, own provenance/actor, canonical payload/hash, receipt, accepted
  time/version, exact predecessor. Restrictive FKs reuse existing old-ledger keys.
- SampleEvidenceProjections: `(TaskId,BindingId)` current LastOperationId/IsActive,
  with a composite FK proving the pointer belongs to that same binding root.

Native SQLite/PostgreSQL checks/triggers validate current real scope, target judgment
and dependency, exact current active predecessor, current accepted reference/Ready
metadata, immutable ledger, forward-only projection and universal operation identity.
New PostgreSQL types are uuid/integer/bigint/boolean/timestamptz/varchar/text with
short constraint/index identifiers. Model/snapshot remain provider-neutral. No old
table rebuild, old history backfill, applied migration edit or production migration.
Downgrade explicitly refuses destruction of accepted history.

One transaction contains shared DraftVersion CAS, batch receipt, binding ledger,
native exclusion key, current projection and acceptance audit. Rollback at every
write/commit stage preserves all earlier data and bytes. Same-task sample, Removed,
reference, Unable and binding writes all contend on the same CAS. Binding does not
block a legitimate later sample write waiting for evidence to exist.

SQLite uses the existing nondeferred writer transaction. PostgreSQL uses existing
ReadCommitted account-first sorted locks, fresh family validation, configured-line
locks, Task lock, sorted source contexts, then attachment availability lock for a
fresh photo validation. Existing bounded transient retries and PR17 account-security
publication fence are unchanged. No new planning lock is taken after the Task lock.
Two-order races use real `pg_blocking_pids` paths; sleep alone is not proof.

Fresh binding source proof/owner/security eligibility is independently checked.
Security revoke/reset/role removal/deactivation that wins first prevents acceptance;
accepted-before-revoke receipts remain original facts. Accepted sample/photo sources
are not retrospectively reauthenticated. A new eligible binding source may refer to
them after original-source security revocation. Normal original-A logout, expiry,
self-password change or refresh does not security-revoke that historical source;
the current binding actor must still have current online authority.

Physical verification is an observation under DB locks, not a joint database/filesystem
transaction. External later loss is reported without changing accepted history.
F3 deletion/claims, production storage recovery and physical authenticity are deferred.

## Assessment integration and self-review fix

AssessmentVersion2 adds bounded accepted binding observations and BindingFactsComplete.
At most100 active roots per item are observed; overflow is explicit and cannot prove
complete association. A photo's BindsSampleTransition=true means a real active accepted
association to that exact currently eligible reference, not coverage/newness. Existing
sample-history/minimum limits and raw policy remain unchanged.

P3 self-review correction: unavailable/unsupported current attempt scope had returned
BindingFactsComplete=true without observing bindings. A genuine accepted binding then
missing-current-pointer regression failed before correction. Completeness now starts
false and becomes true only after the supported current-scope bounded query succeeds;
the immutable prior history remains readable. SQLite and PostgreSQL run this negative.

When all relevant draft corrections have eligible accepted associations, the reason
can become NewEvidenceOrCoverageSemanticsUnresolved. EvidenceStatus still remains
UnsupportedUntilFutureWorkflow; NewEvidenceForEachRelevantTransition and
PhysicalReinspectionOrImprovementEvent are unproven. Missing associations still report
PhotoToInspectionTransitionAssociationNotRecorded. Binding alone never makes a
correction Satisfied. No policy-configuration write/confirmation or formal event exists.

Developer self-review found and fixed a P2 read-amplification issue: multiple bindings
could repeatedly read/hash/decode the same bytes within one assessment/history read.
Request-local availability observations are now shared by binding observations and
the assessor's existing photo observations. No cross-request caching or fresh-write
validation reuse occurs. Regression with101 genuine accepted bindings verifies one
physical read for100 associations, and verifies the next request detects deleted bytes.
Test construction also now selects exact operation IDs rather than assuming an
accepted-reference batch contains one receipt, and creates long histories through the
real authorized transactional service without weakening HTTP production rate limits.

## Required acceptance and negative-test inventory

These are executable coverage references, not claims that deferred workflows ran.
Final PASS/FAIL/SKIP results/counts and real PostgreSQL execution evidence are in the
exact final-HEAD PR delivery. Backend opt-in PostgreSQL SKIPs are not executions.

| Case | Actual coverage |
| --- | --- |
| 01 accepted sample operation | Lifecycle, Scope, Unsupported |
| 02 exact dependency/classification | Transition NG/OK, NG/NG, OK/NG; WithinBatch; native Integrity |
| 03 genuine current photo reference | Photo, Lifecycle |
| 04 wrong task/item/attempt/sample operation | Scope, Amendments, Unavailable completeness, native Integrity |
| 05 Ready unassociated/invalid/detached | Photo pending/unassociated/unaccepted/missing/corrupt/unavailable/detached/superseded |
| 06 independent A/B/binding actor | Lifecycle, Source |
| 07 source security both orders | Security explicit/reset/role/deactivate, both serialization orders |
| 08 normal A lifecycle | Source logout/expiry/self-password/refresh |
| 09 shared all-domain CAS | Cas binding/sample/Removed/photo/Unable first |
| 10 replay/changed payload/identity/receipts | Lifecycle, Identities, native Integrity |
| 11 lost response/late commit/recovery | Lost |
| 12 immutable amendment/withdrawal | Lifecycle, Amendments, Later, native Integrity |
| 13 rollback including audit/commit | RollbackFresh six points, RollbackWithdraw three points |
| 14 sample change/photo detach races | LaterWrite both orders; Cas Removed/Unable |
| 15 no false Satisfied | Transition, WithinBatch, existing assessor regressions |
| 16 five photos/multiple genuine samples | Capacity fifty samples/five objects/shared photo; no slot release |
| 17 unconfirmed policy unchanged | Existing DraftEvidenceAssessment policy cases and full regression suite |
| 18 native fresh/populated migration/integrity | Both-model DDL, PopulatedUpgrade, three MigrationRollback points, native Integrity |
| 19 existing security/draft/Removed/Unable/photo | Full backend and existing real PostgreSQL suites retained in CI |
| 20 no formal activation/canExecute=false | Lifecycle/Read/Accept flags and zero formal rows; existing Submit fences |

SampleEvidencePostgresTests executes the same applicable rule/failure/race/upgrade
scenarios on disposable real PostgreSQL17 through the appended CI OR filter; existing
PG classes remain included. Model-only script checks also compare both provider models.

Formal NG improvement/reinspection/PQE history, per-transition newness/coverage success,
confirmed normal-OK policy, final submission completeness, F2/F3, Android, UI, PKI,
deployment and production migration/recovery are NOT RUN and NOT implemented here.
No synthetic historical sample/attempt, repair/backfill or acceptance fact substitutes
for those absent producers. All shared tasks remain canExecute=false.
