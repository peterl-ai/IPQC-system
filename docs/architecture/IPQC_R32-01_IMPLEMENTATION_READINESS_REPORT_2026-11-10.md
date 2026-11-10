# R32-01 Implementation Readiness Report

Date: 2026-11-10, America/New_York. Repository: `peterl-ai/IPQC-system`.

**Outcome: ready for a separately approved, migration-free retirement PR. Architecture Rev3.2 is signed off; R32-01 application implementation is NOT authorized by this report.** No application code, database, branch, commit, push, PR, merge or deployment was changed in this task.

## 1. Verified baseline and authority

Workspace root: `C:/Users/kuohao.lai/Desktop/project/IPQC/IPQC-system`.

| Item | Verified result |
| --- | --- |
| Current branch | `main` |
| Local HEAD | `d24eed7adc6f7302bacd02182ce5a7417fe15b1e` |
| Cached `origin/main` | Same SHA |
| Live remote main | Same SHA, confirmed through GitHub connector commit lookup for `main`; [merge commit](https://github.com/peterl-ai/IPQC-system/commit/d24eed7adc6f7302bacd02182ce5a7417fe15b1e) |
| Direct Git network check | `git ls-remote origin refs/heads/main` could not connect to GitHub:443 in the restricted shell; the independent connector lookup succeeded. No fetch/ref update was needed. |
| Tracked working tree / index before work | Clean; no staged changes |
| Existing untracked work | Two Rev3.2 drafts plus unrelated walkthrough/handoff/prompt/review files/directories; preserved except the two explicitly authorized document promotions |
| Rules read | `AGENTS.md`, `CLAUDE.md`, complete `README.md`, both complete revised Rev3.2 documents, relevant enrollment/RBAC/verification documentation and actual source/test contracts |

Unrelated untracked paths preserved: `ANDROID_PATROL_WALKTHROUGH_DELTA_v0.1_2026-11-05.md`, `IPQC_PROJECT_HANDOFF_2026-11-05.md`, `docs/architecture/IPQC_P1-01_CODEX_IMPLEMENTATION_PROMPT_DRAFT_2026-11-06.md`, `handoff/`, `pr_diff/`, and `prompts/command.txt`. Their presence is not authorization to stage or delete them.

Peter's final approval dated **2026-11-10** is recorded in both promoted documents:

- [Approved Rev3.2 architecture](IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md).
- [Approved Rev3.2 Implementation Handoff Supersession](IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md), particularly §6.1.

These replace the two local `_DRAFT.md` filenames. Headers, cross-links, acceptance of the WorkContext attribution limitation and execution boundaries were updated. All **47 acceptance-case rows are text-identical to the revised draft**, including their IDs. Their status remains **proposed / NOT RUN**; architecture approval does not establish implementation or test success. Dual-tablet conflicts, accepted-versus-unaccepted source work, independent shared-work encryption, all six F2 handoff invariants and F3 requirements remain intact.

Historical sources are unchanged, with SHA-256 verified before and after promotion:

| Historical source | SHA-256 |
| --- | --- |
| `IPQC_ANDROID_ARCHITECTURE_V1_REV3_1_2026-11-06.md` | `C1A1968C5A1A2DC1C3E2BF28CADE56EE39568761DE4D917EE489EB551694C801` |
| `IPQC_ANDROID_ARCHITECTURE_V1_IMPLEMENTATION_HANDOFF_2026-11-06.md` | `666AE0262E75B51A90D9899E59FE257DF3733841A2628AFD99BB51F5A39537D9` |

The final source of truth is Rev3.2 plus its supersession addendum. Old README/probe/P1-03A instructions are AS-IS history where they conflict; they do not authorize resuming P1-03B. No further architecture review or choice of implementation libraries is needed.

## 2. Actual code findings and minimum response

Full paths in the inventory are relative to the verified workspace root. In the evidence table, `Endpoints/`, `Services/` and `Security/` abbreviate paths beneath `api/JaxPower.Ipqc.Api/`. Line references describe this baseline, not a future diff.

| Verified finding | Evidence | R32-01 consequence |
| --- | --- | --- |
| Enrollment services and worker are always registered; a legacy development flag selects the signing-capable issuer | `api/JaxPower.Ipqc.Api/Program.cs:27`–36 | Default runtime must expose no new issuance; opting into cleanup must be independent of old enrollment enable flags. |
| Bootstrap is currently conditional, not permanently retired | `Endpoints/DeviceEnrollmentEndpoints.cs:30`–48 accepts in Development/Testing when enabled | Replace all four POST handlers with non-executing retirement responses; no environment, HTTPS, JWT or option combination reopens them. |
| Admin create/approve do not depend on BootstrapEnabled | `Services/DeviceEnrollmentService.cs:32`, `:104`; endpoint registration at `Endpoints/DeviceEnrollmentEndpoints.cs:20`–25 | Guard both endpoint and service paths; disabling Bootstrap alone leaves invitations/approval active. |
| Every registry mutation is still callable | `Services/DeviceRegistryService.cs:26`–88 | Block create/update/reserve/replace and only the unbind branch of ReleaseAsync; retain its revoke branch. |
| Recover can initiate new signing | `Services/DeviceCredentialIssuance.cs:49`–50 | Remove the recover-NotIssued-to-Issue path from runtime processing. An early worker flag alone is insufficient because ProcessNextAsync is directly callable through DI. |
| Existing queue selection excludes IssuedUnconfirmed candidates without a pending flag | `Services/DeviceCredentialIssuance.cs:20`–24 | A drain must consider open persisted enrollments/candidates, not just the current issuance query. Already-issued unconfirmed candidates and invitations without candidates need an explicit closure path. |
| Expired on GET may be a projection only | `Services/DeviceEnrollmentService.cs:19`–20; Web cancel hides both Cancelled and Expired | Do not equate displayed Expired with persisted closure/revocation. The retirement drain must inspect stored state; retain an explicit Admin closure affordance for an expired projection that may still be open. |
| PR #11 pending-flag fix is already present | `Services/DeviceEnrollmentLifecycle.cs:35` uses OR assignment | Keep this invariant. Do not replace it with certificate-presence recomputation or reset flags during retirement. |
| Signer work is outside DB transactions; lease ownership gates finalization | `Services/DeviceCredentialIssuance.cs:41`–100 | Retain the two-transaction pattern, original operation ID, lease fence and acknowledgement/audit atomicity. |
| Queue may stop at a closed no-cleanup candidate; due retries are ordered by Id | `Services/DeviceCredentialIssuance.cs:24`, `:32`, `:71`, worker `:123` | A bounded drain must not repeatedly select a no-op or let repeatedly failing low IDs indefinitely exclude later obligations. Use eligible work and oldest-due ordering with stable ID tie-break; no schema change is required. |
| Line deactivation explicitly checks existing reservations | `Services/ProductionLineService.cs:36` | Preserve `409 LineInUse`; do not remove the check or release reservations automatically. |
| Audit projection already includes all affected lines without duplicate rows | `Services/ProvisioningStore.cs:83` onward | Retain `ProductionLineId OR OldLineId OR NewLineId`, totals, stable DeviceId and correlated ChangeId. |
| Enrollment body middleware currently precedes authentication | `Program.cs:137`–148 | Avoid a new pre-auth retirement middleware. Ensure Admin retirement responses preserve 401/403 ordering; put any retained enrollment-specific body check after authorization, before body binding. |

The AS-IS findings are not additional Rev3.2 architecture objections. They identify the concrete implementation work needed to enforce the approved retirement boundary.

## 3. Final endpoint and response contract for the implementation proposal

Keep routes registered where authorization must still run. Do not implement a prefix-wide kill switch: line administration and security closure share the Admin namespace.

Engineering decision for the R32-01 proposal: authenticated Admin calls to retired Admin mutations return **410 Gone**, ProblemDetails with `code: DeviceProvisioningRetired`. Anonymous Bootstrap operations return **404 Not Found**, consistent with the existing disabled surface. Retired handlers do not bind old invitation/CSR/approval/device DTOs, inspect target existence or invoke mutation services. Retain existing Admin read/write policies on those handlers. Normal infrastructure rate limits may still return 429; generic transport rejection is not a successful operation.

| Method | Exact route | Proposed behavior |
| --- | --- | --- |
| GET | `/api/admin/production-lines` | Preserve filtering, pagination/totals and `LinesRead` |
| GET | `/api/admin/production-lines/{id:guid}` | Preserve detail and missing-target 404 |
| GET | `/api/admin/production-lines/{id:guid}/audit` | Preserve line/device event projection and pagination |
| POST | `/api/admin/production-lines` | Preserve create, normalization, global uniqueness and audit; existing 201 |
| PUT | `/api/admin/production-lines/{id:guid}` | Preserve rename, expectedRevision, validation and audit |
| POST | `/api/admin/production-lines/{id:guid}/activate` | Preserve current validation/revision/audit |
| POST | `/api/admin/production-lines/{id:guid}/deactivate` | Preserve current validation/revision/audit, including 409 LineInUse |
| GET | `/api/admin/devices` | Preserve Admin-only historical list/filter/totals |
| GET | `/api/admin/devices/{id:guid}` | Preserve historical detail and missing-target 404 |
| GET | `/api/admin/devices/{id:guid}/audit` | Preserve stable IDs, actors, reason, snapshots, ChangeId and totals |
| POST | `/api/admin/devices` | Retire: 410 for authorized Admin; no device/audit insert |
| PUT | `/api/admin/devices/{id:guid}` | Retire: 410; no metadata/revision change |
| POST | `/api/admin/devices/{id:guid}/bind` | Retire: 410; no reservation or version change |
| POST | `/api/admin/devices/{id:guid}/rebind` | Retire: 410; no reservation transfer/invalidation side effect |
| POST | `/api/admin/devices/{id:guid}/unbind` | Retire: 410; no reservation release |
| POST | `/api/admin/devices/{id:guid}/replace` | Retire: 410; no second device or historical transfer |
| POST | `/api/admin/devices/{id:guid}/revoke` | Retain DevicesRead + DevicesWrite, reason/revision checks, original ReleaseAsync(revoke:true), enrollment invalidation and correlated atomic audit; existing 200/400/404/409 behavior |
| GET | `/api/admin/devices/{id:guid}/enrollments` | Preserve Admin historical DTOs/pagination and no-store; no new public credential-recovery route |
| POST | `/api/admin/devices/{id:guid}/enrollments` | Retire: 410; no invitation, expiry-side-effect write or new secret |
| POST | `/api/admin/enrollments/{id:guid}/approve` | Retire: 410, including replay of an old approval operation; history is recovered through Admin GET |
| POST | `/api/admin/enrollments/{id:guid}/cancel` | Retain current Admin transaction check, reason/revision/environment validation, closure and atomic audit; existing 200/400/404/409 behavior |
| POST | `/bootstrap/v1/enrollments/{id:guid}/challenge` | Retire: 404; no challenge generation |
| POST | `/bootstrap/v1/enrollments/{id:guid}/proof` | Retire: 404; no consumption or state change |
| POST | `/bootstrap/v1/enrollments/{id:guid}/receipt-challenge` | Retire: 404; no anonymous DB growth |
| POST | `/bootstrap/v1/enrollments/{id:guid}/receipt` | Retire: 404; no anonymous candidate/PEM disclosure or challenge consumption |
| Existing methods | `/api/auth/*`, `/api/me`, legacy Standards/Plans/Tasks/reviews/Completed/report/XLSX | Unchanged authentication, RBAC, ownership, workflows and reporting |

Ordering for Admin routes: invalid/missing/expired/revoked authentication → **401**; authenticated PQE/IPQA/forced-change Admin without the relevant permission → **403**; otherwise retired mutation → **410**. Test existing and nonexistent target IDs, malformed/extra JSON and old otherwise-valid payloads. Authorization runs before retirement/lookup/DTO validation. Retained APIs keep their normal 400/404/409 rules. Revoke retains its current requirement for a PendingCredential device; do not broaden terminal-device transitions.

Service-level checks independently reject retired methods before opening ProvisioningStore.WriteAsync, allocating IDs, consuming challenges or auditing. In particular ReleaseAsync(revoke:false) rejects while ReleaseAsync(revoke:true) retains its existing behavior. These guards are unconditional build behavior, not injectable production enable switches.

There is no activation route or Current enum today. Do not add one. Device/Enrollment/Candidate `canExecuteLineShared=false` and Device `credentialStatus=NotEnrolled` retain their existing meanings; personal task capabilities are outside R32-01.

## 4. Worker, signer, configuration and cleanup safety

### 4.1 Configuration decisions

- Retired API behavior is unconditional across Production, Development and Testing. There is no `AllowLegacyEnrollment`, test environment exemption or feature flag that opens the old HTTP/service mutation paths.
- Add one explicit **`Enrollment:CleanupEnabled`**, default false, for old-operation drain only. Existing WorkerEnabled may stop automatic scheduling; it is never sufficient to authorize cleanup or issuance. Direct ProcessNextAsync must also respect CleanupEnabled. Tests may drive a cleanup-enabled processor with automatic WorkerEnabled=false.
- BootstrapEnabled and DevelopmentIssuerEnabled alone cannot register a signing-capable runtime or reopen an API. Warn that obsolete enabling configuration is ineffective; do not break otherwise valid login/line management merely because an unused old flag or PFX path remains configured. Validate real cleanup configuration only when cleanup is requested.
- Default startup neither loads a PFX nor creates a receipt directory/signing key. Keep the unavailable signer as the default. If a configured cleanup adapter is unavailable, leave pending work visible and report deferred cleanup; never acknowledge success.
- For isolated Development/Testing cleanup, the existing issuer may be wrapped in a cleanup-only runtime adapter whose IssueAsync always refuses. The production DI surface permits only original-operation Recover/Revoke, with a second no-Issue guard in the processor. Pure software fixtures may directly construct an isolated signer outside the application HTTP/DI path; that is not an enrollment enable mode.
- Keep the original EnvironmentId and signer/journal custody for outstanding work. CancelAsync currently checks the stored request environment against options; do not erase EnvironmentId during retirement. A mismatch remains a conflict, not permission to rewrite it. Drain must verify configured environment and original IssuerId/root association; a different CA/journal cannot acknowledge the old obligation. No new production CA adapter is in scope.

### 4.2 Required drain protocol

1. Cleanup is opt-in. Under the existing ProvisioningStore transaction, select bounded eligible persisted work, including nonterminal enrollments without candidates, PendingIssuance, OutcomeUnknown, issued-unconfirmed candidates and any Failed/Revoked candidate with ExternalRevocationPending. Do not use the GET display state as a closure predicate. Respect outstanding candidate leases; use due-time ordering and a stable tie-break to avoid starvation.
2. Close a still-open request using existing Cancelled/Expired lifecycle transitions, a truthful System actor/reason and append-only audit. Do not re-close terminal rows on every retry. Preserve device/line/enrollment/credential IDs, source/target versions, original operation ID, approval facts, CSR/SPKI, issuer/root and existing public result metadata. Retirement closure does not revoke the registry row or clear its reservation automatically.
3. Keep CloseAsync's monotonic pending flag. A candidate with a stored certificate, an uncertain external attempt or an existing pending flag requires the original external operation to be reconciled. A provably never-attempted PendingIssuance record may be cancelled without signing; do not manufacture an issued result. Unknown is never equivalent to definitive NotIssued.
4. Commit closure, cleanup intent and a fenced lease before any external interaction. Outside all DB transactions, Recover and/or Revoke the **original IssuanceOperationId** through the matching old adapter. A NotIssued recovery result can lead only to confirmed closure/cancellation of that original operation, never Issue. If a revocation/cancellation fence is needed to exclude a late publication, obtain it before acknowledging cleanup. Never rely on a missing receipt in a lost/misconfigured journal as proof of non-issuance.
5. Only adapter acknowledgement of durable revocation/cancellation, covering possible late issuance, can discharge the pending obligation. Current RevokeAsync is Task-returning: successful completion must mean that acknowledgement; timeout/uncertainty must throw/defer. The existing dev tombstone is only an isolated-test acknowledgement under its per-operation file lock, not production CRL/OCSP evidence. Keep its journal/tombstone unchanged and idempotent.
6. Reacquire the provisioning transaction, clear stale EF state, verify the same lease and original identity, and atomically append the result audit and clear pending. A signer failure leaves pending; audit/finalization failure rolls back the database acknowledgement and retries the same original operation after lease expiry. A stale worker cannot finalize another worker's lease. External calls may repeat after uncertain acknowledgement; exactly one committed acknowledgement is required, not an impossible exactly-once network guarantee.
7. A recovered late result remains historical/non-authorizing and is revoked against its original identity. Never restore Approved/Issuing/IssuedUnconfirmed as an active issuance progression or transfer a certificate to a replacement. Do not require a currently valid leaf certificate to revoke an expired or rejected result. Preserve unknown/invalid-result obligations even with CertificatePem=null.

This is one bounded cleanup processor, not a new enrollment/retirement workflow platform. Existing lease/revision/error/operation fields and audit actions suffice. No new device/enrollment enum is needed. Local closure can be committed while the signer is unavailable, retaining the external obligation; signer unavailability must not prevent safe Admin cancellation/revocation.

### 4.3 Operational boundary

At a later approved rollout, stop all old signing-capable instances/workers before running the retirement build/drain. An old binary can still invoke Issue independently of new guards. Preserve its external durable operation journal and keys until outstanding obligations are reconciled; no deployment is performed in this task. Rollback must not silently reopen signing or use pre-P1-03A readers that cannot handle nullable typed audit actors. Do not delete an external journal or private material merely to make retirement look complete.

## 5. Exact proposed application change set

All entries are **future modifications**, not files changed during this audit. One PR is sufficient: the complexity is concentrated in guards, cleanup and adapting fixtures, with no business execution/schema work.

### 5.1 Backend production files — eight existing files

| Repository-relative file | Required change |
| --- | --- |
| `api/JaxPower.Ipqc.Api/Program.cs` | Retired/default runtime registration; optional cleanup-only issuer/worker; obsolete-option warnings; preserve auth/line/scheduler mappings; ensure enrollment-specific body handling does not mask Admin authorization. |
| `api/JaxPower.Ipqc.Api/Security/DeviceEnrollmentProof.cs` | Update DeviceEnrollmentOptions validation and cleanup-only option. A small nonconfigurable shared retirement guard may live here; leave cryptographic parsing/verification algorithms unchanged. |
| `api/JaxPower.Ipqc.Api/Endpoints/DeviceEnrollmentEndpoints.cs` | Body-independent retired invite/approve and four Bootstrap stubs; preserve Admin list/cancel, policies, no-store and legitimate limits. |
| `api/JaxPower.Ipqc.Api/Endpoints/DeviceRegistryEndpoints.cs` | Retired mutation stubs with existing permissions; separate retained revoke from retired unbind in the shared mapping loop; preserve GET/detail/audit. |
| `api/JaxPower.Ipqc.Api/Services/DeviceEnrollmentService.cs` | Unconditional service guards for create/approve/challenge/proof/receipt-challenge/receipt before side effects; preserve list, cancellation, output and current Admin/environment validation. |
| `api/JaxPower.Ipqc.Api/Services/DeviceRegistryService.cs` | Guard create/update/reserve/replace and ReleaseAsync's unbind branch; preserve revoke and its lifecycle/audit transaction. |
| `api/JaxPower.Ipqc.Api/Services/DeviceCredentialIssuance.cs` | Convert runtime processing to bounded opt-in closure/recovery/revocation only; cover omitted historical states, lease/fairness/retry behavior and remove every runtime path to Issue. |
| `api/JaxPower.Ipqc.Api/Security/DeviceCertificateIssuer.cs` | Cleanup-only runtime wrapper/contract; Issue always refused through DI, original Recover/Revoke/journal preserved. Do not add a CA or generate keys. |

**Inspect and preserve without planned implementation edits:** `Services/DeviceEnrollmentLifecycle.cs` (the OR fix already suffices), `ProvisioningStore.cs`, `ProductionLineService.cs`, `Endpoints/ProductionLineEndpoints.cs`, `Security/IpqcPermissions.cs`, authentication/user services and endpoints, all Plan/Task/generation/review/report services, entities/contracts/model/snapshot and all applied migrations. If implementation uncovers a necessary change outside this list, explain its dependency and keep it minimal; do not refactor these services as retirement work.

### 5.2 Web production files — six existing files

| Repository-relative file | Required change |
| --- | --- |
| `web/src/features/device-provisioning/DeviceProvisioningPage.vue` | Keep the line tab's add/rename/activate/deactivate/audit/pagination. Device tab becomes historical with only applicable revoke, enrollment history and audit controls; remove create/edit/bind/rebind/unbind/replace forms/dispatch. Keep stable DeviceId/ChangeId display. Stop loading the complete device inventory solely for retired reservation selectors. |
| `web/src/features/device-provisioning/DeviceEnrollmentPanel.vue` | Historical list/detail/pagination/candidate/error/pending display and controlled cancel remain. Remove invite, one-time-secret and physical-approval controls/state. Do not hide needed closure solely because GET derives Expired; an explicit closure attempt may receive the existing terminal/stale 409, then refresh. No activation UI. |
| `web/src/features/device-provisioning/api.ts` | Remove unused retired mutation callers/action types; retain line operations, device reads, audit and a narrowly typed revoke call. Map DeviceProvisioningRetired to a localized safe message. |
| `web/src/features/device-provisioning/enrollmentApi.ts` | Retain historical types/list/cancel; remove invite/approve callers and unused inputs. Preserve server DTO field meanings. |
| `web/src/i18n/locales/en.ts` | Historical-module/retirement labels and errors; remove promises of future mTLS activation. Update LineInUse guidance to retained revoke/closure, since unbind is retired. Keep historical event labels. |
| `web/src/i18n/locales/zh-CN.ts` | Equivalent Simplified Chinese wording; no component hard-coded display strings. |

Keep `/provisioning`, router permissions and server DevicesRead/DevicesWrite/LinesRead/LinesWrite mappings unchanged. `web/src/router/index.ts`, `router.test.ts`, `services/permissions.ts` and `permissions.test.ts` are regression surfaces, not a permission redesign. Both Admin tabs share a page, so verify that retiring device controls does not disable the line Add button.

### 5.3 Test files and fixture adaptation

Modify these existing backend test files as needed:

- `api/JaxPower.Ipqc.Api.Tests/DeviceRegistryTests.cs`: contains the shared ProvisioningFixture as well as obsolete successful mutation expectations. Seed synthetic historical devices/events through test-only database helpers; retain revoke/rollback/race tests and assert retired mutations have no effects.
- `api/JaxPower.Ipqc.Api.Tests/EnrollmentFixture.cs`: replace HTTP create/bind/invite/proof/approve setup with valid synthetic pre-retirement rows/receipts; preserve controlled signer, fake clock, audit-failure hooks and original-ID assertions. No production enable override.
- `api/JaxPower.Ipqc.Api.Tests/DeviceEnrollmentTests.cs`: change retired HTTP success expectations into retirement/RBAC/non-authority tests, preserving meaningful pure protocol checks without reopening APIs.
- `api/JaxPower.Ipqc.Api.Tests/DeviceEnrollmentConcurrencyTests.cs`: exercise competing cleanup leases, cancel/revoke versus cleanup and recoverable pre-existing signer results; replace expectations that new issuance/invite/proof/approval succeeds.
- `api/JaxPower.Ipqc.Api.Tests/EnrollmentRevocationTests.cs`: retain failed+pending/no-PEM, failed signer, in-flight acknowledgement and audit rollback regressions using seeded historical transitions. Actual cancel/revoke/expiry/retirement closure remain tested; retired rebind/unbind/replace now reject without side effects.
- `api/JaxPower.Ipqc.Api.Tests/EnrollmentBindingTests.cs`: retain immutable environment/device/line/version and non-transfer assertions through seeded historical cases and cleanup; no successful retired API setup.
- `api/JaxPower.Ipqc.Api.Tests/EnrollmentMigrationTests.cs`: its P102 upgrade source currently creates/binds via HTTP and its fresh-schema test invites a device. Replace only fixture creation; retain actual migration/constraint/history assertions.
- `api/JaxPower.Ipqc.Api.Tests/ProductionLineManagementTests.cs`: retain normalization, restrictive FKs, stale revisions, Admin matrix and line reservation guard; adapt device setup and obsolete mutation success checks. Preserve legacy report/state equivalence through retained revoke and rejected retired calls.
- `api/JaxPower.Ipqc.Api.Tests/ProvisioningAuditTests.cs`: seed pre-existing bind/rebind/unbind/replacement/metadata events, then prove both device identities, line OR projection, unchanged event JSON and duplicate-free pagination after retirement/closure.

Add one focused `api/JaxPower.Ipqc.Api.Tests/DeviceProvisioningRetirementTests.cs` for the allow/deny/configuration/direct-service matrix. Keep the synthetic history seed helper in existing fixture files unless extraction actually improves clarity; no new production fixture seam or Testing bypass. Do not retain old HTTP success solely to preserve a numerical test count. Explain superseded expectations and replace them with equally meaningful negative/history/cleanup assertions; do not silently skip them.

Modify `web/src/features/device-provisioning/DeviceProvisioningPage.test.ts`, `DeviceEnrollmentPanel.test.ts` and `api.test.ts`. Replace obsolete bind/replace/invite/approve UI expectations with absence/no-request assertions, while retaining keyboard tabs, lines, error handling, stable audit identities, pagination and cancellation/revoke tests in EN/ZH. Pure `EnrollmentInteropTests.cs`, migration/model tests not dependent on retired fixture paths, all legacy tests and router/permission tests should remain unchanged and be rerun.

### 5.4 Active guidance in the later PR

Use short supersession notices in `README.md`, `docs/13_DEVICE_ENROLLMENT.md`, `docs/14_P103A_VERIFICATION.md` and `android/enrollment-probe/README.md`: label old operational enrollment/probe commands historical and inactive, link approved Rev3.2, state no P1-03B continuation, document cleanup-only operation/custody and retain historical test evidence. Correct the active hardware inventory in `docs/08_ANDROID_DEVICE_TARGET.md` to SM-X210/API35 plus SM-T500/API30, minSdk30; this is documentation only. No probe project/build/APK or patrol app is added. Historical signed Rev3.1/original Handoff and old test results are not rewritten.

Include the two approved architecture documents in the eventual scoped change when commit authorization is given; they are currently local untracked files. Do not accidentally omit them or stage unrelated handoff/review material.

## 6. Database and migration impact

**No migration is necessary or proposed.** All needed closure/lease/retry/acknowledgement fields already exist. Runtime options and retirement errors are not EF entities. Keep the model snapshot byte-for-byte unchanged.

Verified existing structure includes ProductionLines global NormalizedCode uniqueness and Revision; RegisteredDevices' unique nullable LineId, status/counter checks and restrictive FKs; DeviceEnrollments' filtered unique open-device slot and original N/N+1/environment/approval facts; EnrollmentChallenges' revision/nonce/payload facts; DeviceCredentials' unique enrollment/issuance/certificate IDs, lease/error/pending fields; typed append-only provisioning audit and restrictive historical references. P103A Down explicitly throws rather than discard history. Audit SaveChanges rejects modifying/deleting historical audit entries.

Retirement rejection is read-only: no seed/backfill, revision increment, audit insert or expiry cleanup on a denied request. Explicit retained cancel/revoke and enabled cleanup append truthful lifecycle/audit events atomically and may update their existing lifecycle counters; this is not a historical identity rewrite. Revoke's live reservation release is preserved, while old associations remain in enrollment/credential rows and audit snapshots. Default startup/drain-disabled operation does not silently bulk-close records.

Implementation migration-safety evidence must include: unchanged migrations/model/snapshot checksums, same migration inventory, fresh SQLite, populated P101/P102 and historical legacy upgrades, `PRAGMA foreign_key_check`, typed audit/FK checks, full original-column/report comparison and no pending model changes for SQLite and PostgreSQL. Existing SQL-generation tests review PostgreSQL native types, restrictive FKs and historical preservation without executing PostgreSQL. A live disposable PostgreSQL cleanup/concurrency run is separate; without one, report SKIPPED. Never run against the production DB or invoke destructive Down.

## 7. Focused R32-01 acceptance plan

These **20 proposed cases are NOT RUN**, and do not replace or claim completion of the 47 architecture-wide cases.

| ID | Proposed regression / required result |
| --- | --- |
| R01-01 | Every retained line operation still succeeds/fails as before, including normalized duplicate codes on inactive lines, stale revision, validation, filtering and append-only audit. |
| R01-02 | Every retired Admin route: anonymous/invalid/expired/revoked token 401; PQE/IPQA/forced-change Admin 403; eligible Admin 410 with DeviceProvisioningRetired, for valid/malformed/extra payloads and existing/missing IDs. No writes, audit, generated secrets, leases or signer calls. |
| R01-03 | All four Bootstrap routes return unavailable 404 without DB reads/mutations that reveal enrollment state, secret/key proof validation or candidate disclosure; old valid proof and invalid/unknown IDs are equivalent. Respect documented transport/throttle behavior. |
| R01-04 | BootstrapEnabled, DevelopmentIssuerEnabled, WorkerEnabled, environment Development/Testing and spoofed HTTPS/proxy/device headers cannot reopen retired operations. Default startup ignores unused issuer material and keeps login/line management operational. |
| R01-05 | Direct service create/update/reserve/replace/unbind and invitation/approval/challenge/proof/receipt methods reject before a transaction or side effect; default or cleanup-enabled runtime IssueAsync is never callable successfully. |
| R01-06 | Retained revoke/cancel preserve reasons, 400/404/409, expected revisions, original immutable IDs and correlated atomic Admin audit; audit failure rolls back all database effects. |
| R01-07 | Seed Failed + CertificatePem=null + ExternalRevocationPending=true; cancel/revoke/expiry/retirement closure retains the flag, original issuance operation and full historical identity. Rebind/unbind/replace attempts now reject without altering it. |
| R01-08 | IssuedUnconfirmed not initially pending, projected-expired but persisted-open, invitation without a candidate, PendingIssuance and already-terminal records all receive appropriate opt-in closure/skip processing without new issuance or duplicate closure events. |
| R01-09 | Recover outcomes Issued/Unknown/NotIssued, lost journal/configured wrong issuer/environment, revoked/replaced device histories and expired certificates never trigger Issue, Current activation, record transfer or fabricated acknowledgement. |
| R01-10 | Failed/blocked external revoke leaves pending; confirm RevokeAsync uses the original ID outside DB transactions. A successful acknowledgement plus audit commit clears once. No further network call after committed acknowledgement. |
| R01-11 | Signer success followed by audit/commit failure leaves DB pending; restart/lease expiry retries the same external ID idempotently and commits exactly one result audit. Cancellation during external calls preserves recovery. |
| R01-12 | Two workers, lease expiry/stale acknowledgement and concurrent cancel/revoke have coherent final state with no identity changes; closed no-op/failing low-ID work cannot starve later due cleanup. Test a queue exceeding the eight-per-tick budget. |
| R01-13 | CleanupEnabled=false, unavailable signer, WorkerEnabled=false and explicit cleanup-enabled isolated adapter behave as specified; no old flags load signing material or authorize new Issue. |
| R01-14 | Before/after historical device/enrollment/audit snapshots and IDs remain unchanged except explicitly tested lifecycle transitions/new audit. Line history keeps both replacement identities, old/new line associations, correlated ChangeId, truthful actor kinds and duplicate-free totals. |
| R01-15 | Existing reservation still causes 409 LineInUse. Retired unbind cannot release it; controlled revoke can release it atomically, after which normal deactivation works. No automatic registry deactivation/rebinding. |
| R01-16 | EN/ZH Web: line Add/rename/activate/deactivate/audit/pagination remain; device add/edit/bind/rebind/unbind/replace and invite/approval/secret UI are absent and issue no requests, including through stale component state. |
| R01-17 | EN/ZH Web: revoke/cancel require reason and current revision, stale/conflict errors do not auto-repeat, expired projections do not strand legitimate closure, old candidate/pending/actor/stable-ID history stays readable, no mTLS activation promise or raw translation keys. |
| R01-18 | Existing router/server Admin/PQE/IPQA boundaries, login/refresh/logout/password/user management, legacy owner/non-owner Draft/Submit, single/batch review and Completed HTML/XLSX remain unchanged. Valid-looking LineShared fixtures still cannot execute or be projected through legacy reports. |
| R01-19 | Fresh/populated SQLite migrations and constraints, original history/report comparisons, unchanged EF migration/model/snapshot inventory, both-provider pending-model checks and PostgreSQL SQL regressions pass; no R32-01 schema migration. |
| R01-20 | Backend/Web Release/full CI and whitespace/secret checks pass at the exact proposed PR HEAD. Report real PostgreSQL execution separately; Android/on-site/real-CA evidence is not inferred from software or SQLite. |

## 8. Verification gates and current evidence

### 8.1 Executed in this documentation/audit task

| Check | Result |
| --- | --- |
| Branch/local/cached remote/live GitHub HEAD inspection | PASS, matching SHA in §1; direct shell remote query unavailable, connector fallback succeeded |
| Complete revised draft reads and scoped implementation/call-path inspection | PASS, static audit; no runtime claim |
| Formal approval/date, filenames, cross-links and implementation stop boundary | PASS |
| All 47 original case IDs and complete row text retained | PASS |
| Historical Rev3.1 and original Handoff SHA-256 unchanged | PASS |
| Markdown local-link/structure/fence/UTF-8/whitespace checks | PASS |
| Git before/after tracked and index diff / whitespace; untracked draft-to-approved no-index comparison | PASS; application code remains unchanged, document differences are approval/reference/boundary metadata |
| Backend/Web builds and tests, HTTP/browser behavior | NOT RUN: this is the authorized readiness audit, not implementation verification |
| EF runtime/model checks, fresh/populated database tests, generated PostgreSQL SQL | NOT RUN in this task; test implementations inspected and commands planned below |
| Live PostgreSQL / IT signer execution | NOT RUN; no disposable endpoint or real adapter supplied/used. Report SKIPPED if unavailable during implementation verification. |
| Android/PAD/TLS connectivity tests | NOT RUN and outside R32-01; ordinary HTTPS reachability/trust remain PENDING external gates for later Android work |
| R32-01 GitHub CI | NOT RUN: no implementation commit/PR exists |

`docs/14_P103A_VERIFICATION.md` records 187 backend and 72 Web passing tests for the earlier PR. Those are historical P1-03A results, not new R32-01 or Rev3.2 results.

### 8.2 Commands for the separately approved implementation

From the repository root, use the repository .NET 10 / Node 22 CI versions:

```powershell
dotnet restore api/JaxPower.Ipqc.slnx
dotnet build api/JaxPower.Ipqc.slnx --configuration Release --no-restore
dotnet test api/JaxPower.Ipqc.slnx --configuration Release --no-build --filter "FullyQualifiedName~DeviceProvisioningRetirementTests|FullyQualifiedName~EnrollmentRevocationTests|FullyQualifiedName~DeviceEnrollmentConcurrencyTests|FullyQualifiedName~ProductionLineManagementTests|FullyQualifiedName~ProvisioningAuditTests"
dotnet test api/JaxPower.Ipqc.slnx --configuration Release --no-build
dotnet test api/JaxPower.Ipqc.slnx --configuration Release --no-build --filter "FullyQualifiedName~WorkflowLineMigrationTests|FullyQualifiedName~ProvisioningMigrationTests|FullyQualifiedName~EnrollmentMigrationTests"
npm --prefix web ci
npm --prefix web run lint
npm --prefix web run test
npm --prefix web run typecheck
npm --prefix web run build
git diff --check
git diff --cached --check
```

The migration filter includes both InlineData providers in `ProvisioningMigrationTests.Shared_snapshot_has_no_pending_model_changes_for_either_provider`, original populated historical upgrade cases and PostgreSQL SQL assertions. Verify their actual coverage/results after fixture adaptation; passing a filter that discovers no tests is not PASS. If separate EF CLI checks are used, run `dotnet tool restore` and `dotnet-ef migrations has-pending-model-changes` for each configured provider using nonproduction configuration; do not scaffold a migration to satisfy the check. Review the migration/model diff as empty.

Run live PostgreSQL transaction/race cases against a disposable database only when available. Report count, provider, commands, failures and environmental skips precisely. After an authorized implementation PR exists, verify `.github/workflows/ci.yml` backend and frontend jobs against its exact HEAD; do not trigger/push CI during this audit. Scan only the scoped diff for secrets/private-key material and confirm all test issuer artifacts remain temporary and outside Git. No Android build or P1-03B transport/CA integration is a merge prerequisite for this retirement PR.

## 9. Risks, dependencies and explicit exclusions

No architecture or schema blocker prevents this source change. Implementation approval is the current execution boundary.

Specific risks to close in R32-01: worker/service bypasses, wrong shared ReleaseAsync branch, accidental line-tab gating, omitted issued-unconfirmed/expired history, queue starvation, stale cleanup lease completion and fixture setup that accidentally reopens retired APIs. These are covered by §§3–7.

Operational inputs remain conditional: an inventory of actual outstanding issuance/revocation operations, original environment/issuer/journal custody and absence of old signing workers during a later rollout. The audit inspected repository code and synthetic fixture design, not a production database or IT signer. If no adapter is available, keep obligations pending; do not add a production CA integration or claim cleanup complete. A live PostgreSQL server remains a separate environment gate, not a reason to expand the schema.

Keep ordinary server URL/port/TLS trust and tablet security/update assessment as later PENDING inputs; do not invent infrastructure. Both tablets remain supported by the approved target, with one minSdk30 codebase. R32-01 does not require a tablet, Android SDK or certificate installation.

Out of scope: Android/probe implementation, PersonalSessionId, offline receipts, WorkContext/F1, LineShared activation or execution/drafts/photos/Submit, changes to F2/F3 business semantics, task-state additions, NativeSession/OfflineGrant, device activation, P1-03B/mTLS, CA issuance/renewal, destructive migrations, history rewrites, anonymous-challenge retention purges, line-lifecycle redesign and deployment. Retired Bootstrap creates no new receipt challenges; do not bundle a historical purge or a new abuse-protection platform.

## 10. Smallest execution sequence after separate approval

1. Reverify local state and live origin/main. Use this SHA as the audited baseline; investigate material main advancement before relying on old line references. Preserve all unrelated work and the newly approved local documents. Only after explicit Stage B authorization create `codex/r32-01-device-enrollment-retirement` from verified current main.
2. Add synthetic pre-retirement history/receipt fixture setup and focused negative/security/closure cases. Do not put a configuration-dependent successful enrollment seam in the application.
3. Implement immutable retired Admin/Bootstrap responses and matching service guards; preserve all retained permissions/line/read/cancel/revoke paths. Update options/DI so old flags cannot sign or reopen endpoints.
4. Implement opt-in bounded cleanup-only processing and runtime issuer guard, preserving lifecycle OR, original operation/identity/lease and atomic audit. Test unavailable, Unknown, definitive no-issue, signer failure/acknowledgement and finalization rollback/races.
5. Update only the six Web production files and their three tests, keeping both languages and line administration functional. Add short active-guidance supersession notices; preserve historical evidence.
6. Run all applicable gates in §8; inspect the final diff for unchanged model/migrations/business services and unintended fields/permissions. Report live environment gaps separately.
7. Make reviewable commits (API/runtime guard; cleanup/regressions; Web/docs as practical), push the feature branch and open a single review PR only under the later implementation authorization. Include exact HEAD, changed files, migration-free evidence, test counts and PASS/FAIL/SKIP results. Do not merge or deploy automatically.

## 11. Ready-to-use R32-01 implementation prompt

The following prompt is prepared for the next explicit implementation approval. **It is not being executed by this audit.**

```text
Implement R32-01 — Device Enrollment Retirement, ONLY AFTER Peter explicitly approves
this implementation scope. Architecture Rev3.2 was formally signed off 2026-11-10;
do not reopen its decisions or resume P1-03B.

Repository: peterl-ai/IPQC-system.
Audited main: d24eed7adc6f7302bacd02182ce5a7417fe15b1e.
Read AGENTS.md, CLAUDE.md, README.md and these complete authoritative documents:
docs/architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md
docs/architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md
docs/architecture/IPQC_R32-01_IMPLEMENTATION_READINESS_REPORT_2026-11-10.md

Verify current branch/status and live origin/main. Preserve unrelated untracked work
and the locally finalized architecture/report documents. Investigate material baseline
drift. After implementation authorization, create the codex/r32-01-device-enrollment-retirement
feature branch from verified main; do not modify main directly.

Use the exact touchpoint inventory, endpoint matrix and 20 R32-01 cases in the report.
Preserve all Admin ProductionLine operations/validation, historical device/enrollment
GET/audit contracts, controlled device revoke and enrollment cancel, existing RBAC,
personal JWT authentication and legacy Plan/Task/review/report behavior.

Retire device create/edit/bind/rebind/unbind/replace and enrollment invite/approve:
keep existing route permissions, then return 410 ProblemDetails code
DeviceProvisioningRetired without body/target validation or side effects. Preserve
normal 401/403 ordering. All four anonymous Bootstrap operations return unavailable
404 and cannot be re-enabled by environment/options/headers. Guard corresponding
service methods too, before transactions. ReleaseAsync(revoke:true) stays live;
ReleaseAsync(revoke:false) is retired. No new activation route or authority state.

Old BootstrapEnabled/DevelopmentIssuerEnabled/WorkerEnabled cannot enable signing.
Introduce only opt-in CleanupEnabled=false for bounded original-operation cleanup.
Default runtime does not load issuer material or sign. Runtime issuer IssueAsync
always refuses; the processor has no Issue path, even after Recover=NotIssued.
Keep login/line management working when unused old configuration remains present.

Process persisted open/issued-unconfirmed/expired-projection/no-candidate requests
and pending Failed/Revoked/Unknown credentials through existing non-authorizing
closure states. Preserve all immutable identity, versions, approval facts and original
IssuanceOperationId/journal. Keep CloseAsync's OR preservation of pending, including
Failed + no PEM. Respect original environment/issuer and leases, avoid queue starvation.
External Recover/Revoke run outside DB transactions. Unknown or missing journal is
not success. Clear pending only after a durable signer acknowledgement/fence against
late issuance plus atomic database/result-audit commit. Failed or stale finalization
retries the same operation; external calls may repeat, committed acknowledgement must
not duplicate. Never transfer credentials, resurrect trust or release reservations
silently. Retain the existing reservation-based line deactivation guard.

Web: retain line tab management; device/enrollment surfaces become historical with
only applicable reason/revision-checked revoke/cancel, pagination and stable audit IDs.
Remove retired action handlers/callers, invitation secret and approval UI. Keep EN/ZH
accurate; remove future-mTLS activation promises. Do not strand persisted-open records
just because the read projection says Expired. Keep router/permission mappings.

Use synthetic pre-retirement DB rows/public receipts for test setup. Do not reopen
production HTTP/service paths or add a Testing enable flag to satisfy obsolete tests.
Replace obsolete success expectations with negative/history/cleanup regressions and
retain meaningful pure crypto, migration, history, RBAC and legacy coverage.

No EF model/entity/snapshot/migration change is expected. If genuinely necessary,
report its exact reason before adding a migration. Do not delete data, downgrade,
rewrite history, add Android/personal sessions/WorkContext/LineShared execution,
change F2/F3 business semantics, implement mTLS/NativeSession/OfflineGrant or deploy.

Run backend Release build, focused/full tests, Web lint/tests/typecheck/build, fresh
and populated SQLite upgrades/foreign keys/history comparisons, both EF provider
model checks, existing PostgreSQL SQL tests, whitespace/secret checks and PR CI.
Run disposable live PostgreSQL races if available; otherwise explicitly SKIPPED.
Historical 187/72 test counts are not current proof. No simulated PAD or real-CA PASS.

After approved implementation, use reviewable commits and one PR for review. Report
PR URL, branch, final SHA, exact changed files, migration-free evidence, acceptance
and cleanup/audit/security results, PASS/FAIL/SKIP counts and remaining risks.
Do not merge or deploy. Do not include unrelated untracked files or production secrets.
```

**Stop condition for this task:** documentation finalization and this read-only readiness report/prompt are complete. Wait for explicit R32-01 implementation approval; no new architecture approval round is requested.
