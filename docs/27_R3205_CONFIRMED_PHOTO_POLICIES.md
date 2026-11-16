# R32-05 — Explicit confirmed photo policies and frozen Task contract

## 2026-11-16 Web configuration follow-up

Peter separately authorized the PQE photo-policy Web UI after merging PR #26.
Actual reviewed HEAD `0ad625cbafd8b80753ec1619e390ffebcb3b3a4b` merged as
`73073d0bb89aadc5b6a9e15e9832922c2c1fe020`, also the verified latest main.
The merge has the identical reviewed tree. Merged-main
[CI43904931203](https://github.com/peterl-ai/IPQC-system/actions/runs/43904931203)
completed successfully: backend920 PASS/513 opt-in PG SKIP/0 FAIL; actual
PostgreSQL17.12 513 PASS/0 SKIP/0 FAIL; Web90 PASS/18 files, lint/typecheck/build;
Release0 warnings/errors. Final UI-PR HEAD evidence is recorded separately in its PR.

The existing Standards row now opens a per-item photo-policy drawer. It shows real
Standard/item IDs, original raw text, current applicability, confirmed value, definition
revision/hash, immutable confirmation ID/version, actual PQE user ID and plant-time UTC
conversion. The exact source header and all item fields are available for review.
PQE must explicitly choose Required/Optional, attest review and click Confirm. Nothing
is preselected from raw text or an old confirmation. Admin can view and retain normal
Standard editing but receives no Confirm control. IPQA receives no configuration access.
English and Simplified Chinese use existing i18n. No bulk Confirm All, Plan override,
task generation, readiness success or execution control is added.

A narrow additive **read projection**, `PhotoPolicyPage.ReviewStandard`, returns the
existing StandardOutput from the **same locked planning transaction** as policy hashes
and preconditions. Independent Standard and policy GETs could otherwise mix review text
from one definition with another hash. No confirmation authority, mutation input,
database schema, migration, security hook or frozen Task contract changes. A new actual
API race test verifies both sides of the source read on SQLite and real PostgreSQL,
including observed PostgreSQL planning-gate blocking and the later invalidated state.

The existing API client sends exactly RequestId, CanonicalizationVersion1, nullable
ExpectedDefinitionId, ExpectedDefinitionHash, ExpectedConfirmationId and Value. The
RequestId is client idempotency only; confirmation IDs/versions/actors/families/times
come from server receipts. Same-origin authentication, bounded one-refresh behavior and
explicit no-store are reused. A different intended confirmation creates a new RequestId.

Before POST, only the non-secret exact intent (current actor ID, Standard/item IDs and
six input fields) is retained in sessionStorage, scoped to that actor and Standard.
No token, password, proof, credential or authoritative client policy copy is stored.
Storage failure prevents POST. Reload/remount restores the same request without sending
it. Unknown network/5xx/response outcomes retain it for **explicit exact retry**. Refresh
can discover its matching current RequestId and GET the exact immutable receipt; a
superseding current confirmation is not proof of this older request's failure. Explicit
retry can recover the original older receipt without rewriting today's policy. Receipt
scope/value/hash/identity/actor must match before displaying accepted success.

SessionStorage survives reload in the same tab, not tab/browser-data destruction. This
slice adds no cross-tab coordinator, server RequestId query, cancellation or background
sender. Do not treat a missing local request or a GET404 as proof a prior in-flight
request never accepted. A deliberate new action still uses freshly read server
preconditions; current server authority and CAS remain final. Historical receipt and
current applicability are displayed separately.

400/401/403/404/409/429 and unavailable responses have bilingual explicit messages.
Definition/competing-confirmation409 retains the selected value, reloads actual source
and current decision, clears review acknowledgement and requires a new deliberate
confirmation/new RequestId. No automatic rebase or write occurs. Unknown originals are
retained across session denial for that same actor's later authorized recovery; another
user never sends the prior actor's request. Successful Standard editing refreshes the
policy view; no-op preserves server confirmation and meaningful edit requires another
confirmation. Standard Save itself never calls the confirmation endpoint.

Developer self-review confirmed **P2**: an older accepted success banner initially
remained visible when a second confirmation's response was unknown. A negative regression
failed before the fix; starting a genuinely new request now clears that previous success
indicator while retaining independently loaded current configuration. The full Web suite
then passed127 tests/20 files. New protocol/component coverage comprises37 cases, including
both languages, actor changes, exact recovery/replay, malformed scope, competing edits,
null initial IDs, all specified errors and Standard Save/no-op/invalidation integration.
The new SQLite API review-read test passed; its real PostgreSQL twin is in the existing
PhotoPolicyPostgresTests CI filter. Initial test-only regex/lint/type corrections are
not classified as application defects.

Actual Playwright browser checks used an isolated temporary SQLite database, synthetic
accounts/Standard and disabled scheduler: PQE reviewed and confirmed Required through
the real API; EN/ZH status/definitions were inspected; Admin retained Edit and had only
read-only policy status. Screenshots and disposable evidence remain outside the repo.
This is browser/HTTP evidence, not tablet UAT, production migration or deployment.
The original foundation delivery below remains historical evidence; its UI deferral is
superseded only by this authorized configuration slice. Formal workflows, photo newness/
coverage, F2/F3, execution UI and production operations remain deferred; canExecute=false.
Stop after one UI PR for independent ChatGPT Final Review and Peter's merge decision.

## Original foundation delivery

Date: 2026-11-16. Status: implementation for independent ChatGPT Final Review.
Peter's attached Photo Policy Confirmation implementation authorization and explicit
A/A business decisions apply. No merge, deployment or subsequent phase is authorized.

## Verified baseline and business decisions

PR [#25](https://github.com/peterl-ai/IPQC-system/pull/25) merged reviewed HEAD
`bd527c4b72a11d4ad4f939b462041db6e34ee840` as actual merge/latest main
`be00802037f868fd9be75f5b69b1a6649dac09dd`, with identical trees. No open PR existed.
Merged-main [CI43516661909](https://github.com/peterl-ai/IPQC-system/actions/runs/43516661909)
passed: Release0 warnings/errors; backend872 PASS/467 opt-in PG SKIP/0 FAIL;
actual PostgreSQL17.12 467 PASS/0 SKIP/0 FAIL; Web90 PASS/18 files plus
lint/typecheck/build. These are baseline results, not this PR's final-HEAD evidence.

Authoritative [Rev3.2 §11](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md)
requires explicit confirmation but did not assign confirmation authority/lifecycle.
Peter explicitly selected:

1. Only a currently eligible **PQE**, using a current online personal family, may
   confirm. Admin keeps existing Standard/Plan editing rights and receives no new
   confirmation permission. Saving/importing/copying a Standard is not confirmation.
2. Confirm at the **Standard item exact-definition revision**, reusable across Plans.
   Plans reference and never override the policy. Relevant definition changes require
   another explicit confirmation; new Tasks cannot be admitted without valid policies.

This is a separate photo-policy action, not a legacy Standard Submit/Approve/Upgrade
workflow, measurement-definition approval or four-eyes workflow. The original raw
PhotoRequirement is retained. Neither literal Required nor Optional implies consent.
Existing tasks are not initialized, converted or backfilled.

## Definition identity, history and applicability

Canonical definitionV1 includes actual StandardId/itemId, frozen Standard name,
factory code/name/workshop/material, item sequence and every frozen item definition
field (including raw PhotoRequirement, sample count, limits and inspection type).
Legacy Standard line labels and actor/update timestamps are excluded: actual Task line
belongs to Plan/ProductionLine, and timestamp/no-op edits are not definition identity.
Hashing uses fixed domain/version/field order and exact persisted strings.

Four new tables, with no old-table ALTER/rebuild/backfill:

- PhotoPolicyDefinitions: immutable server GUID, actual logical Standard/item IDs,
  monotonically increasing item definition version, canonical bytes/hash and creation UTC.
- PhotoPolicyConfirmations: immutable **server-generated ConfirmationId**, client
  RequestId/fingerprint, exact definition FK, per-item PolicyVersion, Required/Optional,
  prior confirmation precondition, actual PQE user/family/environment/time and receipt.
- PhotoPolicyStates: current definition and confirmation pointers, monotonic policy
  version and present/deleted-source state. This is applicability, not history rewriting.
- FrozenTaskPhotoPolicies: immutable TaskItem-keyed Task/confirmation identity and
  full versioned frozen snapshot/hash, restrictive TaskItem/confirmation references.

Existing Standard edits synchronize established definition states under the same
planning transaction after actual retained/new/removed child reconciliation. A meaningful
change creates a new server definition epoch and clears applicability. A→B→A creates
another epoch; it cannot reactivate an old confirmation merely because bytes match.
An actual no-op preserves the confirmation. Changed Standard product/header context
invalidates affected items; unaffected item content retains its confirmation.

Logical source IDs intentionally have no restrictive FK to mutable/deletable Standard
children. Removing/copying/deleting definitions must not delete confirmation history or
unexpectedly break legacy Standard maintenance. Source deletion retires applicability;
confirmation receipts remain recoverable. New copied item IDs have no inherited approval.

Before a durable definition exists, metadata returns null DefinitionId/DefinitionVersion
and the actual current-content hash. GET does not manufacture a revision or approval.
Confirmation remains scoped to the requested real item; complete measurement readiness
is independently required by existing Task-generation validation.

## API and authority

- GET `/api/patrol-standards/{standardId}/photo-policies`: current Admin/PQE with
  Standards.Read, current family; current raw text, definition identity/hash,
  Unconfirmed/ExplicitlyConfirmed/InvalidConfiguration and exact current receipt.
- POST `/api/patrol-standards/{standardId}/items/{itemId}/photo-policy-confirmations`:
  current PQE plus the dedicated PhotoPolicies.Confirm permission and current family.
  Strict input: RequestId, CanonicalizationVersion1, ExpectedDefinitionId (nullable only
  for an unrecorded current epoch), ExpectedDefinitionHash, ExpectedConfirmationId,
  Value exactly Required or Optional. No actor/server approval ID/client time accepted.
- GET `/api/photo-policy-confirmations/{id}`: authorized Admin/PQE/current-family
  recovery of the original immutable receipt, including after the source is deleted.
- GET `/api/line-patrol/tasks/{taskId}/photo-policies`: existing current IPQA/task/line
  authority; each actual item's frozen policy or explicit unconfirmed/invalid state.
  It grants no execution, and canExecute remains false.

Service checks independently validate actual DB role/current family; a forged Admin
permission claim cannot sign. Existing legacy Standard/Plan APIs retain their own
authorization and response contracts. New POST uses existing strict JSON/body/rate
limits. Unknown/duplicate fields, invalid enum/hash/identity/version fail visibly.

Same RequestId/exact fingerprint recovers the original receipt, not a new signature;
changed scope/payload conflicts. Recovery does not restore current applicability after
definition change. GET current state separately. A new RequestId requires exact current
definition and confirmation preconditions. Concurrent competing changes have one winner.
An accepted confirmation is historical business configuration; later signer logout,
expiry/reset/role loss/deactivation does not rewrite its acceptance. Fresh confirmations
always require current authority. No new retroactive policy-revocation workflow exists.

## Generation, locking and frozen compatibility

Plan editing remains scheduling configuration, with existing revisions, boundaries,
overlap and source readiness rules. No Plan-level policy override is accepted. Scheduler
generation independently requires every real current Standard item to have an applicable
valid confirmation matching the exact current definition; missing/drifted/invalid policy
logs SharedTaskPhotoPolicyUnconfirmed and admits no new Task. Disabled/incomplete drafts
and existing tasks retain their original data.

The existing planning gate orders Standard edits, confirmation, Plan revisions and
generation. SQLite retains the nondeferred writer transaction. PostgreSQL retains actor
account NO KEY UPDATE → common planning advisory gate → sorted line roots → Plan/claim;
no account lock is added after the gate, and existing security fences/retries remain.
Generation consumes already accepted immutable confirmations, never a dead signer's
token. A security closure that wins before confirmation prevents that new acceptance.

Inside the same transaction, the generator reloads actual Plan/revision, line, Standard,
item definitions and current confirmations, builds the existing PlanningDefinitionV1,
Task/TaskItems/real initial attempts, and adds all FrozenTaskPhotoPolicies. Snapshot
ContractVersion1 embeds actual Task/Item/Plan/revision and full original policy receipt.
Existing generation uniqueness/idempotency is retained. Any graph/frozen-write/commit
failure rolls everything back; confirmation itself rolls back at every write/commit.

The companion frozen rows deliberately preserve original PlanningDefinitionV1 bytes
and old Task schemas. Reads validate actual stored confirmation/definition identities,
hashes, scope and the actual frozen item/header/Plan revision, without today's mutable
parents or current signer permissions. One validated planning snapshot is reused within
the read, avoiding per-item reparsing of a large full Task snapshot.

## Evidence assessment

AssessmentVersion3 adds actual frozen policy metadata, retaining raw text separately.
For an established ordinary all-OK result, confirmed Optional adds no mandatory photo;
confirmed Required requires a genuine accepted attached current-attempt reference and
actual byte integrity. Ready-but-unassociated, detached, missing or corrupt evidence
cannot satisfy it. Unconfirmed/invalid configuration never defaults to Optional.

Sample minimum/result completeness remains separately visible and is not supplied by
confirmation. NG requirements, Removed, Unable reason/photo, immutable A/B provenance,
accepted sample-transition facts and unresolved NG→OK/repeated-NG newness/coverage
remain unchanged. No confirmed Optional bypasses those unresolved transitions. Evidence
Satisfied is not formal completeness, physical authorship or Submit permission. GET
does not update approval, applicability, availability, task/draft versions or audit.

## Migration, test and review evidence

Additive migration: `20261116145540_R3205ConfirmedPhotoPolicies`. Native UUID/bigint/
boolean/timestamptz/varchar/text types are explicit for PostgreSQL; SQLite has its native
types. New keys/FKs use short names. Native scope/forward-pointer/role/immutability
guards and tracked history guards are applied. Down refuses accepted-history destruction.
Existing applied migrations and all prior ledgers/WorkContexts/security histories remain.

Executable suites: PhotoPolicyTests, PhotoPolicySecurityTests, PhotoPolicyConcurrencyTests,
PhotoPolicyMigrationTests and PhotoPolicyPostgresTests. They cover Required/Optional,
raw/legacy compatibility, stale/no-op/reverted definitions, copy, signing authority,
fake claims/input, current family, duplicate/conflicting requests, all specified race
partners/both orders, native integrity, atomic rollback and fresh/populated migrations.
Real PG cases use actual lock-manager blocking paths; no opt-in skip counts as execution.
Final exact counts, run/job links and complete changed-file manifest belong in PR delivery.

Retained fixtures explicitly obtain real PQE confirmations for new tasks. Historical
pre-policy migration/legacy tasks use a test-only frozen copy of the actual generator
from main `be00802037f868fd9be75f5b69b1a6649dac09dd`, with the unchanged real identity
producer. No production fallback/feature switch, fabricated approval, ledger or sample
is introduced. The old Standard-edit race now asserts blocked generation until explicit
reconfirmation, then retains all original coherent-snapshot assertions. The older binding
migration test is pinned to its historical migration interval, retaining its exact two-table
check; new tests separately demand exactly four additive tables.

Initial checks exposed and corrected the new frozen-policy FK name exceeding PostgreSQL's
identifier limit; both-provider pending-model/name tests retain this regression. Existing
historical names were not rewritten. Metadata now returns null for unrecorded definition
versions; request-local full-snapshot validation avoids repeated parsing. Test-only fixture
and historical interval corrections are not claimed as new product defects.

Full final backend Release/tests, SQLite/migrations, actual disposable PostgreSQL17,
Web lint/tests/typecheck/build, whitespace/secret/scope scans and complete actual diff
self-review are required before review handoff. CI retains every prior PG class and
appends the46 new real-provider cases. Older failing local runs remain evidence; no
failed CI is repeatedly rerun merely to obtain green.

## Deferred / NOT RUN

Formal Submit, PQE result decisions, new inspection attempts/reinspection producers,
NG photo newness/coverage success, F2 sealed intents, F3 retention/deletion/restoration,
Android/Web shared execution, production migration/repair/backfill/rollout are neither
implemented nor claimed tested. This slice supplies backend configuration/frozen read
contracts; configuration UI is not added. All shared tasks remain canExecute=false.
Stop after this one PR for independent ChatGPT Final Review and Peter's merge decision.
