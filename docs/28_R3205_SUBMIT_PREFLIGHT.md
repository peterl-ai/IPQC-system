# R32-05 — Fail-closed formal Submit preflight foundation

Date: 2026-11-16. Scoped backend-only, read-only implementation for independent review.
Peter's explicit preflight authorization applies. No merge, production migration,
deployment, actual Submit or subsequent implementation is authorized.

## Requirements inventory — before implementation

Approved [Rev3.2 §§10–12](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md)
and [Supersession](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md)
remain authoritative. The [identity/acceptance reference](architecture/IPQC_SHARED_INSPECTION_IDENTITY_ACCEPTANCE_CONTRACT_V1_2026-11-12.md)
distinguishes approved invariants from historical proposals. D01(A), Admin-only controlled
initialization, PQE-only exact Standard-item policy confirmation and association-only
photo reuse/correction decisions remain unchanged. WorkContext preflight is not acceptance.

Verified PR #27 reviewed HEAD `f47babbff5b0a8fe94902de47bcc90fa89b64e57` merged as
`3084042aee858d9037e29f6597a47baa858ec242`, also latest main, with the same reviewed tree.
No other open PR existed. Merged-main CI43914751273 was running during this inventory;
its completed evidence and this PR's exact-HEAD evidence must be verified separately.
Latest migration remains `20261116145540_R3205ConfirmedPhotoPolicies`; no new durable
model is needed. Existing rows, receipts, policies, migrations and files are preserved.

| Requirement | Actual persisted/read source | Classification and preflight treatment |
|---|---|---|
| Frozen Task/line/definition | PlanningDefinitionV1, actual Task/items; PlanningSnapshot.Read | Provable coherence; corrupt snapshot Invalid, historical/unsupported scope never repaired |
| Current initial Attempt | InitialInspectionIdentity.State and real applicability/attempt FK | Provable only for genuine complete initial sets; missing/stale/unsupported identity remains blocked |
| Frozen measurement definition | Existing non-mutating ValidateDefinition | Type/count/numeric-limit validity; do not invoke legacy Calculate, which mutates legacy answers/N/A |
| Complete current sample history | Existing AssessItem/BuildEvidenceHistory, real ledger/dependencies/projections | Provable within existing100-identity/operation bounds; incomplete/over-bound history Unsupported, unknown counts null |
| Minimum and actual count | Actual attempt's frozen minimum and accepted nonremoved samples | Ordinary measured item Missing below minimum; Removed/Unable do not fabricate samples or lower the retained minimum |
| Qualitative / quantitative judgment | Existing evidence assessor and frozen numeric judgment helpers | Observe actual OK/NG/decimal values; valid NG may still be a legitimate draft result; no formal result created |
| Ordinary all-OK Optional | Valid immutable FrozenTaskPhotoPolicy | Individual photo requirement may be Satisfied without a photo; never proves formal submission eligibility |
| Ordinary all-OK Required | Same frozen policy plus accepted active reference and verified bytes | Missing/unassociated Missing; detached/missing/corrupt/unavailable Invalid; policy unknown never becomes Optional |
| Initial ordinary NG | Existing evidence assessment's genuine current-attempt reference/bytes | Photo evidence can be individually Satisfied while sample minimum is separately Missing |
| NG→OK / NG→NG / OK→NG and unresolved NG history | Real accepted dependency transitions, factual bindings | Association may be proven; newness/coverage/physical event proof remains Unsupported |
| Removed | Genuine ItemRemoval operation for current real item/attempt | Distinct disposition; no OK/N/A, no invented measurement; preserve audit/history/minimum |
| All Removed | Global current item/attempt removal membership, never an empty page/zero-item task | Explicit future NotPerformed submission and exact PQE special confirmation required; neither implemented |
| UnableToMeasureNG | Genuine accepted disposition receipt, mandatory reason, exact accepted current reference and bytes | Reason/evidence observed independently; no sample or lowered minimum; lost bytes remain invalid |
| Attachment Ready versus reference | Existing EvidencePhotos/ReferenceAvailability | Ready alone is not business evidence; original A/B provenance is retained |
| Physical availability | Existing protected read/hash/decoder observation, no mutating photo status probe | Point-in-time only; no byte writes, deletion, promotion, cleanup, availability/audit refresh or future guarantee |
| Legacy/free-text policy | Frozen original text without explicit confirmation | Unresolved for ordinary OK; no historical conversion or borrowed current Standard confirmation |
| PQE/reinspection/formal history | No implemented shared formal event producer or new-attempt lifecycle | Unsupported/deferred; never fabricate SubmissionId, inspection attempt or prior measured history |
| Formal Submit as a whole | No actual shared formal acceptance/F2/F3 integration in this slice | Always Blocked, canSubmit=false, canExecute=false, formalSubmitPerformed=false |

