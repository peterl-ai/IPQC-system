# Shared inspection identity and operation acceptance prerequisites — proposed V1

Date: 2026-11-12. Status: **PROPOSED TECHNICAL CONTRACT — D01 option A separately approved by Peter; other proposals remain under documentation review, not implemented or globally signed off.**

## 1. Authority, status vocabulary and verified baseline

Peter authorized this two-file documentation PR on 2026-11-12 and granted standing development authorization for later small, clearly scoped work implementing already-approved requirements. Routine PR implementation does not need repeated permission. Material architecture/business alternatives still require Peter's decision; merging, production operations and destructive cleanup remain excluded. This task stops after one PR and verified CI. Neither publishing nor merging this proposal silently selects an OPEN DECISION or implements its tables/routes.

| Label | Meaning in this document |
| --- | --- |
| APPROVED REQUIREMENT | Existing signed-off business/security invariant; the references below remain authoritative |
| IMPLEMENTED | Observed code at the exact merged baseline, not inferred from the target architecture |
| PROPOSED TECHNICAL CONTRACT | Future engineering design for review; no schema, endpoint or acceptance fact exists because it appears here |
| OPEN DECISION | Material alternative requiring Peter's decision before affected implementation |
| DEFERRED / NOT RUN | Future implementation/test/operational gate; no PASS claim |

Authoritative references, unchanged by this PR:

- [Approved Rev3.2](IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md), especially §§5–10, 11–12 and 16–17.
- [Approved Handoff Supersession](IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md), especially §§3.3–4.
- [R32-04 Readiness](IPQC_R32-04_IMPLEMENTATION_READINESS_REPORT_2026-11-11.md), especially §9 and deferred cases R04-D01..D08. Its original NOT RUN matrix is historical planning evidence.
- [R32-02 implemented personal access](../16_R3202_PERSONAL_ACCESS.md), [R32-03 implemented planning](../17_R3203_SHARED_PLANNING.md), [R32-04 implemented WorkContext](../18_R3204_WORK_CONTEXT.md).

