# IPQC Rev3.2 — Implementation Impact and Handoff Supersession Addendum — APPROVED

Date: 2026-11-10, America/New_York.

Status: **APPROVED IMPLEMENTATION HANDOFF TARGET — IMPLEMENTATION PENDING.** Peter formally approved this addendum and its companion [Rev3.2 architecture](IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md) on **2026-11-10**. Mandatory device authentication and the previous P1-03B proposals are cancelled. This approval establishes target contracts; it does not authorize code, migrations, branches, commits, pushes, PRs, merges or deployment.

Final sign-off, Peter, 2026-11-10: all three review clarifications, the API allow/deny direction and all 47 proposed cases are included. Peter explicitly accepts the scoped WorkContext offline-attribution limitation. Acceptance cases have not been executed. Proceed with the authorized R32-01 read-only exact-file audit and implementation prompt, then stop for separate implementation approval.

The original [Implementation Handoff](IPQC_ANDROID_ARCHITECTURE_V1_IMPLEMENTATION_HANDOFF_2026-11-06.md) and [Rev3.1](IPQC_ANDROID_ARCHITECTURE_V1_REV3_1_2026-11-06.md) remain unchanged historical signed-off artifacts. Read this addendum with the complete approved Rev3.2 architecture; it is not a replacement for the unchanged inspection/F2/F3 requirements.

## 1. Baseline, precedence and stop condition

- Verified local branch `main`, local/cached origin/live GitHub main HEAD `d24eed7adc6f7302bacd02182ce5a7417fe15b1e` on 2026-11-10. PR #11 is merged; P1-01/P1-02/P1-03A migrations remain applied history.
- Tracked working tree was clean before this documentation task. Pre-existing untracked walkthrough/handoff/review/prompt files are not part of this work.
- No P1-03B implementation, implementation branch or new PR was created. The earlier P1-03B-Transport / P1-03B-Activation scope proposals are superseded and must not be executed.
- New binding product direction: online personal login, explicit selection of either actual ProductionLine, optional explicit Switch Line; no physical PAD proof or fixed PAD-to-Line authorization.
- These two documents are formally approved architecture/handoff targets as of 2026-11-10. Current deployed/source behavior remains AS-IS until separately approved implementation. Unaffected legacy contracts and signed-off F2/F3 business invariants remain binding.

Historical-file SHA-256 recorded before creating the drafts:

| Historical file | SHA-256 |
| --- | --- |
| IPQC_ANDROID_ARCHITECTURE_V1_REV3_1_2026-11-06.md | C1A1968C5A1A2DC1C3E2BF28CADE56EE39568761DE4D917EE489EB551694C801 |
| IPQC_ANDROID_ARCHITECTURE_V1_IMPLEMENTATION_HANDOFF_2026-11-06.md | 666AE0262E75B51A90D9899E59FE257DF3733841A2628AFD99BB51F5A39537D9 |

Architecture approval is not code completion, test evidence, production trust activation or deployment approval. Complete documentation finalization and the authorized read-only R32-01 readiness audit, then stop before implementation.

## 2. Exact original Handoff supersession matrix