There is no new material business choice required to implement this observation-only
slice. A complete future positive decision requires the missing approved workflows/proof.
For a later authorization Peter can choose A: implement/review the real formal identity,
new-evidence/coverage and special-path prerequisites first (recommended), or B: retain
this blocked observer and defer positive Submit. Neither option reinterprets the existing
A/A decisions or authorizes weakening evidence, increasing photo capacity or another PR.

## API and bounded result contract

`GET /api/line-patrol/tasks/{taskId}/shared-draft/submit-preflight`

Optional query: `page` (default1), `pageSize` (default20, maximum20),
`expectedDraftVersion` (nonnegative), `expectedTaskLineId`, and the paired
`expectedTaskItemId` / `expectedInspectionAttemptId`. The item/attempt pair asserts
actual current membership; it does not filter the observation or grant access.
Invalid bounds/empty GUIDs/unpaired assertions return400 `InvalidSubmitPreflightQuery`;
malformed bound types return400. A pinned version mismatch returns409
`SubmitPreflightVersionConflict`; real foreign item404 `NotFound`; stale/foreign attempt
409 `SubmitPreflightAttemptConflict`; wrong asserted line409 `TaskLineMismatch`.
Missing, legacy or outside-configured-line task returns404; unavailable configured lines
use the existing visible configuration error. No invented selected-line permission is added:
authorized IPQA can read actual tasks on either configured line, as Rev3.2 requires.

Current authenticated personal family and database IPQA role are required at HTTP and
service entry. Anonymous/missing/closed family401; wrong current role/forced password
change403. A proof, old contributor session or asserted role is never authentication.
Responses use `Cache-Control: no-store`, the existing bounded photo-read admission policy,
and expose no proof, token, private path or storage key. Actual accepted A provenance remains
observable to current B after A logout, self-password change or security revocation.

`SubmitPreflightOutput` embeds the unchanged `DraftEvidenceItem` for each observed item;
actual IDs, frozen minimum, observed nullable count, original policy text/confirmation,
disposition, real sample/transition receipts, accepted binding facts and byte availability
are preserved. The implementation shares the exact existing assessor within one store
transaction; it does not call it through a second transaction or recalculate judgments.
Existing bounds remain: maximum20 items/page,100 sample identities/operations/dependencies,
100 active bindings/item,5 photo objects/current item/attempt. Truncated/invalid history
has unknown counts and incomplete observation; truncated bindings remain explicitly
incomplete. There is no new draft-write restriction or schema/migration.

Each requirement has stable `code`, typed `state` (`Satisfied`, `Missing`, `Invalid`,
`Unsupported`, `NotApplicable`), and stable `reasons`. NotApplicable explains a scoped
exemption; it does not rewrite an actual disposition as legacy N/A or change minimums.

| Requirement code | Blocking reason or interpretation |
|---|---|
| FrozenPlanningSnapshot | FrozenPlanningSnapshotUnavailable |
| CurrentInitialAttempts | CurrentInitialAttemptUnavailable |
| CurrentDraftScope | CurrentDraftScopeUnsupported |
| CompleteTaskObservation | TaskObservationIncomplete |
| FormalSubmitAcceptance | FormalSubmitWorkflowNotImplemented — always Unsupported |
| NotPerformedSubmission | AllRemovedRequiresNotPerformedSubmission — only actual global all-Removed |
| PqeNotPerformedConfirmation | PqeNotPerformedConfirmationNotImplemented — same special path |
| FrozenMeasurementDefinition | FrozenMeasurementDefinitionInvalid |
| CurrentSampleHistory | CurrentSampleHistoryUnavailable |
| CurrentDisposition | CurrentDispositionUnavailable |
| FrozenMinimumSamples | FrozenMinimumNotMet / SampleMinimumObservationUnavailable; Removed/Unable retain the actual minimum but have no measured-sample requirement |
| CurrentSampleJudgments | CurrentSampleJudgmentsNotEstablished; real qualitative/quantitative judgments reused |
| FrozenOKPhotoPolicy | FrozenPhotoPolicyUnconfirmedOrInvalid; distinct ordinary-NG and special dispositions do not borrow an OK waiver |
| CurrentPhotoEvidence | Existing assessor's exact reasons/unproven-fact codes, or CurrentEvidenceUnsupported; Removed has no independent photo requirement |
| AcceptedBindingObservation | BindingObservationUnavailable; satisfied means observed association facts only |
| TransitionPhotoNewnessAndCoverage | TransitionPhotoNewnessAndCoverageUnsupported / TransitionHistoryUnavailable; unknown history is never NotApplicable; genuine bindings prove neither newness nor coverage |
| UnableToMeasureReason | UnableReasonMissing / UnableDispositionUnavailable; mandatory only for active Unable, unknown disposition stays Unsupported |