**IMPLEMENTED baseline:** [PR #15](https://github.com/peterl-ai/IPQC-system/pull/15) merged as `6fa84de00d5e719ab4474a77daa89d55eea4f086`; its feature HEAD was `846f984b67d74d87595945eaca4e86ad01285ba9`. Live main at verification was the merge SHA, with no open PR. The main/feature trees both equal `59a3e3544e815cdfd6f903ab62c72d36582d45f3`. Local main was fast-forwarded without resetting/stashing/cleaning unrelated work.

The merge [CI run 43262427172](https://github.com/peterl-ai/IPQC-system/actions/runs/43262427172) passed: backend374/0FAIL/33opt-inSKIP, separate real PostgreSQL17 job33/0FAIL/0SKIP, Web90/18 files plus lint/typecheck/build. These are **existing foundation regression results**, not tests of any proposed contract below. This documentation PR's exact-HEAD CI is reported separately in PR delivery.

**Review gate, 2026-11-12:** while validating this documentation PR's earlier HEAD `a318033f432da7ca64be710c797466b97232a7e4`, [PR CI 43284538286, attempt1](https://github.com/peterl-ai/IPQC-system/actions/runs/43284538286/attempts/1) had PostgreSQL32 PASS/1 FAIL/0 SKIP: `WorkContextPostgresTests.Issue_before_deactivation` reached `WorkContextConcurrencyTests.cs:34`, expected0 unrevoked contexts but found1. The unchanged companion push PG job passed33/0/0. Root cause/security impact remain unresolved; a passing rerun cannot erase that evidence or establish an infrastructure flake. Peter chose to retain this two-document PR, defer merge and arrange a separate security fix. No application/test/CI change or second branch is included here. Final delivery reports subsequent CI attempts/HEAD separately.

## 2. Observed implementation seams

All paths here are repository-relative. These are IMPLEMENTED observations at the merge baseline.

| Source | Actual contract and limitation |
| --- | --- |
| `api/JaxPower.Ipqc.Api/Domain/PatrolPlan.cs` | Stable Task and task-owned `PatrolTaskItem.Id`; separate `SourcePatrolStandardItemId`. Legacy samples have IDs and sequence but no shared attempt, operation or contributor relationship. Formal submission samples are revision copies. |
| `api/JaxPower.Ipqc.Api/Services/LineSharedTaskGenerationService.cs` and `PlanningSnapshot.cs` | Real server-generated ownerless tasks/items and frozen `PlanningDefinitionV1`. No attempt or accepted sample is generated. Planning admission is not inspection acceptance. |
| `api/JaxPower.Ipqc.Api/Services/LineSharedTaskEligibility.cs` | Clean FoundationOnly state; answers/samples/submissions/reviews make the old foundation predicate unavailable. It is not the future execution predicate. |
| `api/JaxPower.Ipqc.Api/Contracts/PatrolTaskContracts.cs` and `Services/PatrolTaskExecutionService.cs` | Legacy input lacks expected DraftVersion, OperationId and InspectionAttemptId. Samples are updated/deleted by sequence; legacy writes enforce ownership and workflow1. Its CurrentRevisionNo CAS is not shared draft CAS. |
| `api/JaxPower.Ipqc.Api/Contracts/WorkContextContracts.cs` and `Services/WorkContextService.cs` | Owner context issuance, metadata and SourcePreflightOnly/acceptsWork=false. No business samples, accepted references, operation ledger or operation receipts. |
| `api/JaxPower.Ipqc.Api/Services/WorkContextStore.cs` and `WorkContextSecurity.cs` | Current authority, source/account locking and sticky revocation with typed audit in the same transaction. Existing security hooks are reusable atomicity constraints, not a business-acceptance service. |
| `api/JaxPower.Ipqc.Api/Data/Migrations/20261111214948_R3204WorkContextFoundation.cs` | Adds only WorkContexts and WorkContextAuditEvents plus checks/FKs/indexes. No draft/attempt/operation schema. Prior ten migrations unchanged; Down refuses destructive removal. |
| `.github/workflows/ci.yml` | Real PostgreSQL filter names PersonalSessionPostgresTests, LineSharedPlanningPostgresTests and WorkContextPostgresTests explicitly. Future classes are not automatically included. |

**APPROVED REQUIREMENT:** keep legacy workflow/auth/Admin JSON/report behavior and history, actual Task.LineId and frozen definitions, and `canExecute=false` until separately implemented execution gates pass. No legacy route or assignment can be reused to bypass shared identity/authority. Existing WorkContext issuance/preflight must eventually support the reviewed execution lifecycle; do not merely relax FoundationOnly to accept arbitrary dirty rows.

## 3. Real identity and lifecycle

### 3.1 Identity invariants

**APPROVED REQUIREMENT:** `StandardItemId != TaskItemId != InspectionAttemptId != SubmissionAttemptId`. An F2 submission request identity is not an item inspection attempt. Sequence, display name, current user and task revision are not identities. Old accepted work cannot satisfy a required new inspection attempt.

**PROPOSED TECHNICAL CONTRACT:** each actual item inspection attempt receives one nonempty server GUID, with restrictive Task/TaskItem relationships and validated joint scope. Preserve `PatrolTaskItem.Id`; never clone/rekey items merely to represent reinspection. Each item's current applicable attempt is explicit and selected under Task locking. An ordinal may display chronology, but neither ordinal1 nor a deterministic placeholder creates a real attempt. Client-supplied unknown attempt IDs fail; clients must obtain a real server identity before offline work can be associated with it.

An initial attempt record expresses an actual newly established, empty inspection work scope, not a measurement or completion. Its truthful creation event/time is distinct from ScheduledOccurrenceUtc, WorkContext issuance, accepted-operation time and formal Submit time. Attempt creation never implies a sample, operation acceptance, StartedAt, actor authorship, task transition or submission unless that separate real event occurred.

### 3.2 Initial creation — D01 option A approved by Peter

The original approved architecture requires real identity and no fabricated history, without selecting eager versus explicit lazy initialization. The following alternatives were presented to Peter; both preserve one durable initial attempt per admitted TaskItem, idempotent duplicate initialization and zero accepted measurements. They are retained as the decision record, not two simultaneously authorized implementations.

| Option | Advantages | Costs / risks |
| --- | --- | --- |
| A — create initial attempts atomically with future task generation; explicitly initialize eligible existing clean, admitted FoundationOnly tasks through a separately scoped server action | Downloaded future tasks already have stable offline scope; duplicate scheduler runs cannot create duplicate attempts; existing task initialization has truthful present-time audit | Adds rows at generation; requires coordinated planning lock protocol and an explicit policy/eligibility inventory for existing tasks |
| B — initialize only on an explicit authorized online preparation action, before downloading executable work | Keeps planning generation inspection-independent; same explicit entry point for new and existing eligible tasks | Adds an online preparation step; GET/context issuance cannot silently initialize; clients must distinguish uninitialized from available and cannot invent offline IDs |

**APPROVED REQUIREMENT — D01 resolved, Peter, 2026-11-12:** Peter explicitly selected **A: create initial attempts with future task generation; explicitly initialize eligible existing tasks**. Use an explicit, auditable initializer only for inventoried clean admitted FoundationOnly tasks, with no SQL backfill or historical conversion. This supplies stable identities before offline work while keeping creation truthful. Option B is not selected. D01 is no longer OPEN; the authority is Peter's direct decision, not publication/merge of this proposal. All other schema/API/locking proposals retain their stated review status. This documentation task still implements neither attempts nor initialization.

HistoricalUnadmitted, inconsistent, legacy, answered or formally historical tasks remain unsupported for initialization. No attempt is reconstructed from current Plan/Standard or fabricated for old sample/submission rows. Actual persisted shared-row inventory remains an implementation/rollout prerequisite, not an assumed zero count.

### 3.3 Reinspection and closure

**APPROVED REQUIREMENT:** a targeted reinspection uses a new attempt/new sample set. Applicable formal NG permits new work without waiting for PQE; a PQE rejection requires reason and valid targets, which may include prior OK/Removed. Non-targeted valid OK results may carry forward. Draft NG→OK, save, logout, transport retry or source revocation is not a new formal NG/PQE event.

**PROPOSED TECHNICAL CONTRACT:** the future formal NG transition creates exactly one next attempt for each actually required NG target; PQE rejection creates one for each actual selected target. Persist the actual immutable SubmissionId or review-event ID, trigger kind, predecessor attempt and server creation time; validate that the trigger really targets that item/revision. Unique `(TaskItemId, TriggerKind, TriggerId)` and a unique initial-attempt constraint prevent duplicate creation. A replay of an old formal event resolves the existing attempt rather than inventing a new cycle.

Create/retire the applicable-attempt pointer atomically with the real formal transition and write-version change. A trigger producer must recheck current state, exact formal revision/targets, applicable predecessor and draft version under the same Task lock; an old review/NG event cannot silently retire a newer attempt or discard its pending draft. Immediate PQE visibility of NG is not, by itself, authorization for a review mutation. The eventual state/review contract must establish that permission. Do not pre-create fake formal events in an identity foundation PR. Retain prior attempts, accepted results and receipts; new acceptance targets only the currently applicable editable attempt. An exact old accepted replay may recover its historical receipt even after that attempt closes; it cannot reapply old content to the current attempt. Internal lifecycle labels must not introduce new Task business states. Event-specific producers remain deferred to their actual R32-05/07 workflows.

### 3.4 Stable sample identity

**PROPOSED TECHNICAL CONTRACT:** a client creates a stable `ClientSampleId` only for a real captured sample in a known real attempt. The server maps `(InspectionAttemptId, ClientSampleId)` uniquely to a server SampleId at first actual acceptance. Server identities are not preallocated dummy measurements. Sequence/display order is separately editable, bounded and unambiguous; reorder must not change sample identity or create fresh measurement counts.

Corrections/removal use new immutable OperationIds against the same sample identity and expected server state; preserve the operation history and materialize the current value/tombstone. Never overwrite an accepted operation. A second real sample may have the same measured value, but copying prior-attempt sample IDs/operations/bytes does not make a new inspection. Local pending IDs are organization/replay keys, never proof of accepted work or physical capture. An empty UI row is not a captured measurement: it cannot mint a server sample or increase an accepted/formal count. Incomplete draft support does not permit placeholder zero/null samples to satisfy measurement requirements.

## 4. Samples, dispositions and completion

The following are **APPROVED REQUIREMENT**, from Rev3.2 §§10–11; no new business treatment is introduced:

- Initial measured minimum is the frozen SampleCount, default1 only when missing; valid configured count1..50, maximum50. Malformed/unsupported frozen definitions are unavailable, not silently defaulted or clamped.
- A new targeted attempt's minimum is `max(frozen minimum, prior applicable formally measured count, inherited requirement)`, maximum50. A Removed/UnableToMeasureNG path cannot lower a previously established minimum; arbitrary draft counts cannot be mislabeled formally measured history.
- Qualitative samples use OK/NG; ordinary quantitative samples require actual Inspection Value and permit optional repeatable Barcode. Judgment uses frozen limits and exact numeric rules. No new trusted per-sample measurement timestamp is claimed.
- Removed is per item after Production/EQ confirmation of absence/non-applicability, optional reason, truthful actor and retained definition; no bulk remove. It requires no fake zero or sample. Warn on the last normal item's removal; valid all-Removed may later submit NotPerformed, unlike a corrupt task with zero frozen items.
- UnableToMeasureNG requires NG Reason and a new current-attempt photo, without a fake zero/sample. Ordinary NG reason/type/remarks remain optional. Preserve optional PIC and header contracts for their later scoped implementation.
- New LineShared work has no legacy N/A. Partial Removed with all remaining normal items passing follows ordinary approval; all-Removed requires its separate special confirmation. Do not use legacy Qualified/Approved to fabricate that outcome.
- NG requires new current-attempt evidence; NG→OK requires new improvement evidence; repeated NG requires new evidence. PQE-targeted prior OK/Removed follows the same rules, not an unconditional photo rule for every Optional OK item. Unknown PhotoRequirement text cannot become Optional automatically.

**PROPOSED TECHNICAL CONTRACT:** persist the resolved attempt minimum and its actual frozen/formal/inherited source references so later source changes cannot lower it. Draft operations may represent incomplete permitted fields/sample sets. Accepting such an operation means those exact draft facts were durably stored, not that inspection/sample minimum/evidence completeness passed. Reject structurally invalid values, over-cap samples and illegal transitions at write time; evaluate required completion, sample minimum, applicable attempt and actual Ready evidence before formal submission. Missing evidence stays incomplete, never fake Ready. Draft sync does not transition a formal NG/PQE state or increment CurrentRevisionNo. Formal Submit/review and other persisted changes to business editing eligibility must advance the relevant write version atomically, preventing stale draft acceptance after a state transition; source-security actions still use their own source/account ordering.

## 5. Future schema and API boundary

Everything in this section is **PROPOSED TECHNICAL CONTRACT**, not a migration/file inventory for this documentation PR. Physical naming and generated migration timestamps belong to later scoped implementation.

| Logical record | Required facts / constraints |
| --- | --- |
| InspectionAttempt | Server ID, TaskId/TaskItemId, initial/reinspection kind, actual trigger/predecessor when applicable, frozen/inherited minimum with references, truthful creation, current applicability; scope and unique creation guards from §3 |
| SharedSample | Server ID, attempt FK, unique client capture key, current typed value/judgment/barcode/order/tombstone projection; immutable operations explain corrections, no placeholder measurements |
| SharedDraft | One Task-scoped version/root and actual applicable-attempt bindings; nonnegative DraftVersion with checked monotonic changes. Do not add an unused version that accepts no work |
| ContributionOperation | Unique `(TaskId, OperationId)`, typed payload/result and dependencies, TaskItem/real attempt/sample scope, source context/derived A, actual first accepting B/family, canonicalization version/hash, accepted UTC, resulting DraftVersion and immutable receipt linkage |
| AcceptedBatchReceipt | Unique `(TaskId, BatchId)`, immutable versioned request fingerprint, original accepted operation/result mappings, accepting B/family, accepted UTC and resulting DraftVersion. No JWT/proof or guessed formal revision |
| AcceptanceAudit | Truthful stable actors, operation/batch/version/scope and proof-free change facts in the same transaction; append-only, separate from the unrelated device/provisioning aggregate |

Restrictive FKs preserve history; the service and database constraints must jointly enforce Task/item/attempt/sample consistency. A FK to an existing ID alone does not establish joint scope. PersonalSessionId remains historical scalar provenance, not a FK to a rotating refresh-token row. Protect ledger/receipt/audit append-only facts with tracked-write guards and service encapsulation; no public update/delete, cascading deletion or raw bulk bypass. Privileged direct DB tampering is outside the application guarantee. Typed immutable operations plus projections are sufficient; do not introduce a generic event-sourcing/merge platform or arbitrary JSON document writer.

Candidate routes under `/api/line-patrol/tasks/{taskId}`:

| Candidate | Future semantics |
| --- | --- |
| GET `/shared-draft` | Current task-read authority; versioned actual draft/current attempt IDs, accepted references and completeness. No initialization on GET |
| POST `/operation-batches` | Current eligible uploader; strict bounded typed manifest, BatchId, expectedDraftVersion, expected actual scope, operations and accepted references; atomic first acceptance or exact receipt recovery |
| GET `/operation-batches/{batchId}/receipt` | Current task-read authority, including recovery when no longer editable; original durable outcome without A login/proof |
| GET `/operations/{operationId}/receipt` | Same current read authority; resolve stable operation outcome when local batch acknowledgement is missing |

Approved D01(A) requires an explicit eligible-existing-task initializer; its route and strict input contract are frozen in that later identity slice. Input limits/error codes for operation acceptance are frozen with the eventual endpoint scope, using existing auth-before-binding, no-store/redaction and bounded-body conventions. Do not silently inherit the preflight100-context/32KiB limits as a complete sample-batch policy. Unsupported scope404, invalid shape400, version/identity/content conflicts409, authority401/403 and exhausted known transient contention503 retain their separate meanings.

## 6. Operation identity, manifests and immutable receipts

### 6.1 Hash and actor boundaries

**APPROVED REQUIREMENT:** A is server-resolved contributor; B is current transport/first actual uploader; C is whoever later explicitly submits. WorkContext proves issuance/proof possession, not physical operator, precise offline capture time or authentication. The accepted limitation is not an OPEN DECISION.

**PROPOSED TECHNICAL CONTRACT:** each immutable operation binds OperationId, actual environment/Task/Line/workflow/item/attempt/sample scope, typed kind/payload, source WorkContext/owner assertion, dependency identities/hashes, originating LocalEditVersion and expected draft base. CanonicalizationVersion fixes property/collection ordering, invariant exact decimals, Unicode/string and null/empty treatment; reject unknown versions and recompute server-side. Sets and ordered sample content must have explicitly different ordering rules. Test equivalent inputs and ambiguity attacks with frozen vectors before release.

Exclude raw WorkContext proof, its stored proof-secret hash, JWT, refresh credentials and current transporter/session from the business canonical hash. Persist actual B/family separately at first acceptance; a later B2 recovery cannot rewrite them. Changing/rebasing payload or source uses new OperationIds, preserving the old conflict/provenance records. Unchanged exact accepted operations retain their identity and original receipt. These draft rules do not permit B to POST A's frozen F2 intent.

### 6.2 Manifest resolution

**PROPOSED TECHNICAL CONTRACT:** a batch declares exact typed new operations, accepted-operation references and source/dependency/evidence identities; raw proofs occupy a separate redacted transport map. Validate duplicates, joint scope, dependency existence/hash, acyclic ordering, permitted operation kinds and exact dependency closure. No missing dependency is inferred from latest draft or mutable Standard. A client `accepted` flag or supplied SampleId is never authority.

Resolve every known OperationId against the server ledger before classifying it as new. Matching accepted content supplies its original receipt, even when the client thought it unacknowledged. An accepted reference used to construct new state must still be applicable to the current task/item/attempt/dependencies. A closed-attempt receipt is recoverable historical fact, not a valid reference satisfying new reinspection. A conflicting hash/source/scope under the same accepted ID is409; never substitute B provenance for missing A proof.

Previously unaccepted operations require their saved valid scoped proof and eligible, non-security-revoked source, independently of B's current auth. Capture/family expiry, logout or self-password AuthVersion changes alone do not reject historical provenance; client-reported time cannot prove capture. Already accepted references require neither current A eligibility nor old raw proof. Consequently the existing source-preflight cannot be run indiscriminately over all accepted references, or used as a reusable acceptance authorization.

F3 must validate exact source/dependency manifest before accepting unaccepted photo bytes and revalidate when attaching Ready evidence. A preflight token/response cannot replace later atomic checks. No photo receipt, actual bytes, Ready fact or storage reference is created by this document or WorkContext.

### 6.3 Acceptance/replay algorithm

**PROPOSED TECHNICAL CONTRACT:**

1. Authenticate current B and validate actual task read authority before outcome disclosure; for new acceptance require current IPQA write eligibility under the transaction. A proof/receipt/installation ID never authenticates B.
2. Acquire the compatible lock set and reread authoritative facts. An existing accepted BatchId with the same versioned request fingerprint returns its original receipt before stale expected-version or new-source eligibility tests; different fingerprint conflicts. Authentication remains mandatory. Recovery is read-only even if the task is no longer editable.
3. Resolve operation IDs and references as in §6.2. A replay-only request returns original operation receipts without applying them, creating another acceptance fact or bumping DraftVersion. A new batch containing both accepted matches and fresh operations treats the former as accepted references, validates their present applicability, and never reinserts/reapplies them. New accepted batches require at least one fresh operation; reference-only reads cannot claim a version advance.
4. For fresh operations validate current B, actual task/state/attempt, new sources, typed data and dependencies. Conditionally claim expected DraftVersion in the database; one successful fresh batch advances it once, not once per operation. Locking alone is not the externally observable CAS contract.
5. Persist the entire fresh ledger, materialized draft, audit and immutable batch/operation receipts in the same transaction. Save/commit failure returns no success. A mixed invalid batch rolls back all its new effects while preserving prior accepted facts.

The request fingerprint covers the exact frozen envelope, expected version, operation hashes and references, excluding transport secrets/current credentials. The first receipt records the actual first B; exact replay by an authorized different B cannot make that person the original uploader. Unknown/never-accepted request IDs are not falsely advertised as permanently reserved by an accepted-only ledger.

### 6.4 Lost response and two-tablet conflicts

**APPROVED REQUIREMENT:** the boundary is server commit, not local acknowledgement. One receipt NotFound is only a point-in-time observation; it cannot prove an earlier request will never commit. Keep original payload/IDs/files/proofs and OutcomeUnknown while resolving.

**PROPOSED TECHNICAL CONTRACT:** retry the exact frozen batch or query its operation/batch receipts with current B authorization. Same-base concurrent requests serialize at the Task/CAS: either the old request accepted and its receipt is recovered, or another real version transition fences its later first acceptance. A stale GET or absence alone is not such a fence. If uncertainty persists, keep recovery pending; do not mint speculative revisions, recreate lost payload or silently rebase. After a conflict, explicitly refresh/reapply eligible changed work with new IDs; preserve originals and accepted server state. Acknowledging local version n cannot acknowledge n+1.

## 7. Security, transaction protocol and recovery

**APPROVED REQUIREMENT:** reset/deactivation/IPQA eligibility loss/explicit source revoke blocks new source acceptance, not accepted history. Ordinary logout/expiry/self-password change does not sticky-revoke A history. Reactivation never revives revoked contexts. Current B auth is checked independently on every attempt. Existing Admin user APIs keep their permission/JSON/session compatibility; do not add the new personal-family requirement globally or revoke contexts from generic session closure.

Unaccepted revoked-source work and dependent unverified evidence are quarantined with originals retained. B must actually re-perform affected work under B's context/new IDs, not relabel A's values. Resolve uncertain receipts before classifying data as unaccepted. Quarantine must not delete valid previously accepted facts, and cannot be bypassed by full-draft replacement. Client queue isolation is DEFERRED / NOT RUN, not an implemented server vault.

**PROPOSED TECHNICAL CONTRACT — SQLite:** reserve a non-deferred writer transaction before authoritative reads; clear stale tracking, validate current authority/scope/source/attempt, conditional version claim and all ledger/projection/audit/receipt writes in that unit. Use independent connections for races, bounded wait and rollback-safe retries. Never execute PostgreSQL locking SQL here or infer provider correctness from an in-memory database.

**PROPOSED TECHNICAL CONTRACT — PostgreSQL:** extend the implemented account-first protocol: discover immutable source-owner IDs solely for lock discovery; lock B and distinct new-source owners in sorted GUID order `FOR NO KEY UPDATE`; reread authority/source facts. Lock sorted configured line roots `FOR SHARE`, Task `FOR UPDATE`, draft root, sorted item/attempt scopes, then sorted source contexts and operation/receipt records as needed. Recheck all discoveries. Task locking serializes creation of missing draft/ledger keys; unique constraints and conditional version updates remain required. No acquisition of the planning advisory gate after holding task/context locks. Do not upgrade all account locks to FOR UPDATE and recreate audit-FK deadlocks.

Under approved D01(A), the future eager generator must remain planning-gate→line→new Task/attempts, without acquiring accounts after the planning gate; explicit initialization uses the reviewed current-account→line→Task order. No path may take one order and later request the earlier gate. Existing account-security actions lock their account then contexts in their current transaction, without nested stores. The complete future graph must test both race orders with security, planning/line changes, refresh/logout and multiple sources. This is a proposed lock protocol, **not proof of deadlock freedom**; real PostgreSQL tests are mandatory. D01 does not approve ignoring the existing CI race noted in §1.

Snapshot visibility is part of this protocol: [PostgreSQL17 isolation rules](https://www.postgresql.org/docs/17/transaction-iso.html) distinguish statement snapshots from Repeatable Read/Serializable transaction snapshots. Acquiring a previously blocked row lock is not proof that every later query sees newly committed contexts. Test a security transaction whose snapshot starts while issuance holds the account lock, then verify that successful security completion covers the committed context. This requirement is not a proven diagnosis of the CI failure.

Only known rolled-back provider conflicts get bounded whole-transaction retries, using the repository's at-most3 attempts/5s lock-wait convention. Unknown commit/network outcomes use receipt reconciliation, not blind write retry. No network/storage calls or success delivery inside an uncommitted database transaction. Fault injection at version claim, ledger insert, projection, audit, receipt and commit must show all-or-nothing behavior.

For future additive migrations preserve existing columns/IDs/FKs/indexes and all applied migrations. No backfilled attempt/accepted contribution/submission facts, destructive Down, sample rewrite or legacy workflow conversion. Do not claim the full historical SQLite chain is crash-atomic: R32-03's documented rebuild/history-write interruption caveat remains. A persistent database that applied an earlier draft R3204 definition requires owner-reviewed reconciliation; do not edit the merged migration to repair it.

Application rollback retains expanded schema/ledger/audit and closes new ingress before older binaries lacking security hooks can run. Restore closes ingress/workers first, reconciles accounts/sessions/context revocations and Task/draft/receipt history, then permits writes. Restored counters/receipts must not allow a client ID whose post-backup acceptance is missing to be silently accepted as new. Preserve uncertainty and require reviewed forward reconciliation before reopening; no invented restoration epoch or automatic receipt recovery is implemented here. Photo storage/tombstone reconciliation and retention remain F3 responsibilities under the [restore runbook](../11_POSTGRES_BACKUP_RESTORE.md).

## 8. Small future PR sequence and gates

**PROPOSED TECHNICAL CONTRACT:** reorder only prerequisite engineering work, not approved business outcomes. Every future PR starts from verified merged main, stays independently reviewable and stops for independent Final Review/Peter's merge decision. Standing authorization covers clearly scoped approved work; D01(A) has Peter's separate decision, while other proposals keep their stated status. Neither authorizes starting another PR in this task. Peter deferred this PR's merge pending independent review and the separately arranged security issue.

| Slice / proposed title | Minimum coherent scope | Exit/dependency |
| --- | --- | --- |
| Next identity slice, after the separately arranged security work: `R32-05 prerequisite: establish real shared inspection attempt identities` | Under approved D01(A): additive identity/applicability model, truthful initial creation path, stable TaskItem binding, initial minimum snapshot, read contract and duplicate/rollback/provider tests. No fake samples/acceptance, no formal event producer; exact file/migration inventory must be verified for that slice | Existing-task inventory and satisfactory security baseline; canExecute=false; future execution-aware WorkContext/eligibility contract explicitly reviewed |
| `R32-04 follow-up: atomically accept typed shared sample draft operations` | After real attempts: a minimal typed real sample draft operation set with stable sample mapping, DraftVersion CAS, immutable ledger/receipts, manifest/source validation, audit and rollback together. Do not split out a pretend accepting CAS/empty ledger PR | Current attempt/source/eligibility checks and real provider races pass; incomplete draft is not completed inspection; no Submit or broad execution activation |
| Later R32-05 slices | Actual sample/disposition/header/PIC/state contracts, targeted reinspection producers tied to real domain events; preserve legacy behavior | Coordinate real formal NG/PQE triggers with R32-07; no synthetic submissions to make tests pass |
| R32-06 / F3 and R32-07 / F2 | Actual storage/Ready/reference/retention; explicit frozen Submit, exact review events and six takeover invariants | Separate scoped readiness; no byte acceptance or formal Submit activated by draft sync |

D01 no longer blocks the identity prerequisite. Its next scoped implementation still needs a verified file/migration inventory, existing-task eligibility inventory and a satisfactory security baseline; Peter's decision to keep this PR documentation-only remains binding. Operation acceptance additionally requires the real identity model and reviewed typed operation/read/eligibility contracts, so it is not implementation-ready now. A schema-only empty ledger, arbitrary payload endpoint or fake attempt is not a shortcut. The exact application/test/migration file inventory belongs to each future scoped task, not to this two-document PR.

F2 must preserve actual C/origin family/context, expected draft/base revision, canonical frozen content and evidence manifest, query-only old intent after takeover, one base-state winner and late-commit recovery. B may query A's receipt but cannot POST A's intent; unchanged applicable accepted A facts may enter B's own explicit Submit. F3 must supply actual Ready/storage/reference locks and rollback/restore gates. These integration requirements remain DEFERRED / NOT RUN; source preflight is neither contract's success signal.

## 9. Proposed acceptance and falsification matrix

Every case below is **DEFERRED / NOT RUN**. IDs identify proposed tests, not test counts or additional approvals. Existing R04-01..32 foundation evidence and R04-D01..D08 boundaries are unchanged.

| Case | Slice | Positive / negative or race oracle |
| --- | --- | --- |
| ID-01 | Identity | Real generated item IDs preserved; wrong Task/StandardItemId/sequence/unknown attempt rejected, zero new measurements |
| ID-02 | Identity | Selected D01 path concurrent/retried initialization creates one real initial attempt/item; audited creation time is present-time, not occurrence/measurement time |
| ID-03 | Identity | Legacy, historical-unadmitted, corrupt, answered/submitted tasks remain unchanged; no implicit GET/context initialization or migration backfill |
| ID-04 | Identity | Fresh/populated upgrades retain every old column/ID/FK/index/audit; both provider models clean; SQLite foreign_key_check empty; protective rollback documented |
| OP-01 | Operations | Real capture→unique sample mapping; reorder/correction retains identity; equal measured values allowed; old-attempt IDs cannot satisfy new work |
| OP-02 | Operations | Missing/wrong/duplicate/cyclic dependencies and invented accepted flags rejected atomically; no generic full-draft bypass |
| OP-03 | Operations | Same accepted ID/hash returns original receipt; changed scope/source/payload/hash-version conflicts; accepted replay does not bump version or rewrite B |
| OP-04 | Operations | A proof/receipt/forged principal cannot authenticate B; direct service and HTTP unauthorized/current-family/forced-change cases deny before write |
| OP-05 | Operations | Normal A logout/expiry/self-password preserves historical source; reset/deactivate/role-loss/explicit-revoke denies new A work; reactivation stays sticky |
| OP-06 | Operations | Acceptance-first/revoke-first on SQLite and real PG: former keeps accepted A/B facts; latter prevents acceptance; mixed bad batch leaves prior facts intact |
| OP-07 | Operations | Two independent clients expected v: one commits v+1, other409, no last-write-wins; retained original payload/IDs, explicit rebase uses new IDs |
| OP-08 | Operations | Lost response, exact retry, early NotFound and late commit recover one original outcome; closed-attempt replay is receipt recovery, not new application |
| OP-09 | Operations | Inject failure at CAS/ledger/projection/audit/receipt/precommit: no partial version, accepted rows or success; subsequent safe retry works |
| OP-10 | Operations | Multi-source sorted locks vs account/line/planning/refresh/logout writers, both orders including snapshot-before-lock-wait/newly committed contexts, bounded waits/retry exhaustion and no false401 on contention |
| OP-11 | Operations | Canonical vectors cover decimals/order/null/Unicode and unknown versions; secret/current transport exclusion; raw proof/proof-secret hash/JWT absent from logs, URLs, audits and receipts; content hashes remain allowed |
| OP-12 | Operations | Replay-only returns original receipts with no version bump; mixed accepted/fresh IDs preserve original per-operation uploaders and validate applicability; mismatched accepted ID conflicts; ledger/receipt/audit mutation and cascade attempts fail |
| RULE-01 | R32-05 | Default/minimum1/count50/count51/malformed snapshot; incomplete draft allowed, formal minimum enforced; Removed/unmeasurable creates no sample |
| RULE-02 | R32-05/07 | Real NG/PQE target event creates one next attempt; event replay cannot duplicate; stale target/revision/draft cannot retire newer work; inherited formal count survives dispositions; non-targeted OK provenance retained |
| RULE-03 | R32-05/06/07 | UnableToMeasure reason/photo, NG→OK/new NG evidence, all-Removed special outcome; missing actual Ready blocks formal completeness, not fake success |
| INT-01 | F2/F3/client | Six F2 handoff cases, new B intent, late A commit, actual bytes/Ready/reference/restore; Android personal-key logout isolation and two-model UAT remain deferred |
| REC-01 | Operations/IT | Rollback retains expanded history; restore with post-backup accepted receipts does not silently reaccept/reuse versions; ingress stays closed until reconciled |
| REG-01 | Every code slice | Legacy auth/Admin/ownership/review/report, R32-01/02/03/04 regressions and shared canExecute=false; no unrelated migration/device/PQE permissions |

Future code gates: full Release/backend/Web; disposable SQLite fresh/populated/race/fault tests; real PostgreSQL17 separate-connection races and populated upgrades with **no opt-in SKIP counted as PASS**; both-provider model/native SQL review. Include new PG classes explicitly in CI's filter and inspect actual discovered/executed counts. Provider SQL generation or synthetic exception tests are not real PG execution.

This documentation PR runs scope/link/status/whitespace checks and existing CI only. Its green CI cannot promote the proposed cases above to PASS. No Android, production DB inventory/upgrade, restore rehearsal, file storage or QA/PQE UAT is executed. Do not introduce production identifiers, secret fixtures, MES/Material Verification, cancelled device PKI, deployment or another PR into this task.