| Original Handoff location | Retain | Replace / interpretation after Rev3.2 sign-off |
| --- | --- | --- |
| Header: target device / status | Historical approval and provenance | Actual two-model deployment inventory; latest business direction replaces physical-PAD prerequisites |
| START HERE items 1–2 | Do not reopen unaffected business/F2/F3 choices | Rev3.2 becomes the revised target after sign-off; old device-authentication direction cannot require P1-03B continuation |
| START HERE items 3–6 | Scoped PR/review/verification, old/new workflow separation, separate deployment approval | P1-01 is already merged; use the revised PR sequence, no required mTLS gate |
| §1.1 sign-off lineage | Historical sign-offs and F2 source | Add Rev3.2 sign-off as a new decision, never rewrite the historical approval chain |
| §1.2 conflict resolution | AS-IS versus TO-BE, old record preservation, automatic retention | Approved Rev3.2 personal-auth/line-selection contracts supersede physical binding; implementation remains separately gated |
| §2 existing baseline | Legacy compatibility, AS-IS verification and separation from target design | Current baseline is merged d24eed7 including P1-01/02/03A; old test counts stay historical |
| Repository rules below §2 | Monorepo, same-origin Web, server authorization, i18n, no MES/Material workflow, history/XLSX | Android uses ordinary HTTPS; no separate Native TLS entry required |
| §3.1 site/users/schedule/offline | Two lines, shared continuation, online new login/switch/Submit, ordinary 72h, no auto-Submit | Replace one fixed registered PAD per line with personally authenticated user selection; user offline receipt replaces device grant |
| §§3.2–3.4 | Screens, forms, dispositions, samples, NG/PQE/PIC/photos/reference rules | Only transport/identity wording changes; no new inspection behavior |
| §3.5 retention/infrastructure | One server/volume, five-year policy, F3/backup, QA/IT responsibilities | Remove device CA/mTLS requirements; retain ordinary server TLS and actual device security assessment |
| §4.1 diagram/components/device identity/execution gate | Kotlin/Room/coordinator/backend/DB/storage choices and truthful roles | Personal session + actual persisted Task/LineId; no registered PAD/certificate/binding; local installation Id is organizational only |
| **§4.2 signed-off F2 addendum, clauses 1–6** | **All six business/concurrency invariants** | “This PAD's coordinator” becomes the installation-local task coordinator, not authenticated device identity; current/persisted actor fields follow §4 below |
| §4.3 F1 bullets 1–3 | Original contributor / uploader / submitter, expiry versus security revocation, quarantine, no historical rewrite | Server-issued personal/task WorkContext and proof, no device-bound context |
| §4.3 F1 bullet 4 | Wrong task/line/source must not be repurposed; preserve unsynced work | Remove PAD revoked/rebound execution predicate; personal/source-context revocation and persisted scope now govern |
| §4.4 F2 | All barriers/version/idempotency/receipt rules | Personal original actor/session/context replaces physical-binding fields; bearer tokens are not frozen payload |
| §4.5 F3 | Entire attachment reference/delete/restore contract | Current personal/task/source authorization replaces device prerequisites only |
| §5 state machine | Entire new LineShared state/outcome/tab/PQE table and old/new separation | No device eligibility predicate |
| §6 phases / §6.1 first-PR recommendation | Incremental scoped changes; not giant mandatory PRs | Replace obsolete P1-01-first and PKI prerequisites with Rev3.2 §16 sequence |
| §6.2 quality protocol | Git verification, provider-specific tests, immutable history, secrets and review evidence | No automatic authority from provisioning; no device-auth implementation |
| §7.1 F1-01/02 | Expiry/logoff historical transfer versus security quarantine | User/task contexts in place of device/grant fields |
| §7.1 F1-03 | Reject forged/missing/mismatched provenance without relabeling | Remove device revocation/binding cases; test forged context, task/line scope and arbitrary installation Id |
| §7.1 F2-01–06 / F3-01–05 | All outcomes and concurrency guarantees | Update only actor/authorization fields; retain F2-05 query-only handoff clarification |
| §7.2 broader UAT | Business, offline durability, history, PostgreSQL, signed upgrade and real tablet tests | Run both models; ordinary HTTPS instead of device mTLS; account/context revocation instead of PAD revocation |
| §7.3 owner dependencies | QA content/readability, ordinary server/DB/volume/backup/TLS and IT security | Device CA, client certificate custody and Native mTLS port are not new patrol dependencies |
| §7.4 out of V1 | All existing exclusions | Also exclude mandatory PAD enrollment, PKI/attestation and the cancelled P1-03B designs |
| §8 immediate plan / kickoff prompt | Preserve as historical handoff text | Next planning target is scoped Rev3.2 retirement after sign-off; do not execute its obsolete first-PR instructions |
| §9 integrity notes | Historical record and approval/implementation/test distinction | Keep originals unchanged; this addendum records the new direction |