`blockingReasons` is the union of Missing/Invalid/Unsupported requirement reasons on
this page plus task-level blockers. It does not summarize unobserved pages.
`pageObservationComplete` means all items in a nonempty page have supported scope, valid
frozen definitions and complete bounded sample/binding observations. Missing requirements
can be completely observed; unsupported physical business proof remains explicitly
unsupported even when persisted facts are completely observed. `taskObservationComplete`
also requires page1 containing every actual task item. Neither flag is Submit eligibility.

`allItemsRemoved` is global actual current item/attempt membership, independent of page;
it is null for unsupported scopes, false for a valid non-all-Removed task, never inferred
from an empty page or a corrupt empty frozen scope. No normal sample, measured result,
formal NotPerformed event or PQE confirmation is manufactured.

Every response retains `outcome=Blocked`, `canSubmit=false`, `canExecute=false`,
`formalSubmitPerformed=false`, `acceptsFormalInspection=false`, and
`completeness=NotEvaluatedForFormalSubmission`. These observations are neither accepted
inspection work nor a reusable future authorization/ready token.

## Atomicity, read-only and physical proof limits

Reuse `WorkContextStore`: SQLite nondeferred transaction before authority/read; PostgreSQL
ReadCommitted current-account lock, sorted configured-line share locks and actual Task
row update lock before all dependent reads. Existing sample/reference/disposition writers
use that same Task serialization. No planning gate is acquired after the Task lock.
The service also rereads the authoritative DraftVersion before returning; drift produces
409 rather than a mixed-version success. A subsequent write can immediately stale an
observation; consumers may pin `expectedDraftVersion`, but future Submit must revalidate.

Frozen policy rows and immutable confirmation receipts are read as frozen facts; current
Standard confirmations are never refreshed into an existing Task. No entity is modified,
no audit/receipt/availability row or draft root is created, and no file is written, moved,
promoted or deleted by GET. The shared store's normal SaveChanges/commit has no staged
mutations. Storage tests use owned disposable fixture files and actual PNG bytes.

Physical integrity is observed with the existing protected read/hash/full decoder, memoized
only inside one request. `physicalEvidenceObservation=ObservedDuringReadNotReservedForSubmit`
is explicit: database locks do not reserve external bytes. Later loss/tampering may change
the next result. Neither upload order nor server acceptance order/client time/EXIF proves
physical capture time, a new improvement event or coverage. Future formal acceptance/F3
must implement its own coordinated revalidation and durable reference contract.

## Validation and review evidence

Merged-main run [43914751273](https://github.com/peterl-ai/IPQC-system/actions/runs/43914751273)
completed successfully on `3084042aee858d9037e29f6597a47baa858ec242`: backend921PASS,
514 opt-in PostgreSQL SKIP,0FAIL; real PostgreSQL17.12 job514PASS,0SKIP,0FAIL;
Web127PASS/20files with lint/typecheck/build successful; both Release builds0warnings/errors.
The opt-in backend SKIPs are not executed PostgreSQL evidence.

The new `SubmitPreflightTests`, `SubmitPreflightSecurityTests` and
`SubmitPreflightConcurrencyTests` cover the inventory above with real generated tasks,
real accepted operations and byte-backed evidence. `SubmitPreflightPostgresTests` provides
explicit real-provider twins; CI appends that class without dropping existing PostgreSQL
regressions. Concurrency cases run sample writes, detach, Removed and Unable in both
orders, using different authenticated users; PostgreSQL asserts an actual transitive
`pg_blocking_pids` wait before releasing the holder. Both frozen-policy read orders and
current-family logout orders are tested too. GET fingerprints retain full database facts;
owned photo bytes and modification times are also checked. Logout tests snapshot after
the authorized logout so its real session update is not falsely blamed on GET.

Adversarial self-review found and fixed P2: unavailable/over-bound sample history could
mark transition-specific proof NotApplicable despite lacking the facts needed to establish
that exemption. Unknown history now explicitly reports Unsupported/TransitionHistoryUnavailable;
an unavailable disposition similarly cannot claim the Unable reason is inapplicable.
Negative regression assertions cover all three unsupported frozen/attempt cases plus
101 genuine sample operations on both providers. Whole-task Submit was always blocked.
Test-oracle corrections also removed a misleading empty-scope label and separated the
authorized logout's session write from GET mutation checks. No approved rule was changed.

Final exact-HEAD Push/PR run URLs, decoded real counts and self-review findings belong
in this PR's delivery evidence. No proposed future Submit/PQE/reinspection tests are claimed
executed here. This document and implementation are for independent review, not approval
to merge, migrate or deploy.

## Explicitly deferred

Formal Submit request/frozen manifest/idempotent receipt, F2 intent barriers/takeover,
exact-revision PQE approval/rejection and NotPerformed special confirmation, real
reinspection attempt producers, transition-photo newness/coverage proof, F3 retention
and reference/deletion lifecycle, Android and any enabled Web execution/Submit action.
No business decision is delegated to this observer, and no next PR begins automatically.
