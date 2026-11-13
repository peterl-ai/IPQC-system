# R32-05 — Read-only normal NG and improvement evidence assessment

Date: 2026-11-13. Status: scoped implementation for independent ChatGPT Final Review,
not merge, formal Submit, deployment or next-phase authorization. Peter's attached
Normal NG/NG-to-OK Evidence Assessment implementation authorization applies.
[Approved Rev3.2 §§10–12](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md),
[Supersession](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md)
and approved requirements in the
[identity/acceptance reference](architecture/IPQC_SHARED_INSPECTION_IDENTITY_ACCEPTANCE_CONTRACT_V1_2026-11-12.md)
remain authoritative. Proposed unrelated technical decisions are not promoted to approval.

## Verified baseline and Peter's A/A decisions

PR #23 merged reviewed HEAD `764dc025ab381800626492a9b796f65988709138` as actual
merge/main `1effb72e1d9e66ceda6689ddb080e191222d512c`, with identical trees.
No open PR existed. Merged-main [CI43497301499](https://github.com/peterl-ai/IPQC-system/actions/runs/43497301499)
completed SUCCESS before implementation: Release0 warnings/errors; full backend753
PASS/350 opt-in PostgreSQL SKIP/0 FAIL; actual PostgreSQL17.12 350 PASS/0 SKIP;
Web90 PASS/18 files plus lint/typecheck/build. These are base results, not this PR's
final HEAD evidence. Exact final Push/PR runs/results belong in the PR delivery.

Peter explicitly selected both conservative read-only alternatives:

1. Retain actual accepted draft corrections/dependency/order facts. If individual
   NG/improvement/repeated-NG photo association cannot be proven, report
   UnsupportedUntilFutureWorkflow. Explicit association needs a later scoped contract;
   shared version ordering alone cannot invent it.
2. Retain frozen PhotoRequirement free text. Required/Optional words without an
   independent explicit confirmation record remain PolicyConfigurationUnresolved.
   Do not reinterpret legacy text or introduce a configuration write in this PR.

The existing Standard ItemInput/PatrolStandardItem/PatrolTaskItem and PlanningDefinitionV1
store only raw PhotoRequirement. Current immutable photo references bind Task/Item/
Attempt, never a SampleId or relevant inspection-transition OperationId. These are
actual code facts, not missing client-selected timestamps that this evaluator can guess.

## Read API and bounded observation

GET `/api/line-patrol/tasks/{taskId}/shared-draft/evidence-assessment`

Query: page>=1, pageSize1..20 (default20), optional expectedDraftVersion>=0. Invalid
query400; stale pinned version409 EvidenceAssessmentVersionConflict. Current eligible
IPQA/current personal family and actual active configured Task/Line/workflow scope
remain mandatory, including direct service calls. Admin/PQE is not a bypass. Legacy or
missing/out-of-scope Task404, authority401/403, existing configuration/storage errors
remain safe failures. Auth precedes binding, body/physical work and photo limits.

The response carries actual Task/Line/workflow, current DraftVersion, page/size/total,
AssessmentVersion1 and per-item:

- TaskItem/current real Attempt, frozen inspection type/minimum, observed active sample
  count and MeetsMinimum; unknown counts are null, not fabricated zero/false.
- Raw/recognized/unconfirmed frozen policy, disposition and accepted sample result state.
- Actual sample/value-operation receipts, tombstones, exact predecessor ID/hash and
  dependency depth, accepted draft transition classification/version; IsFormalEvent=false.
- Reserved photo identities, original source/contributor A, first Ready uploader B/family,
  first Ready server time, original latest reference receipt/actor/family, attached/current
  applicability, physical availability/integrity and actual evidence eligibility.
- Required evidence categories, EvidenceStatus, explicit Reasons and UnprovenFacts.

At most100 sample identities and100 accepted sample operations per item are assessed.
Exceeding a technical read bound returns UnsupportedUntilFutureWorkflow and null unknown
counts, retaining all server history; it does not cap writes, delete facts or assess a
truncated graph as complete. At most20 items and5 reserved current-attempt objects each
are read; bytes/decoders are used sequentially through existing bounded protected adapters.
Pagination sorts actual frozen item order. Optional version pinning prevents clients
silently combining pages from different accepted draft versions.

All responses remain no-store, canExecute=false, acceptsFormalInspection=false and
Completeness=NotEvaluatedForFormalSubmission. Evidence Satisfied is **only the stated
photo/disposition assessment**, not inspection completeness, measured-count success,
physical authorship, task completion, formal acceptance or Submit permission. For example,
one actual NG sample with genuine evidence can have EvidenceStatus=Satisfied while a
frozen minimum5 remains unmet; both facts are explicitly returned. Partial all-OK without
the actual minimum does not become an established ordinary OK result.

## Evaluation rules and proof limits

| Situation | Evidence assessment |
| --- | --- |
| Genuine initial current-attempt NG captures, no unresolved relevant correction/history | Requires at least one genuinely accepted, currently attached and physically verified current-attempt reference; Satisfied only when present |
| No accepted reference, even with a Ready object | MissingEvidence; Ready alone is not business acceptance; unassociated integrity is explicitly NotCheckedUnassociated |
| Only detached/missing/corrupt/unavailable accepted references | InvalidEvidence, not Satisfied; original accepted history retained |
| Another actual Task/Item/Attempt photo | Cannot substitute; candidate selection and scope checks bind actual current identities |
| Current normal all-OK, actual minimum met | PolicyConfigurationUnresolved until a real explicit policy contract/confirmation exists, including literal Required/Optional |
| Empty or partial all-OK draft | UnsupportedUntilFutureWorkflow for an unestablished applicable result; no fabricated judgment/photo exemption |
| Accepted NG→OK correction | Actual dependency proves the draft value change; improvement-photo association is UnsupportedUntilFutureWorkflow |
| Accepted NG→NG correction | Observed repeated NG draft values, not proof of a physical repeat/formal reinspection; per-event new-evidence association unsupported |
| Accepted OK→NG correction | Known new NG value change cannot reuse an arbitrary reference as transition-specific evidence; unsupported association |
| Removed former NG sample, later captures, or withdrawn Unable | No implicit improvement/reset of NG history; unresolved transition/evidence association remains unsupported |
| Whole-item Removed | No independently required photo; Satisfied for NoneForRemoved without creating OK/NotPerformed |
| Active UnableToMeasureNG | Existing dedicated mandatory NG Reason and exact current accepted reference/physical observation; invalid/lost evidence remains invalid |
| Missing/stale identity, unsupported frozen snapshot/formal state or bounded history overflow | Explicit unsupported result; never invent history or a previous attempt |

Multiple genuine samples are assessed individually. Qualitative values are OK/NG without
numeric values; quantitative values/judgments are checked against actual frozen limits.
No fixed two-sample assumption, fake zero, N/A, synthetic attempt, placeholder submission
or fabricated measurement timestamp. Different NG samples in one initial inspection are
not silently relabeled a formal repeated-NG event.

ContributionDependencies plus immutable receipts prove accepted causal predecessor
relations, including multiple corrections in one atomic batch with equal DraftVersion.
GUID sorting, receipt timestamps or list order cannot replace that graph. Current sample
projections must agree with the retained actual value receipts. Sample removal does not
erase earlier NG history. The original sample/photo/batch JSON and A/B facts are untouched.

ReferenceAcceptedAfterOperationIds records only strict larger shared DraftVersion in the
same Task. It does not bind a photo to a transition: BindsSampleTransition remains false.
A delayed reference to already Ready bytes still cannot prove those bytes document the
particular improvement/repeated NG. First ReadyAtUtc is a server storage fact, not a
trusted capture/measurement timestamp or durable cross-domain causal link. No client
clock/EXIF is used to establish evidence ordering or authenticity.

Real previous submitted-attempt production, old identical submitted-image comparison,
formal NG/PQE events and inheritance are not implemented here. A stale current pointer
or a genuine other item's attempt is a negative scope test, not that missing producer.
Those future integration cases are explicitly NOT RUN; unknown facts never become PASS.

## Security, consistency and zero GET side effects

Read uses existing WorkContextStore transactions: SQLite non-deferred writer reservation;
PostgreSQL current-account-first lock, fresh current-family authority validation, sorted
configured lines, then Task lock. It shares ordering with reference/sample/disposition
writers and account/family lifecycle. No new account-security hook, stale issuance snapshot,
planning gate or permission is introduced. Version-pinned reads either see the whole
accepted version or report conflict; they never perform CAS or advance DraftVersion.

Accepted source A provenance survives normal logout/refresh/expiry/self-password changes
and later sticky revoke/reset/role loss/deactivation. Current B authority is independently
required. Sticky source security still blocks **new unaccepted** work through unchanged
write APIs. Original photo source A, first accepting uploader B and reference actor stay
separate. WorkContext is provenance, not authentication or physical authorship.

Actual bytes are read/hash/decoder checked via existing ReferenceAvailability, not the
photo service's mutating availability/audit probe. No GET inserts/updates/deletes audit,
availability, samples, ledgers, attempts, receipts, sources, task states or versions.
Photo failure can be observed while stored Ready remains a historical fact. No file
upload/write/promotion/delete or response path/storage key/proof/token leakage occurs.
The existing20 requests/min/user,4 request slots,2 decoder slots,5MiB image bound and
cooperative2-minute deadline apply. No binary response is embedded in assessment JSON.

DB and filesystem are not jointly atomic. Task locking provides a consistent accepted
DB snapshot against current writers, not protection from later privileged filesystem
tampering/hardware loss. Future Submit must revalidate physical availability and all
business completeness again. Existing restoration/volume/ACL/backup/QA gates remain.

## Validation map and developer self-review

Paths below are under `api/JaxPower.Ipqc.Api.Tests/`. Final exact counts and Push/PR logs
are recorded in PR delivery. DraftEvidenceAssessmentPostgresTests explicitly selects
all new real-provider cases; no prior PostgreSQL/security filter is removed. Opt-in
PostgreSQL skips in a local/full non-PG job are not live provider evidence.

| Requested case | Executable evidence / boundary |
| --- | --- |
| 01 | Genuine and Availability: real NG requires evidence |
| 02 | Genuine: actual current-attempt accepted reference and verified protected bytes |
| 03 | Availability ready-unassociated/pending: no accepting reference inferred |
| 04 | Scope: another genuinely generated Task and actual other Item/Attempt cannot substitute |
| 05 | Availability detached/missing/corrupt/unavailable; unchanged DB facts |
| 06 | Current-scope/stale-pointer negatives PASS; genuine previous formal/targeted attempt producer NOT RUN |
| 07 | Transition NG→OK and WithinBatch exact DAG classification PASS; per-transition association explicitly unsupported |
| 08 | Transition NG→NG classification PASS; physical/formal repeated inspection NOT RUN |
| 09 | OK→NG/RemovedSample/LongHistory adversarial regressions prevent false success or invented counts |
| 10 | Policy retains frozen raw Required/Optional and ignores today's mutable Standard; confirmed-policy producer/integration NOT RUN under Peter A |
| 11 | Policy unknown/empty/lowercase cannot become Optional |
| 12 | Dispositions active Unable, explicit clear, ordinary result and whole-item Removed remain distinct |
| 13 | Multiple real qualitative/quantitative samples, actual frozen minimum3/5, no numeric placeholder |
| 14 | Facts fingerprints old rows/schema/receipt JSON plus new Unable/identity rows before/after GET |
| 15 | Authority HTTP/direct/current-family/role/scope and logout both orders |
| 16 | Bounds empty GET/version pinning and Reference concurrency both orders; no new root/CAS/audit |
| 17 | SQLite cases and actual PostgreSQL17 twins, real lock-manager blocking path |
| 18 | Full retained draft/Removed/Unable/photo/reference/source-security suite in final CI |
| 19 | Read output flags and immutable factual fingerprints; no formal Submit/review/status writes |
| 20 | Formal previous/reinspection/PQE events, explicit photo-transition binding and confirmed-policy end-to-end cases NOT RUN |

Confirmed P2: initial ordinary-NG branch ignored OK→NG corrections and former NG sample
tombstones, falsely accepting any eligible current reference for those unresolved episodes.
Both negative tests returned Satisfied before fix. The evaluator now retains the relevant
correction/tombstone history and reports unsupported association; no old facts are changed.
Confirmed P3: bounded-history failure returned count0/MeetsMinimum=false even though current
counts were not assessed. LongHistory regressed this to require null unknowns. Corrected
five-case focused negative run initially had3 FAIL/2 PASS before the fixes. Both providers
execute the retained regression cases after correction.

Initial test-fixture errors (wrong synthetic PQE username and assuming a correction result
contained only its fresh receipt, despite explicit accepted predecessor receipts) were
corrected separately; they are not claimed product defects. No failing CI is rerun merely
to seek green. Actual full diff, source attribution, DAG/order/unknown-policy semantics,
history bounds, authorization, native-provider concurrency and false-positive assertions
are reviewed independently of this document's claims.

## Scope and deferred phases

2026-11-13 follow-up: [explicit sample-transition bindings](26_R3205_SAMPLE_EVIDENCE_BINDINGS.md)
adds a separate accepted-association API and AssessmentVersion2 factual observations.
The Version1 implementation and evidence above remain historical. Peter approved
fact-only photo reuse and immutable correction/withdrawal, preserving the five-photo
cap. Even complete associations do not establish newness/coverage or formal events;
corrections remain UnsupportedUntilFutureWorkflow and raw photo policy remains unresolved.
Confirmed policy, formal Submit and other deferred workflows below remain unimplemented.

No schema/model/migration or accepted-write contract change. Existing sample, Removed,
Unable and reference APIs remain writable with original guards/CAS/receipts. D01(A),
Admin-only controlled initialization and all established minima/history remain unchanged.
This read-only slice provides observations for later validation, not formal Submit.

Deferred: explicit confirmed Required/Optional configuration, per-transition evidence
association, formal Submit/status/PQE/NotPerformed, targeted reinspection producers,
inherited formal counts, old submitted identical-byte validation, full F2 takeover/intent,
F3 retention/deletion/restore workers, reference-image administration, Android/offline
and Web execution UI, MES/material verification, device PKI, rollout/production operations.
No unresolved material decision remains for this A/A scope. Stop after one PR and verified
CI for independent ChatGPT Final Review and Peter's merge decision.