The complete exact Rev3.1 section map is in Rev3.2 §18.1. Neither this matrix nor the simplified identity model reopens F2's accepted behavior or F3's file consistency rules.

## 3. Minimal personal-session and F1 implementation contract

### 3.1 Personal authentication and offline receipt

Extend the existing refresh-token family with stable PersonalSessionId rather than building a replacement device-session platform. Existing RefreshTokenSession.Id is a rotating token-row Id. Carry the new family Id across atomic rotation and revoke its current authority on logout/security actions. New LineShared requests validate current signed JWT, active eligible user/auth version and current family; legacy Web/API behavior remains version-isolated.

After online verification, a protected local offline access receipt carries user/session/environment and server time/expiry, capped at 72h and absolute personal-session expiry. It is local access policy, not an online credential or device-bound OfflineGrant. Resume only the same active user after normal restart; no offline new login/account picker. Unknown tasks and formal Submit remain online-only. Offline line switching exposes only that user's previously validated cached tasks and never invents downloads or changes Task.LineId. Receipt expiry/logoff locks access while retaining business state.

No hardware clock, attestation, client certificates, device current-state table, device authorization epoch or fixed-line session is introduced. Ordinary Keystore protection of refresh/context ciphertext remains supported. Separate personal login storage from shared work; logout must not destroy the work encryption key or old contexts needed for reconciliation.

Protect the personal credential vault separately from shared work. The shared work encryption key must not derive from A's password/token/session or depend on keeping A's credential-vault key. Logout removes A's reusable login material and stops A's workers, while shared drafts/photos/operations and necessary WorkContext proofs remain decryptable by the controlled coordinator after B's online login. Use only B's current authorized credentials for B's synchronization; never expose/reuse A's refresh/access token or place it in shared work/outbox. Pending logout bookkeeping stores only non-secret family identification/status; any server family closure needs appropriate authorization and cannot impersonate A through B. Raw context proofs are coordinator-only reconciliation material, not a UI identity selector or general export surface.

### 3.2 Source context and actor fields

| Historical field / concept | Rev3.2 target |
| --- | --- |
| RegisteredDevice / trusted DeviceId | No patrol authorization dependency |
| BindingVersion / fixed PAD line | Removed; Task.LineId stays immutable actual scope |
| NativeSessionId with physical binding | PersonalSessionId: stable login/refresh family, no selected-line or installation binding |
| Device OfflineGrant / CapturingOfflineGrantId | Local ordinary offline receipt plus server WorkContext provenance; distinct purposes |
| ContributorContextId | Task-scoped WorkContextId, owner resolved on server, scoped random proof checked |
| CapturedContributorUserId | Original contributor resolved from validated context; client value only an assertion |
| UploadedByUserId | Actual current authenticated uploader |
| SubmittedByUserId | Actual user explicitly making the formal accepted Submit |
| Shared workspace RegisteredDevice key | Local Environment + InstallationWorkspaceId + actual LineId + TaskId; not trusted server identity |

WorkContext is issued only while its owner is online authenticated and eligible for that actual task. A random proof secret is hashed server-side and protected client-side. It cannot be used to authenticate an uploader or authorize another task. Operations/photo manifests retain stable IDs/dependencies/hash and carry valid context proof for unaccepted work; accepted references resolve through server records (§3.3). Never trust an arbitrary claimed author.

Normal source session/capture expiry and normal logout do not destroy historical source eligibility. Security reset, account deactivation/lost IPQA eligibility or explicit source revocation quarantines unaccepted dependent work. Reactivation does not revive old security-revoked contexts. A regular self-service password change affects current authentication according to existing policy but does not automatically rewrite or security-revoke historical contribution facts. Do not use any AuthVersion mismatch as a blanket provenance rejection without distinguishing the security action.

