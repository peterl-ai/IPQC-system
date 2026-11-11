# R32-04 — WorkContext / F1 Foundation Implementation Readiness

Date: 2026-11-11, America/New_York. Status: **READ-ONLY APPLICATION AUDIT COMPLETE; PROPOSED IMPLEMENTATION SCOPE; NOT IMPLEMENTED.** Publication of this report is authorized; application implementation needs Peter's separate authorization. Every R32-04 case below is **NOT RUN**.

## 1. Authority, verified merge and audit baseline

Authoritative requirements, read in full:

- [Approved Rev3.2](IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md), especially §§5–9, 12.2, 14, 16–17.
- [Approved Handoff Supersession](IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md), especially §§3–4. Historical Rev3.1/original Handoff remain unchanged; F2 business and F3 guarantees are retained.
- Current AGENTS.md, CLAUDE.md, README.md; actual merged R32-01/02/03 source, EF model/migrations, API contracts and relevant tests. [R32-02 implemented contract](../16_R3202_PERSONAL_ACCESS.md), [R32-03 implemented contract](../17_R3203_SHARED_PLANNING.md), [restore runbook](../11_POSTGRES_BACKUP_RESTORE.md).

Verified merge evidence:

| Fact | Result |
| --- | --- |
| PR | [#14](https://github.com/peterl-ai/IPQC-system/pull/14), MERGED at 2026-11-11T16:54:01Z |
| Final reviewed PR HEAD | `201bc90afe232def55518f40a3400c9544926f11` |
| Merge commit / application audit baseline | `385babfc937276fc8c167527ae2631a32542bf46` |
| Merge parents | `f935ad8e4a0e5809c7845de912644edb516eb794`, `201bc90afe232def55518f40a3400c9544926f11` |
| Historical P2 threads | no-store, full Standard readiness, revision reconciliation: all RESOLVED by `peterl-ai` before merge; all threads fetched, no further thread pages |
| Review / merge gates | Exact HEAD unchanged, backend/frontend/live PostgreSQL PR and push checks successful, CLEAN/MERGEABLE, no changes-requested review; normal merge API with expected-HEAD guard; no protection bypass |
| Local synchronization | Existing local main fast-forwarded to origin/main; original unrelated untracked work retained |
| Required main artifacts | `docs/17_R3203_SHARED_PLANNING.md` and `api/JaxPower.Ipqc.Api/Data/Migrations/20261111142052_R3203LineSharedPlanningSnapshots.cs` present |
| Post-merge main CI | [43062841633](https://github.com/peterl-ai/IPQC-system/actions/runs/43062841633), all three jobs successful on this exact merge SHA |

The report's later documentation-only publication commit is separate from this application baseline. Its actual SHA, remote main SHA and byte-match evidence are supplied with delivery; this file cannot embed its own commit hash. No production database, deployment, cleanup activation or shared execution was performed.

## 2. Recommendation and exact scope boundary

Use **one small backend foundation PR** for R32-04: server-issued task-scoped WorkContext, redacted source-proof preflight, sticky security revocation integrated with existing account mutations, append-only context audit, one additive migration and provider-specific tests. Keep every shared task `canExecute=false`.

Rev3.2 §16 names a larger sequencing bucket including draft versions, operations, source manifests and quarantine. Peter's current instruction explicitly excludes shared inspection draft execution. Therefore this first PR does **not** add a draft write or accepted-operation ledger. It establishes context/source validation and the future acceptance contracts below. A later separately scoped R32-04 follow-up supplies real draft/operation acceptance before R32-05/06/07 consumers use it. Do not disguise a metadata preflight as accepted inspection work, a durable operation receipt or completed F1 end-to-end implementation.

Included:

1. Authenticated owner-only issuance, from a freshly revalidated current personal family and actual supported Task.
2. A 256-bit opaque proof, stored hashed, bound to environment/user/origin family/task/actual line/workflow and a bounded capture period.
3. Current B may preflight saved A proofs for historical provenance, without A's credentials or physical-device assertions.
4. Account deactivation, loss of IPQA eligibility, Admin security password reset and explicit Admin context-security revocation atomically revoke affected contexts with truthful audit. Reactivation never clears them.
5. Non-secret metadata/history reads and explicit Admin revoke; no new business-management Web page.
6. Document the independent shared-work storage contract and future accepted-fact/F2/F3 integration, without Android code or pretend storage tests.

Excluded: Android project/APK/screens/Room/coordinator implementation; inspection samples/results/PIC/dispositions; new inspection attempts or Task states; shared draft execution, synchronization acceptance and DraftVersion mutation; Submit/receipts/takeover implementation; evidence upload/Ready/retention; device enrollment/certificates/mTLS; production changes; changes to signed architecture.

## 3. Exact source findings

Paths and line anchors refer to application baseline `385babf`. Proposed changes below are not claims that these facilities already exist.

| Actual source | Observed behavior / implementation consequence |
| --- | --- |
| `api/JaxPower.Ipqc.Api/Security/PersonalAccess.cs:37` | `ValidateAsync` verifies current active user, AuthVersion, exact signed family/environment, unrevoked/unexpired family and IPQA/forced-change policy. Reuse it inside the context transaction as well as route middleware; a prior request precheck is insufficient for a mutation. |
| `api/JaxPower.Ipqc.Api/Domain/ApplicationUser.cs:26` | Refresh row Id rotates; nullable PersonalSessionId is the stable family. The family is not an independently keyed parent table. Do not invent a FK to a rotating token row or to a merely filtered-unique family column. |
| `api/JaxPower.Ipqc.Api/Services/PersonalSessionStore.cs:25` | Account-first SQLite non-deferred transaction / PostgreSQL account FOR UPDATE; bounded rollback-safe retries. Reuse transient classification, not nested store transactions. A multi-source context check needs deterministic multi-account locking. |
| `api/JaxPower.Ipqc.Api/Services/LocalAuthService.cs:89,110` | Ordinary logout closes a family; normal self-service password change increments AuthVersion and revokes sessions. Neither is source-security revocation under Rev3.2. Do not hook context revocation into generic token revocation or any AuthVersion change. |
| `api/JaxPower.Ipqc.Api/Services/ApplicationUserService.cs:83,103,124` | Role/activity and Admin reset are already serialized with token revocation, but no contexts/audit exist. Add the source-security hook inside these existing transactions; retain assignment/final-Admin guards. Reset currently receives actor username only; pass the authenticated stable actor Id from UserEndpoints, without altering the external JSON contract. |
| `api/JaxPower.Ipqc.Api/Endpoints/UserEndpoints.cs` | Existing UsersRead/UsersWrite is Admin-only, does not impose the new personal-family requirement. Preserve these legacy account-management permissions while adding atomic context invalidation to their service paths. New context-management routes require a current family separately. |
| `api/JaxPower.Ipqc.Api/Services/ConfiguredLineDirectory.cs` | Exactly two configured real active lines, fetched fresh; absent/invalid configuration is 503. No device join. Context issuance/preflight must use the actual task line, not the selected screen filter. |
| `api/JaxPower.Ipqc.Api/Services/LinePatrolReadService.cs:11,35,55` | Server-derived receipt is capped at 72h/family expiry. Detail accepts only the foundation subset and returns canExecute=false. Availability is private and no issuance service exists. Extract/reuse the foundation eligibility predicate narrowly; do not broaden it into inspection eligibility. |
| `api/JaxPower.Ipqc.Api/Services/PlanningSnapshot.cs` | ValidV1 verifies frozen Task/Plan/line/definition identities and calendar facts. It does not prove inspection readiness or provenance. Require coherent admitted ValidV1 plus the existing clean foundation state for new capture contexts; historical-unadmitted rows remain readable under their old contract but receive no new context automatically. |
| `api/JaxPower.Ipqc.Api/Domain/PatrolPlan.cs` | Task has frozen WorkflowVersion/LineId, CurrentRevisionNo and legacy item/submission collections; no DraftVersion, WorkContext, inspection-attempt identity or contributor/uploader fields. CurrentRevisionNo and SharedPlanRevision cannot substitute for DraftVersion. |
| `api/JaxPower.Ipqc.Api/Services/LineSharedPlanningStore.cs:43,50,77` | Actor NO KEY UPDATE -> planning gate -> sorted line roots. Compatibility with audit FK KEY SHARE is intentional. Context code must not take a stronger multi-account lock and then invert this order through a line/audit writer. |
| `api/JaxPower.Ipqc.Api/Services/ProductionLineService.cs:31` | Provisioning transaction locks line and fences admitted Plans on actual activity changes; reservations still block deactivation. A line deactivation is not automatically a permanent user/source security revocation; current task/line access still fails when inactive. Preserve this behavior. |
| `api/JaxPower.Ipqc.Api/Data/IpqcDbContext.cs:25,39,86,121,150` | Append-only guard covers ProvisioningAuditEvent only. Current models contain no context/operation ledger. Existing audit aggregate check requires a Device or ProductionLine; do not force context facts into that unrelated audit contract. |
| `api/JaxPower.Ipqc.Api/Endpoints/LinePatrolEndpoints.cs` | Personal IPQA line/task reads only. GET has no issuance side effect. Add explicit context POSTs in this namespace, not WorkContext issuance on a paginated list or new anonymous Bootstrap. |
| `api/JaxPower.Ipqc.Api/Program.cs:160` | no-store covers `/api/auth`, `/api/line-patrol`, `/api/line-patrol-plans`. Proposed `/api/admin/work-contexts` needs explicit early no-store coverage, including 401/403/success responses. JWT/role checks precede personal metadata/body binding. |
| `api/JaxPower.Ipqc.Api/Services/PatrolTaskExecutionService.cs:143,196,216` | Legacy writes enforce version1 at the database claim; ownership precedes unsupported-workflow conflict. WorkContext must not bypass this or call legacy SaveDraft/Submit for a shared task. |
| `api/JaxPower.Ipqc.Api/Services/PatrolTaskReviewService.cs`, `CompletedPatrolRecordService.cs` | Legacy review/report isolation remains; no contributor-aware shared report exists. Future actual submitter must not be inferred from old AssignedInspectorKey/SubmittedBy display strings. |
| `web/src/services/authSession.ts`, `authFetch.ts` | Existing Web has in-memory JWT, cookie refresh and logout; no offline shared workspace or proof vault. Keep Web behavior unchanged and run its regression gates; do not claim this validates Android logout isolation. |
| `android/enrollment-probe/README.md` | Retired historical procedure only, explicitly no project/APK. There is no implemented encrypted shared-work coordinator to test or extend in this PR. |
| `api/JaxPower.Ipqc.Api.Tests/PersonalSessionFixture.cs:96`, `LineSharedPlanningFixture.cs` | Reusable relational factories, synthetic actors/clock, fault interceptors, real generated shared tasks and isolated PostgreSQL databases. New issuance fixtures must use actual admitted generated tasks, not bless historical task shells. |
| `.github/workflows/ci.yml` | PostgreSQL17 service runs only PersonalSessionPostgresTests and LineSharedPlanningPostgresTests. Explicitly include WorkContextPostgresTests; adding tests without updating the filter would silently skip the live gate. |

Repository inspection found no WorkContext, contribution OperationId ledger, accepted-operation receipt or provenance API. Existing DeviceCredential IssuanceOperationId and candidate audit are unrelated and must not be reused. No deployment DB was inspected: shared-row inventory in production remains an external gate, not a confirmed zero count.

## 4. F1 authority and identity matrix

| Situation | Current requester | Contributor/source | Uploader | Formal submitter / permitted outcome |
| --- | --- | --- | --- | --- |
| Issue a capture context | Online eligible A, current family | Server sets A, never body-selected | Not an inspection upload | None; context cannot execute/Submit |
| B starts new work later | Online eligible B obtains B context | B | B if uploaded by B | Future explicit submitter recorded separately |
| A ordinary logout, family/capture expiry; A remains active IPQA | Current authorized B | A resolved from saved proof/context | B | Historical source preflight may succeed; no A token required; no business acceptance in R32-04 |
| A normal self-service password change | Current authorized B | A; old AuthVersion is historical, not a blanket rejection | B | Same historical-source rule; A must reauthenticate for new A requests |
| A deactivated, loses IPQA role, Admin-reset or explicit source revocation | Current authorized B | A context remains A, security-revoked | B | Unaccepted dependencies require quarantine/re-performance; no new acceptance under A |
| Valid A facts accepted before security action | Current authorized B | A and original server acceptance retained | Original acceptance records its uploader | Later B may use unchanged applicable accepted facts in B's own new explicit Submit; no A proof/login requirement for those references |
| A's formally submitted record | Current authorized reader | Original contributors retained | Historical uploader retained | Original submitter/time remain immutable; revocation is not a PQE rejection |
| Receipt/acknowledgement lost | B with current task-read authority | Determine from authoritative operation receipt, not local acknowledgement | Preserve original accepted uploader | NotFound is provisional while an earlier request can commit; no relabel/replay as new work |
| B missing current authority | Nobody may upload through B | Source proof is irrelevant to current access | No upload | Deny; retain local work |
| B tries A's frozen Submit | B | May contain valid A contributions, but intent belongs to A | Not authority to impersonate A | Query only; B must create B's own intent in the later F2 phase |

WorkContext proves only that the server issued a scoped capability while A was authenticated and that a later presenter possesses its proof. It **does not independently prove the physical offline operator, exact capture time, tablet identity or per-edit authorship**. This limitation is approved, not an unresolved architecture question. A compromised holder can fabricate within scope. Do not restore device PKI, per-user signing, attestation or trusted-clock requirements.

## 5. Proposed WorkContext database contract

### 5.1 One additive migration; two new tables

Proposed logical migration name: `R3204WorkContextFoundation`. EF assigns its timestamp only during separately authorized implementation; no migration is generated by this audit.

**WorkContexts**:

| Field | Contract |
| --- | --- |
| Id | Server-generated nonempty GUID, primary key |
| ContractVersion | 1, explicit check; no interpretation of unknown versions |
| ProofSecretHash | SHA-256 of random 32-byte proof, canonical 64-character hex; hash only, never a stored recoverable raw proof |
| EnvironmentId | Immutable valid PersonalAccess environment, bounded 200 characters |
| UserId | Immutable owner, restrictive FK to ApplicationUsers; display names are not relationships |
| IssuedPersonalSessionId | Immutable stable family GUID verified at issue; historical scalar, not FK to a rotating refresh row or current-family filtered index |
| IssuedAuthVersion | Audit snapshot only; not a historical eligibility gate |
| TaskId / LineId / WorkflowVersion | Immutable actual Task scope, version2; restrictive Task and Line FKs; service validates their joint identity under lock |
| IssuedAtUtc / CaptureUntilUtc / PersonalSessionExpiresAtUtc | Server UTC; issued < capture deadline; deadline <= min(issue+72h, original family expiry); never extended in place |
| SecurityRevokedAtUtc / SecurityRevocationReason / SecurityRevokedByUserId | Initially null; set together once, stable actor/reason; restrictive actor FK; reactivation/refresh cannot clear them |
| Revision | Positive long, starts1, CAS concurrency token, checked increment on first security revocation |

Use GUID/uuid, UTC timestamp/timestamp with time zone and bigint on PostgreSQL, corresponding SQLite provider types. Use provider-neutral model normalization already established in the repository. Index `(UserId, SecurityRevokedAtUtc)`, `(TaskId, UserId)` and `(EnvironmentId, TaskId)` for security and provenance lookup. No uniqueness rule limiting an owner to one historical context: expiry/renewed online capture can create another immutable context. No new default contexts/backfill or device fields.

Restrictive FKs do not themselves establish Task/Line/workflow consistency. The service copies and rechecks actual task identity under the transaction and protects immutable context fields on tracked updates; no public API can retarget them. Direct privileged database tampering is not claimed to be prevented by the application guard. Tests must demonstrate corruption fails validation.

**WorkContextAuditEvents**:

- Server Id, correlated ChangeId, WorkContextId, Action, server OccurredAtUtc, ActorType, stable ActorUserId and username snapshot, affected source UserId, reason, non-secret before/after snapshots.
- Actions cover ContextIssued and first ContextSecurityRevoked, including account-security cause. Issuance actor is the authenticated owner; explicit/account-security actor is the authenticated Admin. System is reserved for a genuinely audited future maintenance action, never fabricated for a user action. No Device/ApplicantKey actor.
- Restrictive context/user FKs, indexes on `(WorkContextId, OccurredAtUtc, Id)` and ChangeId. Stable ordering and correct totals for paginated history.
- SaveChanges append-only guard and service encapsulation, like existing audit; no application update/delete/bulk-mutation endpoint. No proof secret/hash, password, JWT, refresh token or arbitrary body dump in audit. A successful preflight is not a captured/accepted-work audit event.
- State and corresponding audit commit together. Failure of audit insert must roll back issuance or account/password/session/context changes. Do not begin a second transaction from a security hook.

No modification to old migrations, Task columns, formal submission schema, device/enrollment tables or ProductionLine constraints is expected. A new FK from a new table to an existing PK does not require adding a FK column to the old table. Review generated SQLite SQL: this proposal should create tables/indexes without a new old-table rebuild. Full fresh-database history still includes R32-03's existing rebuild; do not claim the entire historical migration chain is crash-atomic.

### 5.2 Proof delivery and capture lifetime

Use `RandomNumberGenerator.GetBytes(32)` and canonical unpadded Base64url (43 characters) for the raw proof; reject malformed/oversized input before hashing and compare fixed-size hashes in constant time. Proof is returned only by successful owner issuance over authenticated HTTPS, after commit. GET/history never returns it. Never accept it as a bearer access/refresh credential or place it in URLs.

An explicit issuance computes a fresh PersonalAuthority under lock and returns the applicable ordinary OfflineAccessReceipt together with the context. CaptureUntilUtc equals that response's bounded offline deadline. Never trust a client-submitted receipt/expiry. This is fresh online verification, not an extension caused by local Save, opening a screen or network failure. Older contexts retain their deadlines.

Simplest recoverable delivery: issue a fresh context for each explicit issuance request. There is no promise of idempotently redelivering an unrecoverable plaintext proof from a hash. If a response is lost, the client may explicitly request a new context; never invent the missing proof or attach work to an unseen context. Preserve any context/proof already referenced by saved work. Bound issuance rate; do not introduce automatic context TTL deletion or a hidden single-current-context constraint that invalidates historical work.

## 6. Proposed API contracts and authorization

New endpoints only; existing auth, line/task read and legacy JSON behavior remain unchanged. All input DTOs reject unknown fields. No request can supply an authoritative contributor, uploader or submitter.

| Route | Request / response | Authority and effects |
| --- | --- | --- |
| POST `/api/line-patrol/tasks/{taskId}/work-contexts` | `{expectedTaskLineId, expectedWorkflowVersion:2, expectedEnvironmentId}` -> 201 `{context, proofSecret, offlineAccessReceipt, canExecute:false}` | Current eligible IPQA owner only. Copy identity from actual Task/user/environment. Context+audit only; no task/draft mutation. No client UserId or selected-line authority. |
| GET `/api/line-patrol/tasks/{taskId}/work-contexts/{contextId}` | Non-secret context metadata; no hash/raw proof | Current task-read IPQA authority, actual task/context scope. B may inspect scoped provenance metadata without possessing A's token; metadata alone grants no provenance. |
| POST `/api/line-patrol/tasks/{taskId}/work-contexts/validate-sources` | `{expectedTaskLineId, expectedWorkflowVersion:2, expectedEnvironmentId, sources:[{workContextId, proofSecret, contributorUserIdAssertion}]}` -> `{validatedAtUtc, purpose:"SourcePreflightOnly", sources:[{workContextId, contributorUserId, captureUntilUtc}], acceptsWork:false}` | B current authority plus A proof/current historical-source eligibility, independently resolved. Read-only preflight, all-or-nothing, no accepted operation/draft/evidence/receipt. It does not guarantee eligibility of a later write. |
| GET `/api/admin/work-contexts/{contextId}` | Non-secret metadata | Existing UsersRead permission AND current personal family; Admin only, not a new shared-task-result permission. |
| GET `/api/admin/work-contexts/{contextId}/audit?page=1&pageSize=20` | Stable time/Id order, total and page, max100 | Same Admin authority; no proof or content dumps. |
| POST `/api/admin/work-contexts/{contextId}/security-revoke` | `{expectedRevision, reason}` -> non-secret current metadata | UsersWrite AND current family, required bounded reason; sticky CAS + audit. Can revoke when task/line is inactive or directory configuration unavailable. No task/draft/history rewrite. |

For first issuance/preflight require coherent admitted `PlanningDefinitionV1`, clean foundation state (`PendingInspection`/`InProgress`, revision0, no legacy answers/submission/review), actual configured active line and frozen definition integrity. No new inference from a mutable Plan/Standard. A Plan being subsequently disabled does not erase an existing Task; current task/line eligibility is authoritative. A line switch preference is ignored. Existing historical reads retain their original behavior.

A context proof is reusable historical provenance, not a single-use challenge. Replaying it does not itself accept work or create operation receipts. Future work replay protection belongs to stable operation IDs/content hashes and transactional draft versions (§9). Do not falsely describe a reusable bearer source proof as replay-resistant per-edit signing.

Error/ordering contract:

1. Existing authentication/current-account and role/forced-change checks first: 401 or403, before body/query binding.
2. Current family/environment: existing401/503 codes. Recheck mutation authority after acquiring locks; route middleware is not the final check.
3. Input400; missing/legacy/out-of-config task404; expected task line/workflow/environment mismatch409; invalid/unadmitted snapshot or unsupported state409. No repair/conversion.
4. Unknown/bad/mismatched source proof or asserted owner ->409 `SourceProvenanceConflict`; do not disclose unrelated user/context details. After a valid scoped proof, revoked/ineligible historical source ->409 `SourceSecurityRevoked` with source IDs requiring quarantine, never a guessed contributor.
5. Any source conflict rejects the entire preflight; no partial business acceptance. Context/source endpoints never accept photo bytes, sample payloads, arbitrary accepted-operation claims or inspection-attempt IDs whose model does not exist yet.
6. Stale context revision409 `WorkContextConflict`; missing context404 after authorization. An already-revoked context never changes original reason/time/actor; a current-revision repeat is a no-op without duplicate audit. A lost response is recovered by metadata GET and explicit user action.
7. Bounded transient contention503; never turn a lock timeout into invalid credentials/provenance. Only known rolled-back conflicts are retried; no blind network/unknown-commit retries.
8. Early `Cache-Control:no-store` on success and auth/error responses for both namespaces. TLS, token/proof redaction, explicit body limits and authenticated-user rate limits. Proposed technical defaults: context issuance10/min/user, source preflight30/min/user, queue0; require1..100 distinct source contexts and32KiB body. Apply the new policies only after current-user/role/family authorization; do not attach a pre-authorization limiter that changes exhausted anonymous/wrong-role/revoked-family responses into429. Test this ordering. Limits are per process, not a distributed-abuse guarantee; keep proxy controls an IT gate.

## 7. Atomicity, security changes and provider protocol

### 7.1 Account action classification

| Existing action | Current authentication | Historical context action |
| --- | --- | --- |
| Refresh / ordinary expiry / logout / owner prior-family close | Existing family rules unchanged | No security-revoke; no deadline extension or deletion |
| Normal self-service password change | Existing AuthVersion/all-session revocation | No historical-source revocation solely for that change |
| Account deactivation / transition out of IPQA eligibility | Existing role/activity guards and revocation | Revoke every still-unrevoked affected context atomically; keep accepted-history references |
| Admin security password reset | Existing password/forced-change/version/token rules | Revoke affected contexts atomically even if no issued token is currently live |
| Explicit Admin source-context security revoke | Current Admin authority | Revoke target only with reason/CAS/audit |
| Reactivation / return to IPQA / unlock / display change | Existing current-auth policy | No revival of an old context; a fresh eligible login may issue a new one |

Do not call the hook from generic RevokeSessionsAsync, because logout/self-service password change and security reset have different provenance meanings. Hook ChangeAsync's actual eligibility loss and ResetPasswordAsync explicitly. Already rejected user actions, final-Admin/assignment conflicts and transaction failures must not revoke contexts or append success audit.

### 7.2 Transaction ordering

Propose a small `WorkContextStore`, not an overhaul of authentication/planning stores:

- SQLite: clear pre-lock tracking, reserve the writer with a non-deferred transaction before authoritative user/family/context/task reads. No PostgreSQL lock SQL on SQLite. Include a source-only preflight in the same bounded consistent transaction so security mutation has a clear ordering; it still accepts no work.
- PostgreSQL: resolve candidate immutable source-owner IDs only to discover the lock set, then lock the current requester and distinct source owners in sorted GUID order with `FOR NO KEY UPDATE`. Re-read authoritative facts after locks; discovery is never authorization. This conflicts with existing account-security `FOR UPDATE`, while remaining compatible with audit FK `KEY SHARE`.
- For issuance/task-bound preflight, lock the two configured line roots in sorted order with `FOR SHARE`, recheck activity/configuration, then target Task `FOR UPDATE`, then affected context rows in sorted Id order as needed. No acquisition of the planning advisory gate after these locks. Preserve existing account -> line -> task order; preflight/issuance performs no Plan mutation.
- Explicit context revoke: sorted requester/source-owner locks -> target context; no requirement that a line remains active or a task is editable. Existing account-security hook already holds the target account lock and updates context rows in that same unit of work; it does not enter WorkContextStore recursively.
- No long-lived locks, outbound calls or secret delivery inside an uncommitted transaction. Reuse the known transient classifier and bounded three-attempt/5s-wait convention; fix any timeout-zero handling locally in the new store rather than unrelated refactoring.
- Prove both race orders on SQLite and real PostgreSQL: issue versus reset/deactivate/role loss, source preflight versus revocation, current B refresh/logout/security closure, line activity versus issuance, repeated revoke, audit-insert/precommit failures. An issuance before security mutation is subsequently revoked; security-first denies issuance. A preflight before revocation can return an observation, but cannot authorize a later acceptance after revocation.

This is a proposed lock design, **NOT RUN**, not evidence that an unimplemented protocol is deadlock-free. Source references for provider behavior: [PostgreSQL17 row-lock compatibility and lock ordering](https://www.postgresql.org/docs/17/explicit-locking.html), [SQLite transaction/writer behavior](https://www.sqlite.org/lang_transaction.html). Every future actual batch acceptance must participate in the same account/security ordering, then Task CAS and ledger/audit commit, rather than trusting an earlier preflight.

## 8. Shared-tablet protection and offline reconciliation contract

No Android implementation exists in this repository. The following is a required future-client boundary, not a tested Web feature:

- Personal vault: A's refresh ciphertext, personal offline receipt/access markers and vault key are independent from shared work. JWT stays in memory; no stored password.
- Shared workspace key: independent random app-private Keystore-protected work key, not derived from A's password/token/session or A's personal key. Organizational key remains Environment + InstallationWorkspace + actual Line + Task. Installation identity is not server authority.
- On A logout: stop A's workers, lock personal access and remove A reusable credentials/key; preserve encrypted drafts/files/proofs/manifests/intents. Keep only non-secret pending-family-close bookkeeping. B's token cannot close A's family as A; use existing owner-only recovery/natural expiry/account-security path.
- B login/switch is online. After B's current task authority succeeds, the controlled coordinator may decrypt/forward queued A proofs without showing or exporting them. B never acquires A refresh/access token. New B edits use B context and new operation identities, never A's selectable name.
- CaptureUntil limits ordinary new offline editing; expiry/logout alone does not destroy saved historical proof or work. The server cannot prove client-reported capture time. Online historical-source checks distinguish normal expiry from sticky security revocation; do not invent a trusted per-sample clock.
- A disabled before unaccepted work: quarantine dependencies, retain originals/bytes, B re-performs affected work with new B context/IDs. A disabled after server acceptance: accepted facts remain A and can support B's later own Submit when still applicable. Formal history is immutable.
- Resolve unacknowledged work by authoritative stable operation receipts once implemented. A local absence of acknowledgement and one NotFound are insufficient to classify an in-flight operation as never accepted. A preflight response is not that receipt.
- Two offline tablets cannot see each other's unsynchronized edits. Future server CAS has one winner; stale409 preserves accepted server state and the losing installation's pending records/files/proofs. Explicit reapply with changed payloads uses new IDs and preserved original conflict records.
- Preserve work across expiry, process death/reboot, line switching, failed sync and signed in-place upgrades. Do not promise recovery after uninstall/factory reset/key loss. An expired personal receipt locks access; it never deletes shared work.

## 9. Deferred draft/operation, F2 and F3 contracts

These contracts prevent scope ambiguity; their tables/routes are **not** in the first R32-04 migration or implementation prompt.

| Later component | Required integration contract |
| --- | --- |
| Shared draft | Separate mutable DraftVersion from LocalEditVersion and CurrentRevisionNo. ExpectedVersion CAS applies to persisted Task/draft and eligibility; every accepted operation batch advances the relevant server version atomically. No use of SharedPlanRevision as a draft version. |
| Contribution operation ledger | Persist immutable TaskId/OperationId, item/real inspection-attempt scope, source WorkContext/derived ContributorUserId, actual UploadedByUserId/current family, versioned canonical hash, dependencies, accepted UTC and resulting draft version/receipt. Unique `(TaskId, OperationId)` prevents changing source/payload under the same ID. Same identity/hash replay returns original receipt; changed content/source conflicts. |
| Accepted-reference validation | Resolve accepted operation IDs, hashes, task/item/attempt applicability and dependencies from server records. Never accept a client flag saying accepted. Accepted facts do not require old raw proof/current A eligibility again; new unaccepted sources do. |
| Atomicity/quarantine | Source checks, Task version claim, entire operation batch, materialized draft, audit and receipt commit/rollback together. A mixed invalid batch never removes previously accepted facts. Quarantined unaccepted A data cannot be hidden in B full-draft replacement. No body-only generic JSON draft endpoint. |
| Receipt query | Current B task-read authority can recover original operation outcome without A login. Unknown/in-flight is preserved until transaction/version ordering resolves it; never mint a speculative next revision. |
| Inspection attempts | R32-05 defines real attempt identity/sample/disposition validation. A StandardItemId is not a TaskItemId or attempt. Do not fabricate attempt1 in R32-04 and later reinterpret history. Old accepted results cannot satisfy a new required reinspection. |
| F2 | Freeze actual submitter/origin personal family/submitter context, real Task/Line/workflow, base revision, expected draft version, versioned hash and exact source/evidence manifest. Current transport auth remains separate. No context proof/JWT in canonical hash. Retain all six query-only A/B handoff invariants; only original actor may POST a sendable intent, B creates a new one, one base-state winner. |
| F3 | Source/dependency manifest validation before unaccepted photo bytes; actual Ready/storage/reference locks and retention transaction required. WorkContext/proof/preflight is not Ready, a file reference or permission to bypass quarantine. Formal reference lifetime and independent restore/tombstone protocol remain unchanged. |

A's accepted contribution, B's upload and C's formal Submit are distinct facts. Future reports use actual stable actor IDs and frozen names, not the current login or a legacy assignee label. This PR creates no synthetic submitted record to test this future business path.

## 10. Exact proposed implementation file inventory

**New application files**:

- `api/JaxPower.Ipqc.Api/Domain/WorkContext.cs`: context and typed audit entities.
- `api/JaxPower.Ipqc.Api/Data/WorkContextModel.cs`: focused partial EF configuration.
- `api/JaxPower.Ipqc.Api/Contracts/WorkContextContracts.cs`: strict issuance, metadata, preflight and revoke DTOs; secret-bearing request/response types never reused as audit DTOs.
- `api/JaxPower.Ipqc.Api/Endpoints/WorkContextEndpoints.cs`: six routes from §6 and explicit permissions/current-family metadata.
- `api/JaxPower.Ipqc.Api/Services/WorkContextStore.cs`: deterministic multi-user/provider transaction protocol.
- `api/JaxPower.Ipqc.Api/Services/WorkContextService.cs`: issuance, non-secret reads, scoped source preflight, explicit revoke/history.
- `api/JaxPower.Ipqc.Api/Services/WorkContextSecurity.cs`: transaction-local account-security invalidation/audit; never opens/nests a transaction.
- `api/JaxPower.Ipqc.Api/Services/LineSharedTaskEligibility.cs`: extract existing clean-foundation predicate narrowly for reuse; no execution gate relaxation.
- `api/JaxPower.Ipqc.Api/Data/Migrations/<implementation-timestamp>_R3204WorkContextFoundation.cs` and matching `.Designer.cs`: one future additive migration; timestamp deliberately not invented now.

**Existing application files to change**:

- `api/JaxPower.Ipqc.Api/Data/IpqcDbContext.cs`: DbSets/model hook, context immutable-field and audit append-only guards.
- `api/JaxPower.Ipqc.Api/Data/Migrations/IpqcDbContextModelSnapshot.cs`: generated additive model only.
- `api/JaxPower.Ipqc.Api/Services/ApplicationUserService.cs`: classified atomic revocation hooks and stable reset actor parameter.
- `api/JaxPower.Ipqc.Api/Endpoints/UserEndpoints.cs`: pass authenticated stable actor Id for reset; retain current route RBAC/JSON.
- `api/JaxPower.Ipqc.Api/Services/LinePatrolReadService.cs`: delegate foundation predicate, preserving DTO/read semantics and canExecute=false.
- `api/JaxPower.Ipqc.Api/Program.cs`: DI/routes, bounded endpoint/body policies, early Admin-context no-store. No new transport/listener or anonymous registration.

**New tests**:

- `api/JaxPower.Ipqc.Api.Tests/WorkContextFixture.cs`
- `api/JaxPower.Ipqc.Api.Tests/WorkContextTests.cs`
- `api/JaxPower.Ipqc.Api.Tests/WorkContextSecurityTests.cs`
- `api/JaxPower.Ipqc.Api.Tests/WorkContextConcurrencyTests.cs`
- `api/JaxPower.Ipqc.Api.Tests/WorkContextMigrationTests.cs`
- `api/JaxPower.Ipqc.Api.Tests/WorkContextPostgresTests.cs`

**Existing test/CI/docs touchpoints**:

- `api/JaxPower.Ipqc.Api.Tests/PersonalSessionFixture.cs`: only reusable provider/fault seams if required; prefer new context fixture composition.
- `api/JaxPower.Ipqc.Api.Tests/PersonalSessionConcurrencyTests.cs`: extend account-security rollback assertions to context facts; retain existing assertions.
- `.github/workflows/ci.yml`: add WorkContextPostgresTests to the real PostgreSQL filter; retain both existing live suites.
- `README.md`, `docs/11_POSTGRES_BACKUP_RESTORE.md`, proposed `docs/18_R3204_WORK_CONTEXT.md`: implemented boundaries, context recovery and actual results after implementation.

Expected untouched: `LocalAuthService.cs`, `PersonalSessionStore.cs`, `PersonalAccess.cs`, `IpqcPermissions.cs`, legacy Task execution/review/report services, scheduler/planning/provisioning/enrollment services, Domain/PatrolPlan.cs, existing migrations, Web source and Android. Existing policies suffice: IPQA LinePatrolRead plus current family for foundation context routes, Admin UsersRead/Write plus current family for context management. Any necessary deviation must be justified in delivery, not silently expand into shared execution.

Only this report is changed by the current documentation task. This proposed inventory is not an actual changed-file manifest.

## 11. Migration, compatibility and recovery assessment

There are ten existing EF Up migrations through R3203. Preserve them byte-for-byte, including provider-neutral normalization and all historical device/credential/session/audit obligations. Proposed Up creates only the two new tables/indexes/checks/restrictive FKs; no bulk updates, inferred contributors, family backfill, Task conversion or old data deletion. Fresh/populated SQLite and actual PostgreSQL tests compare every pre-existing table/column, indexes/FKs and pending enrollment cleanup facts. Include `PRAGMA foreign_key_check`, both EF pending-model checks and native PostgreSQL idempotent upgrade SQL review. No production migration is authorized.

Historical/unadmitted/incompatible LineShared rows remain unavailable for new capture issuance, without rewriting their snapshots. Existing LegacyPersonalV1 ownership, N/A, workflow guards, reports, auth/logout and Web RBAC stay unchanged. Add no device dependency; retired reservation behavior and R32-01 cleanup remain intact. Current shared list/detail continue canExecute=false, even after obtaining a context.

Application rollback retains new context/audit tables. A binary that does not revoke contexts on account-security actions cannot be advertised as safe for new context/provenance ingress: disable that ingress and future shared acceptance during rollback. Down must refuse destructive context/audit removal; use a reviewed forward repair or verified backup.

Restore: close personal/provenance/business ingress and relevant workers first; reconcile account security, restored refresh authority and source-context revocations. Rotate/reconcile JWT trust under the existing runbook. If context-security history cannot be established, conservatively security-invalidate affected restored contexts with truthful maintenance audit before accepting new work; keep proofs/bytes quarantined and accepted history intact. Do not merely delete rows, rename EnvironmentId, revive revoked contexts or issue device epochs. Future receipt/Task/version rollback and F3 deletion recovery need their own gates before actual writes/images reopen.

Provider references: [EF SQLite migration limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations), [ASP.NET rate-limit policies and testing](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0). Generated SQL/model checks are not live PostgreSQL execution; a rate limiter is not device identity or a complete denial-of-service defense.

## 12. Focused acceptance / test matrix

**40 proposed cases total: 32 first-PR foundation cases + 8 explicitly deferred integration cases. Every case is NOT RUN.** Existing R32-03 CI does not execute these unimplemented tests. Case counts are requirements, not promised xUnit/Vitest test counts.

| ID | Scope and acceptance | Status |
| --- | --- | --- |
| R04-01 | Eligible A obtains owner-scoped context for actual admitted task on either configured line; no registry/device needed | NOT RUN |
| R04-02 | Anonymous/old JWT/revoked or foreign family/auth-version mismatch/forced-change/wrong role denied in normal401/403 order before malformed body | NOT RUN |
| R04-03 | Missing/legacy/out-of-config task404; line/workflow/environment consistency mismatch409; no retargeting | NOT RUN |
| R04-04 | Invalid/unadmitted snapshot, unsupported state/history/legacy answers receive no context; no snapshot reconstruction or Task mutation | NOT RUN |
| R04-05 | Random32-byte proof, strict Base64url/fixed-time hash validation; only hash in DB; no proof/hash in metadata/audit/logs/URLs | NOT RUN |
| R04-06 | Server-derived receipt/capture cap72h and family expiry; exact expiry boundary; no trusted client deadline | NOT RUN |
| R04-07 | Fresh online context leaves old deadline/identity unchanged; lost issuance response safely requests another context without phantom proof recovery | NOT RUN |
| R04-08 | A cannot issue B context or body-select a contributor; B's new capture context is B | NOT RUN |
| R04-09 | B preflights saved A proof after normal A capture/family expiry/logout/self-password change; contributor A and requester B remain distinct | NOT RUN |
| R04-10 | Deactivation, IPQA eligibility loss, Admin reset and explicit revoke invalidate affected sources atomically, with distinct cause/actor | NOT RUN |
| R04-11 | Reactivation/return-to-IPQA/unlock/display edit never revive old security-revoked contexts; fresh eligible context allowed | NOT RUN |
| R04-12 | Revoked/expired/forced-change B cannot transport using A proof or cached receipt; normal current family required | NOT RUN |
| R04-13 | Unknown/wrong/malformed proof, forged owner, unsupported contract version, tampered immutable context facts reject without actor inference | NOT RUN |
| R04-14 | Cross-task/line/environment use and fake workspace/DeviceId/IP claims fail; screen line preference grants no authority | NOT RUN |
| R04-15 | Mixed valid/invalid source preflight is all-or-nothing and read-only; no operations, acceptance receipt, draft/task/history changes | NOT RUN |
| R04-16 | Strict DTOs reject duplicate/oversized source lists, excessive body and invented accepted refs/attempts/business samples; precise bounded errors | NOT RUN |
| R04-17 | Rate-limit429, body limits and no-store on successful/auth/error responses; permission/auth status ordering remains correct | NOT RUN |
| R04-18 | Admin revoke expectedRevision CAS, repeated/current no-op and lost-response metadata recovery; one original cause/audit, no state revival | NOT RUN |
| R04-19 | Metadata/audit RBAC, non-secret output, stable actor IDs, pagination/order/totals, append-only and restrictive history FKs | NOT RUN |
| R04-20 | Context issuance/read/preflight/revoke leave Task/frozen JSON/parent identity/results/status/CurrentRevisionNo untouched, canExecute=false | NOT RUN |
| R04-21 | Capture expiry does not delete contexts/proofs/history or make normal historical source automatically invalid | NOT RUN |
| R04-22 | Issue versus reset/deactivation/role loss, both orders and paused in-flight transaction: no post-security live context | NOT RUN |
| R04-23 | A/B source preflight versus explicit/account revocation, both orders; earlier preflight is not a later write capability | NOT RUN |
| R04-24 | Current uploader rotation/logout/security race: no committed refresh gap, fresh post-closure validation denied, no source misclassification | NOT RUN |
| R04-25 | Line activity versus issuance in both orders; inactive/misconfigured task cannot issue/preflight, Admin source revoke still works | NOT RUN |
| R04-26 | Audit insertion failure rolls back account role/activity/reset password/AuthVersion/tokens and context revocations together | NOT RUN |
| R04-27 | Issuance/context/audit/precommit failure rolls back graph; no success/raw proof before commit; retry has truthful result | NOT RUN |
| R04-28 | Provider lock ordering, contention timeout, rollback-safe retries/exhaustion503 and no blind unknown-commit retry | NOT RUN |
| R04-29 | Full legacy auth/ownership/scheduler/review/HTML-XLSX, R32-01 retired routes/drain and R32-02/03 tests unchanged | NOT RUN |
| R04-30 | Fresh + populated latest/historical SQLite upgrades preserve all old columns/FKs/indexes, no inferred sources, clean foreign_key_check | NOT RUN |
| R04-31 | Both provider models clean; PostgreSQL native additive SQL; live fresh/populated constraints; protective Down and rollback/restore contract | NOT RUN |
| R04-32 | Release/full backend, new live WorkContextPostgresTests plus existing16 live cases, full Web lint/tests/typecheck/build, whitespace/secrets/CI; exact PASS/FAIL/SKIP evidence | NOT RUN |
| R04-D01 | Later actual accepted A facts survive source deactivation; B's own Submit can carry unchanged applicable references; immutable history preserved | NOT RUN — deferred business acceptance/F2 |
| R04-D02 | Stable operation same-hash replay, changed-content/source conflict, lost/in-flight receipt and revocation/acceptance races; no guessed accepted flag | NOT RUN — deferred operation ledger |
| R04-D03 | Two offline tablets sequential/simultaneous reconnect: one DraftVersion winner, stale409 preserves both sides, explicit reapply/new IDs | NOT RUN — deferred shared draft/client |
| R04-D04 | Delete A personal key/tokens at logout; B online login/restart/in-place upgrade still decrypts shared A proof/work using independent key, never A tokens | NOT RUN — deferred Android SEC-02 |
| R04-D05 | Full F2 frozen intents/query-only takeover, late A acceptance, same-user reauthentication and one base-state winner | NOT RUN — deferred F2 |
| R04-D06 | Source manifest before photo bytes; Ready/ref/delete/restore locks and five-year reference preservation | NOT RUN — deferred F3 |
| R04-D07 | Real inspection attempts and dependency graph: accepted old facts cannot substitute for required new work/evidence | NOT RUN — deferred inspection model |
| R04-D08 | IT authority/backup restore drill, HTTPS/PAD secure storage/reboot/UAT on both actual models and signed APK upgrade | NOT RUN — external/client gates |

Foundation tests may prove source resolution/revocation in isolation. They must not label D01/D02/D03 passed by fabricating a business-accepted metadata row or treating preflight as Submit.

## 13. Execution sequence, evidence and external gates

After separate implementation authorization:

1. Verify current origin/main, preserve local unrelated work and create one `codex/` feature branch. Re-read this report and approved requirements; do not reopen sign-off.
2. Commit context/audit models and the single additive migration, with fresh/populated/provider tests.
3. Commit strict context routes, transaction/store/security hooks and source preflight, with deterministic fault/race/RBAC/redaction tests. No production activation.
4. Commit contract/runbook evidence and CI filter; verify all existing backend and Web gates plus real PostgreSQL. Keep changes reviewable; no Android/UI expansion.
5. Open one review PR, report exact HEAD/files/commands and PASS/FAIL/SKIP. Stop before merge/deployment or another phase.

Required commands/gates: `dotnet restore api/JaxPower.Ipqc.slnx`; Release build and complete tests; focused WorkContext/security/session tests; new and existing real disposable PostgreSQL classes; `dotnet tool restore` then both-provider EF pending-model checks and PostgreSQL idempotent migration script review; Web `npm ci`, `npm run lint`, `npm run test`, `npm run typecheck`, `npm run build`; Git whitespace/scoped secret checks and exact-HEAD GitHub CI. Generate SQL/TRX only outside Git. Live test fixture opt-in must validate a disposable `ipqc_test*` target; unavailable live execution is SKIPPED, never simulated PASS.

At this audit baseline, existing merge CI is confirmed successful. This is **R32-03 regression evidence**, not R32-04 acceptance: backend312 PASS/0 FAIL/16 opt-in SKIP; separate real PostgreSQL16 PASS/0 FAIL/0 SKIP; Web90 PASS/0 FAIL/0 SKIP; Release/lint/typecheck/build successful. Historical ledger counts in earlier signed/implemented documents remain unchanged.

External gates do not block foundation implementation but do block production/UAT claims:

- IT supplies `<IPQC_TEST_HTTPS_ORIGIN>`, real DNS/FQDN/port, valid server chain/hostname trust, proxy forwarding policy/firewall/time/environment and two actual line IDs. No endpoint, CA or trust status is invented. Factory Wi-Fi without public Internet is acceptable if intended HTTPS works.
- Both SM-X210/API35 and SM-T500/API30 require real app storage, offline/session/logout/reboot/conflict/signed-upgrade checks later. SM-T500's reported2022-04 patch requires IT security/update assessment. No device enrollment/mTLS prerequisite.
- QA/Peter exercises A-to-B attribution and accepted/unaccepted security-action categories with synthetic accounts; explicitly retains the offline-authorship limitation. Inspect actual shared-row inventory before any future execution rollout.
- IT/DB owner rehearses controlled migration/backup/restore, account/context security reconciliation and accepted-receipt recovery. No live production DB inspection, restore rehearsal or cleanup is claimed by this audit.
- Existing bundle/dependency advisories and legacy cached-scheduling race remain separate follow-ups, not reasons to enlarge this PR.

No unresolved architecture decision blocks the proposed foundation. Draft/operation/F2/F3/Android dependencies are deliberately unimplemented, with explicit NOT RUN cases, not silently waived requirements.

## 14. Ready-to-use implementation authorization prompt

The following text is a prompt for Peter to issue separately. Its presence in this report is **not** application implementation authorization.

```text
Peter authorizes implementation of the first R32-04 WorkContext/F1 foundation PR described in:
docs/architecture/IPQC_R32-04_IMPLEMENTATION_READINESS_REPORT_2026-11-11.md

Repository: peterl-ai/IPQC-system.
Application audit baseline: main merge385babfc937276fc8c167527ae2631a32542bf46.
First fetch current origin/main, verify the published report and any later documentation-only commits, inspect/preserve unrelated local work, and create one codex/ feature branch from verified current main. Unexpected application changes must be reconciled with the audit; do not blindly use an old checkout.

Approved Rev3.2 Architecture and Handoff Supersession remain authoritative. Do not repeat architecture sign-off or restore device enrollment/mTLS/hardware binding. Explicitly retain the accepted limitation: WorkContext does not independently prove the physical operator or time of each offline edit.

Implement only the report's first-PR foundation scope and R04-01..R04-32:
- Task-scoped owner-only online WorkContext issuance with32-byte opaque proof, hash-only persistence, immutable environment/user/origin family/Task/actual Line/workflow and receipt-bounded capture deadline.
- Strict no-store metadata/source-preflight/Admin-revoke/audit routes and error/RBAC contracts in report section6. Preflight accepts no inspection work, draft, evidence or operation receipt and cannot authorize a later mutation.
- Current personal-family validation inside provider-specific transactions, deterministic multi-account/line/task/context lock order, bounded rollback-safe retries and truthful audit.
- Atomic sticky context-security revocation for deactivation, IPQA eligibility loss, Admin reset and explicit Admin revoke. Normal refresh/logout/expiry/self-service password change do not security-revoke historical contexts; reactivation never revives revoked ones.
- One additive SQLite/PostgreSQL migration for WorkContexts and append-only WorkContextAuditEvents only, restrictive history FKs, no historical backfill, model normalization and no rewritten old migrations.
- Failure/race/rollback/direct-service/RBAC/proof-redaction tests, real PostgreSQL CI including its new filter, compatibility checks and implemented contract/runbook evidence.

Retain canExecute=false, existing read DTO semantics, legacy auth/ownership/API/report behavior, Admin/PQE/IPQA boundaries, ProductionLine/provisioning history and R32-01 cleanup. Never treat a WorkContext, offline receipt, installation ID or retired device row as current uploader authentication. Contributor is server-resolved; uploader and later submitter are separate actual actors. Preserve the three accepted/unaccepted/formal-history revocation categories and independent personal/shared-work storage contract.

Do not implement shared draft/sample/result writes, DraftVersion/accepted-operation ledger, fake acceptance receipts, new inspection attempts/states, PIC/forms, Android/APK/client storage, F2 Submit/takeover, F3 attachments/retention or production operations. R04-D01..D08 stay NOT RUN until separately implemented; document dependencies rather than fake their evidence. Do not start the R32-04 follow-up or R32-05 automatically.

Use the exact file inventory in section10; justify any necessary bounded deviation. Do not refactor unrelated auth/planning/provisioning services. Implement in reviewable commits; no secrets or raw employee/production fixtures.

Run focused and complete backend Release builds/tests, fresh/populated SQLite migrations, both EF model checks, PostgreSQL native SQL review, real disposable PostgreSQL new and existing concurrency suites, full Web lint/tests/typecheck/build, whitespace/scoped secret checks and exact-HEAD GitHub CI. Unavailable real PostgreSQL/PAD/IT gates must be SKIPPED/PENDING, not simulated PASS. Report commands and exact counts.

Open one R32-04 foundation PR for review. Return PR URL, branch and final SHA; local/remote sync; exact changed files; migration safety; R04-01..32 evidence with honest unmet gates; source/current-user security boundaries; remaining risks; deferred acceptance dependencies. Do not merge, deploy, run production migrations/cleanup or enable shared execution.
```

Current task stop: report-only publication to main, verified commit-pinned bytes, then wait for separate R32-04 implementation authorization.