B's current auth authorizes transport. A's old context establishes only scoped historical provenance. B obtains B's context for new edits; the app does not expose choosing A as the author. Manifest validation precedes unaccepted photo bytes; batches are atomic; quarantined A data cannot be embedded in B's full draft to bypass validation. Preserve both sides and all files; B re-performs rejected items using new B records. Accepted history is not retroactively relabeled.

**Security limitation explicitly accepted by Peter on 2026-11-10:** without physical/device/per-operation signing, possession of a scoped A proof cannot prove A personally typed every edit. A malicious/compromised holder could fabricate data within that scope or misreport offline capture time. The server rejects arbitrary UserId claims without valid context, but does not promise non-repudiation. The controlled app, protected local state, scope/role/security checks and audit attribution are the approved proportional controls. Do not hide this limitation or restore PKI as an unapproved workaround.

### 3.3 Acceptance boundary, security changes and two-tablet conflicts

| State of A's work when A is disabled/source-revoked | Contract |
| --- | --- |
| Not yet server-accepted | Reject/quarantine unaccepted operations and dependent unverified evidence; preserve original records/files. B re-performs affected work under B's context/new IDs, without relabeling A. |
| Valid mutable draft facts accepted by the server before the security action | Preserve A attribution, accepted values/evidence and audit. B may carry unchanged, still-applicable facts into B's own new explicit Submit without A's current login or source proof. Check accepted references/hashes/task/attempt on the server plus current B authority, versions, samples, evidence and F3. New/changed/re-inspection work uses B's context; old facts do not satisfy a required new attempt. |
| Formal immutable submission/history | No revocation-driven content/actor rewrite or rollback. Normal PQE decisions and re-inspection rules still apply. |

Server acceptance, not client acknowledgement, separates these cases. B's current task-read authority permits reconciliation of stable OperationIds and authoritative receipts. An unacknowledged operation may already be accepted; one NotFound cannot exclude late acceptance. Coordinate source-security mutation and write acceptance atomically with task versions. Do not trust a client's claim of an accepted reference. Rejecting a new mixed batch leaves prior accepted draft facts unchanged; rebuild explicitly against current versions, retaining originals and excluding quarantined dependencies.

Two offline tablets cannot see each other's unsynchronized edits. If both cache Task T at DraftVersion v, the first valid conditional synchronization accepts v+1; another write expecting v gets 409. Preserve the losing tablet's pending batch/files/proofs and the accepted server snapshot, while the peer keeps its own work; the server cannot preserve bytes never uploaded to it. Require explicit reload/reapply, new OperationIds for changed/rebased payloads, retained source/conflict history and no automatic overwrite/merge. Test simultaneous reconnect as well as sequential writes. The installation-local coordinator is not a cross-tablet lock; server CAS remains authoritative. These rules and the independent-key logout contract in §3.1 are mirrored in Rev3.2 §§5, 7–9 and §17.

## 4. F2 field replacement and exact handoff obligations

### 4.1 Canonical versus transport facts

| Category | Freeze in the original intent | Re-evaluate on each request |
| --- | --- | --- |
| Actor | SubmitterUserId, OriginPersonalSessionId, SubmitterWorkContextId | Current authenticated UserId, role, auth version and personal session |
| Task | TaskId, actual LineId, WorkflowVersion, BaseRevisionNo, ExpectedDraftVersion | Current persisted task scope/state/versions and current permissions |
| Content | Frozen header/items/samples/dispositions/PIC/source IDs/evidence manifest, canonicalization version/hash | Server recomputation, source validity and attachment availability |
| Physical identity | None required | None required; no IP/model/DeviceId/binding/workspace authentication |
| Transport secrets | Never seal JWT/refresh/context proof secrets into business hash | Current token/proof checked through the appropriate transport authorization |

Same A can reauthenticate into a new session and explicitly transport A's exact still-sendable sealed intent without changing its old origin, payload, attempt or hash. Normal old-session expiry does not require reuse of an expired token. Source/task/current authorization checks still govern first acceptance. Existing accepted receipt recovery does not re-run a new business submission. A different currently logged-in B may query A's receipt but cannot POST under A's identity.

A query-only intent stays query-only when A returns. If the original frozen local payload is unavailable, querying status does not authorize rebuilding the old attempt from the latest draft. Multiple tablets/installations can compete; persisted server versions and uniqueness are the final acceptance guard.

At first Submit acceptance, validate new source operations separately from already accepted draft references (§3.3). A's later deactivation alone must not invalidate unchanged accepted facts carried into B's own new intent. B remains the actual submitter, A remains contributor, and current task/attempt/version/evidence checks remain mandatory. This permission never makes A's sealed/query-only intent sendable by B. Rejected new work or Submit cannot erase an earlier accepted draft or immutable history.

### 4.2 F2 obligations carried forward from the original Handoff

1. B queries A's authoritative receipt and refreshes Task/Draft/Revision before takeover. NotFound alone cannot prove no late acceptance.
2. Explicit takeover persists A as query-only; no resend by that installation. B explicitly starts a new B-bound intent; no impersonation or renamed A intent.
3. Exactly one locally sendable intent per Task across users/line views; old unknown A intent may coexist only as non-sendable recovery. Persist coordinator changes across reboot.
4. First server acceptance is conditional on base revision/draft version/state. At most one competing A/B request accepts that base. The loser reconciles actual receipt/outcome, not a fake next NG revision.
5. Local query-only marking does not cancel already in-flight network traffic. Late A acceptance remains possible and must be reconciled with truthful authorship.
6. These business/concurrency requirements remain mandatory. Only the physical-identity fields and access predicates change.

Preparing/Sealed/InFlight/Accepted/Conflict/OutcomeUnknown, frozen photos, LocalEditVersion/DraftVersion/CurrentRevisionNo separation, explicit-only online Submit, stable SubmissionAttemptId, hash mismatch conflict and no WorkManager POST all remain. Draft/photo synchronization never becomes formal Submit. Changing the selected line cannot reset the task's barrier or move an intent.

## 5. Schema/API/UI compatibility and retirement plan

No historical EF migration is removed or regenerated; no destructive Down or bulk device-data deletion is authorized. Any later schema extension uses a separately reviewed additive migration with both-provider checks. No new fake device states are needed.

| Area | Immediate retirement PR | Later revised business implementation |
| --- | --- | --- |
| ProductionLine IDs/codes | Keep existing Admin management and canonical global uniqueness | Personal-authorized two-line directory and task filters; no device join for eligibility |
| RegisteredDevice/reservations | Keep rows/relationships/audit, disable ordinary new provisioning/bind/rebind/replace | No patrol authorization use; any explicit reservation cleanup or line-deactivation rule replacement gets its own review |
| Enrollment/candidates | Keep all records and original IDs/versions/issuer facts | Not used as personal login, offline receipt, contribution context or task eligibility |
| Admin GET/detail/audit | Keep permissions and historical contract | Read-only legacy device/enrollment presentation, accurate retired capability labels |
| Admin invitation/approval and Bootstrap | Disable new enrollment/proof/receipt activity for normal V1 operation; settings must not reactivate signing | No replacement public pairing API |
| Cancel/revoke / unresolved side effects | Retain controlled closure and optionally drain-only original signer reconciliation | No cleanup prerequisite for ordinary personal login/patrol and no silent transfer to new users |
| Issuance worker | Disabled for new Issue; cleanup-only must never fall through into Issue | No production device CA/Current activation/renewal project |
| Legacy Plans/Tasks/auth/reports | Preserve exact existing guards and semantics | Introduce explicit new LineShared APIs; do not remove old version isolation |
| Old docs/test counts | Retain historical truth | Later add clear supersession notices to active guidance; do not rewrite past evidence |

Cleanup-only mode is an internal worker option, not a new device state or trusted identity. It must preserve ExternalRevocationPending on Failed/no-certificate candidates and all other unresolved cases, use stable original operation IDs, and record completion only after signer acknowledgement plus atomic audit/state commit. Lost responses, late issuance and audit failures remain recoverable. If the old signer is unavailable, pending status remains truthful. Do not mint a different signing operation, mark an unknown outcome successful or delete the external receipt journal to make retirement appear complete.

The existing ProductionLineService deactivation guard checks Device.LineId reservations. The first retirement PR leaves that legacy mutation behavior intact, documenting its limitation. New personal task access must not consult it as a device-binding gate. A separate line-lifecycle change can introduce reviewed Plan/Task checks or explicit audited release of retired reservations; do not silently null all LineIds or mix a business lifecycle rewrite into retirement.

### 5.1 New personal business API outline, for later scope review

Ordinary HTTPS under the existing application/reverse proxy is sufficient. Proposed version-aware routes such as `/api/line-patrol/lines`, `/api/line-patrol/tasks?lineId=...` and task-scoped draft/context/Submit/receipt routes are application namespaces, not TLS listeners. Actual route names/contracts get locked in their scoped PR.

The server directory supplies the two configured actual line IDs. Lists filter before totals/pagination. Detail/write resolves the persisted Task.LineId, workflow and state; a selected line is not a permission scope. A context or proof cannot change persisted task identity. New LineShared reads/writes use explicit personal role/session policies; existing `/api/auth` and legacy endpoints keep compatibility. Candidate DTO `canExecuteLineShared=false` remains a statement about the old device surface; do not change it to reflect personal-user capabilities.

## 6. Small PR sequence and immediate recommendation

**Next recommended implementation PR after Rev3.2 sign-off: R32-01 — retire device provisioning/enrollment execution surfaces safely.** No new task execution or Android implementation in that PR. No migration expected.

Proposed exact existing touchpoints to inspect/limit for R32-01 (paths relative to repository root for implementation planning):

- `api/JaxPower.Ipqc.Api/Program.cs`: feature registration/worker behavior, no client-certificate listener/auth addition.
- `api/JaxPower.Ipqc.Api/Security/DeviceEnrollmentProof.cs`: current enrollment options/validation; make obsolete enable flags unable to bypass retirement.
- `api/JaxPower.Ipqc.Api/Endpoints/DeviceEnrollmentEndpoints.cs`: retain authenticated history reads, disable invitation/approval/Bootstrap; keep only approved closure routes.
- `api/JaxPower.Ipqc.Api/Endpoints/DeviceRegistryEndpoints.cs`: guard retired provisioning mutations, retain historical reads and explicit security closure.
- `api/JaxPower.Ipqc.Api/Services/DeviceCredentialIssuance.cs` and `DeviceEnrollmentLifecycle.cs`: no new Issue, original-operation cleanup and monotonic pending obligation; do not add Current activation.
- `web/src/features/device-provisioning/DeviceProvisioningPage.vue`, `DeviceEnrollmentPanel.vue`, `api.ts`, `enrollmentApi.ts`, affected component tests and `web/src/i18n/locales/en.ts`, `zh-CN.ts`: keep line management, read-only device/enrollment history and truthful disabled actions in both languages.
- `api/JaxPower.Ipqc.Api.Tests/EnrollmentFixture.cs`, `DeviceEnrollmentTests.cs`, `DeviceEnrollmentConcurrencyTests.cs`, `EnrollmentRevocationTests.cs`, `DeviceRegistryTests.cs`, `ProductionLineManagementTests.cs` and a new focused retirement test file: differentiate retired production surfaces from explicitly isolated internal protocol fixtures; do not reopen public routes just to keep an old test green.
- README and active device/enrollment/probe/deployment guidance: proposed later supersession notices; historical signed-off architecture/Handoff unchanged.

The implementation audit must verify exact current file locations and decide the minimal test-fixture seam; this is a proposed inventory, not an actual changed-file claim. ProductionLineService, personal authentication, task services and applied migrations should not be refactored in R32-01. Use existing reviewed cancellation/revocation endpoints where possible rather than a new retirement management platform.

R32-01 acceptance: normal startup/login/line management/legacy workflows still work; old device/enrollment history remains Admin-only; all new provisioning/enrollment/Issue paths are unavailable even under accidental enable settings; cleanup handles original operations and failed acknowledgements; no authority from candidate/certificate/claimed device; no history loss, fake states or new execution routes; Web EN/ZH and backend/CI gates pass.

### 6.1 R32-01 operation allow/deny matrix — approved planning baseline

This matrix is the authoritative allow/deny planning baseline under Peter's 2026-11-10 approval. Route names were checked against the current endpoint declarations; the dedicated R32-01 read-only audit must verify service/worker bypasses and finalize response contracts. Architectural approval of this direction is not implementation authorization; obtain separate R32-01 scope/implementation approval after that audit.

| Existing surface / operation | Proposed R32-01 behavior |
| --- | --- |
| `/api/admin/production-lines`: GET list, GET `/{id}`, GET `/{id}/audit`, POST create, PUT `/{id}`, POST `/{id}/activate` and `/{id}/deactivate` | Preserve current Admin permissions, normalization/uniqueness, revisions, audit and existing validation, including the reservation-based deactivation guard. No blanket device-module switch may disable line management. |
| `/api/admin/devices`: GET list, GET `/{id}`, GET `/{id}/audit`; GET `/api/admin/devices/{id}/enrollments` | Preserve Admin-only historical reads, identities, pagination and audit. |
| `/api/admin/devices`: POST create, PUT `/{id}`, POST `/{id}/bind`, `/{id}/rebind`, `/{id}/unbind` and `/{id}/replace` | Disable ordinary retired provisioning/metadata mutations; no new device, reservation transfer or replacement path. |
| POST `/api/admin/devices/{id}/revoke` | Retain controlled Admin security closure with current reason/revision/audit contracts. Any existing reservation release must not discharge an unresolved external revocation obligation. |
| POST `/api/admin/devices/{id}/enrollments`; POST `/api/admin/enrollments/{id}/approve` | Disable new invitations and approvals; old enable settings cannot reopen them. |
| POST `/api/admin/enrollments/{id}/cancel` | Retain controlled Admin cancellation with atomic lifecycle/audit and pending-revocation preservation. |
| POST `/bootstrap/v1/enrollments/{id}/challenge`, `/proof`, `/receipt-challenge`, `/receipt` | Disable all four anonymous enrollment operations; no substitute public registration or credential download. |
| Issuer worker / adapter | Prohibit new Issue. Optional explicitly configured drain only recovers/revokes original operations, preserves IDs/unknown obligations, and records completion only after signer acknowledgement plus atomic database/audit commit. |
| Existing personal authentication, legacy Plan/Task/review/report APIs | Preserve existing behavior and RBAC; introduce no R32-02 or Android/LineShared execution APIs. |

For retained Admin surfaces, preserve normal authentication/authorization ordering and 401/403 behavior; the audit must define the unavailable response for otherwise authorized retired operations without weakening permissions. Controlled closure is not new enrollment, credential activation or a migration. No new fake device status, external-success claim, destructive rollback or blanket reservation cleanup is permitted.

Then scope separately: personal refresh-family/offline receipts and line/task reads → LineShared Plan/snapshot support → F1 draft/operation/source contracts → forms/dispositions/PIC → attachments/F3 → F2 Submit/exact review → version-aware PQE/history/reports → retention/recovery/UAT. Android personal login/line selection/cache can begin once its contracts stabilize and proceeds in small increments alongside backend work. Do not combine all phases into the retirement PR.

## 7. Regression obligations and external gates

The complete proposed matrix is Rev3.2 §17: 47 independently numbered cases, retaining the original 43 IDs and adding SYNC-01, F1-06, F1-07 and SEC-02. Minimum review emphasis:

- User online auth/permissions, both-line selection, actual Task.LineId/workflow/state, unchanged legacy ownership and reports.
- Same active-user offline resume within 72 hours; expired access locks without deletion; cached-only offline line switch, no offline new user/unknown task/Submit.
- SYNC-01: two tablets edit the same Task offline without peer visibility; sequential/concurrent reconnect uses expected versions, retains both sides, returns 409 for stale writes and requires explicit reapply with new IDs for changed content.
- F1 A-expired/B-current transfer versus security-revoked A quarantine; scoped proof owner/task mismatch; no arbitrary contributor; accepted history unchanged.
- F1-06/F1-07: accepted-before-deactivation A draft facts remain usable in B's own Submit; unaccepted revoked-source work is quarantined, unknown acknowledgements reconciled, accepted references verified and prior accepted facts/history never rolled back by a rejected batch.
- SEC-02: after deleting A's personal credentials/key, B's online login and app restart still permit controlled decryption/reconciliation of eligible shared work/proof using an independent work key and only B's credentials; no A token leakage or shared-outbox dependency.
- F2 old query-only A never resendable on return; new-session A uses exact original sealed content; B new intent and late A response have one server base-state winner; no automatic Submit.
- All original samples/dispositions/NG/PQE/photo/reference/F3/retention/report cases, with ordinary personal authorization substituted for device predicates.
- Retirement disable/feature-option bypass, safe signer drain and ExternalRevocationPending acknowledgement/audit rollback.
- COMP-02 and §6.1: preserve line management/history/controlled closure while blocking the specific retired operations; server guards, not Web hiding alone, enforce the matrix.
- Fresh/populated migrations, stable FKs/history, SQLite and live PostgreSQL write races, both EF model checks, Web/Android builds/CI where implemented.
- Same minSdk 30 APK on SM-X210/API 35 and SM-T500/API 30; restart, line switching, weak network, photos/scanner, signed upgrade and ordinary secure storage.

Pending IT inputs are normal server FQDN/port/certificate trust, factory-network reachability from both tablets, PostgreSQL, separate photo volume/backup and security/update assessment. Production device CA, CSR registration, mTLS/trusted-certificate headers and Current activation are removed prerequisites. Connected without internet is not proof of internal failure; validate the intended HTTPS endpoint. Do not invent hostnames, ports, CA/trust status or test success.

No Rev3.2 implementation tests have been run by preparing/finalizing these documents. Documentation integrity checks are not backend, Android, real-PAD or PostgreSQL PASS results. Future completion reports distinguish PASS, FAIL, SKIP and PENDING external gates without treating simulated credentials or SQLite as live evidence.

## 8. Review decisions and historical integrity

Peter's formal sign-off on 2026-11-10 covers the simplified personal-auth/line-selection direction, the user/session offline receipt and task-scoped WorkContext, the explicitly accepted offline-attribution limit, the three review clarifications, preserved F2/F3/business rules and non-destructive retirement strategy. No repeated architecture review is required. IT endpoint and deployment details are pending inputs, not a reason to restore device PKI or ask Peter to choose implementation libraries.

If independent per-edit offline authorship proof becomes mandatory, state the concrete conflict with the simplified promise and request a new requirement decision. Do not claim the proposed bearer context supplies it. Do not reactivate a query-only F2 intent as a recovery shortcut.

Rev3.2 is signed off. Complete the now-authorized fresh R32-01 read-only exact-file audit, readiness report and next implementation prompt; obtain separate scope/implementation approval before coding. This task does not authorize application implementation, branches, migrations, commits, pushes, new PRs, merges or deployments. Keep all 47 acceptance cases proposed / NOT RUN until their implementation gates are actually executed.
