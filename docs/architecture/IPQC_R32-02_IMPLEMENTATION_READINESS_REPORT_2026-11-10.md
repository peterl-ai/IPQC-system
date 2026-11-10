# R32-02 implementation-readiness audit and implementation prompt

Date: 2026-11-10. Repository: peterl-ai/IPQC-system.

**Status: Stage A complete; proposed implementation scope and acceptance criteria, awaiting separate implementation approval.** No R32-02 application code, branch, migration, commit or PR was created. This report is the only new audit artifact. Architecture sign-off is already complete and is not being reopened.

## 1. Authorized R32-01 merge result

| Check | Observed result |
| --- | --- |
| PR | [#12](https://github.com/peterl-ai/IPQC-system/pull/12) |
| Expected and verified PR HEAD | `92a0f20a6ac8c68075f0e6a2a98b5544b97d9dd2` |
| Current-head CI before merge | PASS, backend and frontend successful; PR CI run `42893889835` |
| Review/conflict checks | PASS, merge state CLEAN, mergeable; no submitted blocking reviews or unresolved review threads found |
| Protection | GitHub reported main protection not configured and applicable branch rules empty; no bypass/admin override used |
| Scope | Same reviewed 37 files; no Data/Domain changes, new EF migration or unexpected expansion |
| Merge convention | Repository-standard merge commit, with expected-head guard |
| Merge commit | `bcbb842f1f53def49c890986354d6cebfe3312f8` |
| Merge parents | `d24eed7adc6f7302bacd02182ce5a7417fe15b1e`, `92a0f20a6ac8c68075f0e6a2a98b5544b97d9dd2` |
| Final PR state | MERGED / closed, merged at `2026-11-10T21:11:08Z` |
| Updated local / remote main | Both verified as `bcbb842f1f53def49c890986354d6cebfe3312f8`; local fast-forward successful |
| Post-merge main CI | [Run 42902538373](https://github.com/peterl-ai/IPQC-system/actions/runs/42902538373): PASS, backend 182 passed / 0 failed / 0 skipped; Web 68 passed, 16 files; Release build, lint, typecheck and Web build successful |
| Deployment / production cleanup | NOT PERFORMED; no production setting or database changed |

The following audit uses that merged main, not the former PR branch. Existing unrelated untracked walkthrough, handoff, review and prompt files were preserved. Historical verification claims in `docs/15_R3201_VERIFICATION.md` remain historical; the main CI results above were independently queried after merge.

## 2. Authority, evidence and recommendation

Read the approved [Rev3.2 architecture](IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md), [Handoff Supersession](IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md), AGENTS.md, CLAUDE.md and README.md, together with actual authentication, user administration, provisioning, task, reporting, EF, Web authentication and test code. Older documentation's assignee/device descriptions are historical where explicitly superseded. Embedded historical instructions to begin R32-01 do not override Peter's current authorization to merge it and audit R32-02.

Recommend **one backend-focused R32-02 PR**, with three reviewable commits: (1) additive session schema and atomic lifecycle, (2) protected-receipt contract and personal-authorized line/task reads, (3) remaining security/provider/compatibility tests and operating documentation. The read foundation must not enable execution. No Android project or new Web business screen is necessary. Existing Web authentication and permission regressions remain required.

The difficult part is making all writers of account/session authority participate in one transaction protocol. A session-ID-only patch would leave refresh/logout/reset races. The read API itself is small. If this work reveals an additional identity platform or business state transition is necessary, report the concrete scope conflict instead of silently including it.

## 3. Source findings: current behavior versus required work

Paths below are repository-relative. References describe source inspection, not executed exploitation or R32-02 test results.

| Actual file / location | Current behavior and implementation implication |
| --- | --- |
| `api/JaxPower.Ipqc.Api/Domain/ApplicationUser.cs` | Contains both ApplicationUser and RefreshTokenSession. Token row Id rotates; no stable personal-family ID or personal environment binding. Do not invent a nonexistent RefreshTokenSession.cs. |
| `api/JaxPower.Ipqc.Api/Services/LocalAuthService.cs:28` | Login verifies a password then reloads the user before issuing. Reload checks activity/lockout, without revalidating the password/hash/security snapshot. Concurrent password reset/change can invalidate the original proof; rehash must never overwrite a newer hash. This is a source-derived race exposure, not a reproduced exploit in this audit. |
| `api/JaxPower.Ipqc.Api/Services/LocalAuthService.cs:59` | Refresh conditionally revokes one row, then separately saves its successor/replacement link. There is no explicit transaction across both steps. A failed save can consume the old token without a successor. Absolute expiry is already retained and must remain so. |
| `api/JaxPower.Ipqc.Api/Services/LocalAuthService.cs:78` | Logout revokes only the supplied token hash if still unrevoked. A rotated ancestor or refresh racing logout can leave a successor live. Caller identity is not supplied to the service. |
| `api/JaxPower.Ipqc.Api/Services/LocalAuthService.cs:87` | Self password change updates AuthVersion and revokes sessions using separate operations without an explicit encompassing transaction. |
| `api/JaxPower.Ipqc.Api/Services/ApplicationUserService.cs:83` | Role/active changes already use Serializable and protect self/final Admin and active legacy assignments. They must coordinate with the same session writer protocol. Preserve those guards. |
| `api/JaxPower.Ipqc.Api/Services/ApplicationUserService.cs:123` | Admin reset revokes sessions before SaveChanges without one explicit encompassing transaction. Unlock/display updates also touch account state; keep their existing semantics while preventing lost concurrent security changes. |
| `api/JaxPower.Ipqc.Api/Program.cs` | JWT authentication checks signature/issuer/audience, token times with existing 30-second tolerance, and current user activity/AuthVersion; permissions derive from the DB role. No family check. Add family validity only on the new personal surface, not globally on legacy APIs. |
| `api/JaxPower.Ipqc.Api/Endpoints/AuthEndpoints.cs`; `Contracts/AuthContracts.cs` | Existing login/refresh/logout/change-password/me contracts, Native JSON delivery and Web HttpOnly/SameSite Strict cookie are reusable. Add session metadata without removing existing fields or changing routes. |
| `api/JaxPower.Ipqc.Api/Security/IpqcPermissions.cs` | Admin/PQE have review/business permissions; IPQA has ExecuteOwn and record permissions. Admin/PQE do not implicitly become IPQA executors. Add an explicit personal line-read permission for IPQA. |
| `api/JaxPower.Ipqc.Api/Services/ProductionLineService.cs`; `Endpoints/ProductionLineEndpoints.cs` | Directory is Admin-only and permits arbitrary maintained lines. No personally authorized two-line selection configuration exists. Preserve Admin line writes, normalization, audit and reservation-based deactivation. |
| `api/JaxPower.Ipqc.Api/Domain/PatrolPlan.cs` | Also contains PatrolTask and frozen header/item definition fields. Task owns WorkflowVersion and nullable LineId independently of Plan. It does not contain the future DraftVersion, WorkContext or revised inspection-attempt model. |
| `api/JaxPower.Ipqc.Api/Services/PatrolTaskExecutionService.cs:18` | List and Get filter LegacyPersonalV1. The old DTO contains AssignedInspectorKey and mutable legacy result/submission semantics. Do not reuse it for LineShared. OwnsAsync remains relevant only to legacy owner checks. |
| `api/JaxPower.Ipqc.Api/Endpoints/PatrolTaskEndpoints.cs` | `/my`, detail, draft, Submit and review use legacy role/owner policies. Leave routes and owner-first 403/workflow 409 semantics intact. |
| `api/JaxPower.Ipqc.Api/Services/PatrolTaskGenerationService.cs` | Scheduler creates legacy snapshots and rechecks legacy policy at insertion. R32-02 must not enable LineShared plans/generation or fix the separately documented stale scheduling-configuration race. |
| `api/JaxPower.Ipqc.Api/Services/CompletedPatrolRecordService.cs` | Legacy-only Completed query/report projection already protects against shared data masquerading as personal reports. Preserve it and exact approved-history facts. |
| `api/JaxPower.Ipqc.Api/Data/IpqcDbContext.cs` | Refresh has unique TokenHash, UserId/RevokedAtUtc index and restrictive user FK. Task has workflow/non-null-shared-line checks, restrictive line FK and occurrence uniqueness. No dedicated line/workflow paging index. |
| `api/JaxPower.Ipqc.Api/Data/Migrations/IpqcDbContextModelSnapshot.cs` | Shared snapshot clears inferred provider store types; preserves historical long FK/index names. Keep normalization and test both providers. Eight existing migrations end at `20261109202320_P103ADeviceEnrollment`; R32-01 added none. |
| `web/src/services/authSession.ts`; `authFetch.ts`; `permissions.ts`; `types/auth.ts` | Web keeps access token in memory, uses cookie refresh and single-flight refresh, then `/api/me`; UI role navigation is independent. Extra auth response fields are ignored. No Web line-selection UI or token persistence change is needed. |
| Existing backend tests | LocalAuthUserRbacTests covers normal rotation/logout/reset, cookies, lockout, RBAC and historical identity migration; it does not prove stable-family lifecycle/atomicity. WorkflowLineFoundationTests already has adversarial shared records with valid legacy owners/statuses, including N/A/results; retain them. |
| Migration tests | CaptureAsync in WorkflowLineMigrationTests can compare every remembered pre-upgrade table/column. CopyHistoricalFactsAsync omits refresh/enrollment tables; do not assume it alone seeds a populated latest-schema upgrade. Explicitly populate those rows. ProvisioningMigrationTests already compares both provider models without connecting. |

There is no deployment database connection or on-site data inventory authorized/used here. Source fixtures prove that incompatible LineShared rows can exist; they do not prove the real installation has zero or any such rows. Require a separately authorized read-only deployment inventory before later activation; no automatic conversion, repair, inferred mapping or snapshot reconstruction.

## 4. Proposed session design and concurrency contract

### 4.1 Minimal additive family identity

Keep existing refresh rows rather than adding a device/session platform or separate family table:

- Add nullable `Guid? PersonalSessionId` and nullable `string? PersonalSessionEnvironmentId` (max 200) to RefreshTokenSession. The second field freezes ordinary application environment identity across rotation; do not reuse `Enrollment:EnvironmentId`, which must keep its original cleanup meaning.
- New successful logins create a random, nonempty personal-family GUID; existing rotating row Id still changes on every refresh. Store the configured personal environment on creation and copy it unchanged on rotation. If the new environment setting is absent, ordinary legacy login remains usable, but its environment-less family cannot access the new surface. After configuration, normal fresh login is required; refresh cannot promote that old family into a new environment.
- Existing rows remain NULL/NULL. Their refresh descendants remain NULL/NULL and retain old absolute expiry. Do not backfill IDs by username, infer chains from timestamps, or silently promote legacy refresh sessions.
- New JWT claims: `personal_session_id`, and `personal_environment_id` when configured. Add nullable AuthOutput fields `personalSessionId`, `personalSessionEnvironmentId`, `personalSessionExpiresAtUtc`. Preserve all existing fields and delivery modes. Claims and DB facts must agree; response metadata is not authorization.
- Keep ExpiresAtUtc as the family's already-existing absolute expiry, identical in every successor. No sliding 14-day renewal; short access token expiry also never overrides the family deadline.
- Unique filtered index on PersonalSessionId where it is non-null and RevokedAtUtc is null: at most one current token row per family. Add `(UserId, PersonalSessionId)` history/closure lookup index. Retain TokenHash uniqueness and all existing foreign keys.

No receipt, family secret, password or raw refresh token is stored in a new table. No WorkContext/history security-revocation model is introduced in this PR.

### 4.2 Transaction protocol shared by account/session writers

Create a small PersonalSessionStore abstraction for account/session transactions, independent of ProvisioningStore and its device advisory lock:

1. SQLite: open an explicit non-deferred writer transaction before authoritative account/token reads. Never emit PostgreSQL row-lock SQL. Use short transactions and bounded busy handling.
2. PostgreSQL: transaction plus parameterized `SELECT ... FOR UPDATE` on the target ApplicationUsers row before reloading authority/token facts. All login/refresh/logout/reset/change-password/role/activity/unlock writers acquire it. Preserve Serializable protection for cross-user final-Admin invariants; handle serialization/deadlock outcomes without weakening them. Define one lock order, account before token rows, and sorted account IDs if multiple accounts must be locked.
3. Authentication may do an initial non-authoritative lookup to identify the lock target. Password proof must be checked against the authoritative locked hash, or against an unchanged hash/security snapshot under lock with re-verification/rejection on change. Only commit a rehash of that same validated hash. Rate limiting and current lockout rules remain.
4. Rotation rechecks current token/user/version/expiry under the lock, conditionally consumes the old row, inserts one successor with identical family/environment/absolute expiry and saves the replacement link in the **same transaction**. Flush the old-row revocation before inserting the successor if needed by the filtered unique index; both saves still share one transaction. A failed insert/link/commit rolls the entire rotation back.
5. Logout resolves a family from the authenticated signed claim or, when that claim is absent, an owned supplied refresh token, including its rotated ancestor. Revoke its live successor(s) in the same protocol. The signed family is authoritative: an inconsistent supplied token is ignored for target selection and can neither redirect nor prevent that family's closure. A foreign-user token never revokes another user's family. Preserve idempotent 204 and cookie clearing. Legacy NULL-family rows keep token-scoped behavior, with caller ownership enforced.
6. Password/self-change/Admin reset/role/activity security changes update account state/AuthVersion and revoke all current refresh rows atomically. A rollback preserves both account and sessions. Display-only edits and unlock do not start revoking valid families. Preserve existing user validation, last-Admin and assignment guards.
7. After security closure wins, no racing operation can commit a successor using the old authority. A login that wins first is included in the following all-session revocation; a login after a password change must prove the new password. Other independent families survive ordinary single-family logout.
8. Duplicate refresh: one winner; the consumed-token loser gets existing 401 behavior. Do not automatically revoke the winning family for an ordinary duplicated request. A committed refresh with a lost response is not reversible and does not gain a replayable-token recovery API; client retries are bounded and may require online login. A rolled-back refresh leaves the original token usable.
9. Busy/deadlock/serialization exhaustion is a transient 503 on auth/new read paths, or existing user-administration conflict mapping where applicable; do not report account invalidity/401 solely for database contention. No token response/cookie is sent before commit.

Provider rationale was checked against [Microsoft.Data.Sqlite transactions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions) and [PostgreSQL explicit locking](https://www.postgresql.org/docs/current/explicit-locking.html). These references support different provider mechanisms, not evidence that the proposed protocol has passed live tests.

### 4.3 Current family validation and ended-session reconciliation

New personal routes validate the current signed JWT, DB user, role/forced-change state, AuthVersion and exactly one well-formed family/environment claim. Match UserId, family, environment and auth version against a current unrevoked refresh row with ExpiresAtUtc strictly later than current server UTC. Missing/malformed/mismatched/revoked/expired family cannot be repaired from a client-supplied body, cookie, DeviceId or old receipt.

Use a single authoritative join/query for current user/family validation; avoid a pre-read of a revoked row followed by a later family guess. A committed rotation exposes either old current row or new current row, never a committed gap. Concurrent reads may linearize before a revocation; do not claim to cancel an already-authorized in-flight response. Requests validated after closure must fail. Receipt verification and its time anchors must use one consistent authoritative validation boundary. Do not cache positive family authorization across requests or turn contention into a false logout.

Add an owner-only `POST /api/auth/personal-sessions/{personalSessionId:guid}/revoke` to reconcile a user's explicitly ended older family after that same user reauthenticates. It requires current valid personal-family authentication, not IPQA business eligibility; therefore Admin/PQE and forced-change users may close their own families. The target must resolve to the current UserId. Return idempotent 204 for absent/already-closed/foreign targets without disclosing ownership, and never modify a foreign family. This is not an Admin session-console feature.

Offline A logout removes A's reusable credentials and locks local resume immediately. Shared pending-logout bookkeeping holds only non-secret family identification/status. B cannot use it to act as A or close A's family. The pending item waits for appropriately authorized same-user reconciliation or existing Admin account-security closure; otherwise the server family expires naturally. This accepted disconnected limitation must be documented, not bypassed by storing A's reusable token in shared work. R32-02 supplies the server closure contract; Android persistence/worker behavior is a later phase.

## 5. Configuration, APIs and authorization

### 5.1 Ordinary application configuration

Proposed settings, supplied by operations/test fixtures, never invented production values:

| Setting | Contract |
| --- | --- |
| `PersonalAccess:EnvironmentId` | Stable non-secret application environment ID, bounded nonempty string, unique per isolated deployment. Separate from old enrollment environment. Frozen into new families/JWTs/receipts. Changing it requires fresh personal login; it is not a device epoch or endpoint hostname. |
| `LinePatrol:LineIds` | Ordered array of exactly two distinct, nonempty real ProductionLine GUIDs; position supplies selection order. Each must currently exist and be active. No seed, APK constants, code/name inference, device reservation or fixed tablet mapping. |

Absent/malformed personal environment causes the new personal business endpoints to return 503 `PersonalAccessConfigurationUnavailable`; it does not disable existing login, Web, Admin line management or legacy APIs. Invalid/missing/inactive two-line configuration gives 503 `LineConfigurationUnavailable` on directory/task reads, with no partial/fabricated directory. Verify receipts need environment/family, not directory readiness. Runtime line deactivation is rechecked on each new line/task read. A valid configured pair never derives eligibility from RegisteredDevices, including their old reservations.

Do not put actual GUIDs or a made-up server URL/port/CA into appsettings.json. Test fixtures supply synthetic IDs. No new TLS listener or proxy-identity feature. Existing ordinary HTTPS/same-origin deployment remains the boundary.

### 5.2 Proposed route contracts

All new personal responses carry `Cache-Control: no-store` and use UTC `Z` timestamps. Stable machine-readable errors use ProblemDetails `code`; do not depend on localized text for client branching. No secret appears in URLs, responses other than the existing token delivery, or logs.

| Route | Authorization / input | Success / purpose |
| --- | --- | --- |
| Existing `POST /api/auth/login`, `/refresh` | Existing login/token rules; atomic protocol above | Existing AuthOutput plus nullable session metadata; no automatic offline receipt creation for an ineligible user |
| Existing `POST /api/auth/logout`, `/change-password`, `GET /api/me` | Existing routes, body/cookie/status and forced-change compatibility | Family-aware logout for new sessions; atomic existing all-session security closure; `/me` shape unchanged |
| `POST /api/auth/personal-sessions/{personalSessionId:guid}/revoke` | Current authenticated personal family, own target only; no cross-user closure | 204, owner-scoped idempotent reconciliation |
| `POST /api/line-patrol/access/verify` | Active eligible IPQA, valid current family/environment; empty input | OfflineAccessReceipt v1 defined below; verification is not WorkContext issuance |
| `GET /api/line-patrol/lines` | Same IPQA policy, valid configured pair | Ordered `items: [{id, code, name, selectionOrder}]`; current names from ProductionLines, no device/history data |
| `GET /api/line-patrol/tasks?lineId={guid}&page=1&pageSize=20` | Same IPQA policy, mandatory permitted actual LineId | Paged read-foundation summaries; LineId/workflow filters before count and pagination |
| `GET /api/line-patrol/tasks/{id:guid}?expectedTaskLineId={guid}` | Same IPQA policy; optional expected line assertion | Read-foundation detail from persisted task/frozen definitions; mismatch is 409, never a reassignment |

Dedicated permission `LinePatrol.Read` is granted to IPQA only; do not broaden Admin/PQE or reuse Admin ProductionLines.Read. A separate valid-family-only policy supports owner session closure. Global JWT validation remains compatible with already-issued legacy tokens; do not add a global SID requirement. ClientType=Native changes token delivery only.

Authorization/error order: invalid/absent current JWT or account/AuthVersion => 401; authenticated wrong role or forced-change => 403 on new business routes. After those checks, unavailable personal-environment configuration is 503; otherwise an eligible JWT lacking/invalid family/environment => 401 `PersonalSessionRequired` / `PersonalSessionInvalid`, requiring normal online reauthentication. Then validate line configuration and request shape where applicable. Missing/unsupported/out-of-scope task => 404; a resolved authorized task with mismatched expected line => 409 `TaskLineMismatch`. Input line outside the configured pair => 404; missing/empty/malformed LineId or invalid paging => 400. Pagination page >=1, pageSize 1..100 and offset overflow checks are explicit. Configuration failure is 503, not an empty success. Do not reveal task existence before authorization.

### 5.3 Offline receipt and per-user task-access contract

`OfflineAccessReceipt` JSON v1:

```text
version: 1
userId: authenticated ApplicationUser.Id
personalSessionId: validated stable family ID
environmentId: validated configured personal environment
verifiedAtUtc: server UTC at successful verification
offlineAccessUntilUtc: min(verifiedAtUtc + 72 hours, personalSessionExpiresAtUtc)
personalSessionExpiresAtUtc: original absolute family expiry
```

No expiry grace past the family deadline. Repeated successful online verification may advance the receipt within that deadline; refresh alone, failed requests, task browsing, Save and line switching do not refresh it. Receipt verification does not extend refresh expiry or create another login family. No JWS, new signing infrastructure, bearer capability, receipt-accepting login or server-side offline-grant table.

The later Android app must encrypt/integrity-protect the receipt in app-private personal policy storage and keep only the same last active user eligible for offline resume. Use server/monotonic anchors within a boot, persisted progress/wall-clock anomaly handling after reboot, and lock on explicit logout, expiry or corruption without deleting shared work. New login/user switch and unknown task acquisition remain online. A disconnected account cannot be immediately revoked: on reconnect, DB authority wins with no cached grace. These Android behaviors are documented here and **not implemented/tested on tablets by R32-02**.

Successful task reads include `validatedAccess` metadata: UserId, PersonalSessionId, EnvironmentId, server ValidatedAtUtc and exact TaskId / actual Task.LineId / WorkflowVersion. It is a local cache-eligibility marker, not another receipt or authorization token. It does not extend the personal deadline or grant offline execution. List markers attest only returned tasks, not unreturned pages or whole-line access. Cache keys remain UserId + EnvironmentId + TaskId + actual LineId + WorkflowVersion; shared bytes alone do not grant another user access. No WorkContext, source proof or contributor identity is minted.

### 5.4 Keep the read foundation smaller than R32-03

Use new LinePatrol DTOs and service. Do not alter old TaskSummary/TaskDetail to guess shared assignee/final-inspector meaning. Scope is discovery and existing immutable definition fields, not the future full inspection projection:

- Summary: Id, TaskNo, actual persisted LineId, WorkflowVersion, raw stored Status, scheduled/generated UTC, CurrentRevisionNo, frozen display names and a read-foundation availability/reason. No AssignedInspector/final Inspector projection, no invented DraftVersion, attempt, Submit receipt or new state mapping.
- Query only persisted WorkflowVersion=LineSharedV1 and the requested configured LineId; deterministic ScheduledOccurrenceUtc descending then Id, filter before totals/paging. Ignore assignee fields as authorization even in adversarial rows with a valid legacy owner.
- Detail: exact task/line/workflow and stored Plan/Standard identifiers, frozen header fields and ordered frozen item **definition** fields, including source item IDs and raw SampleCount/PhotoRequirement. Never rehydrate missing snapshots from today's Plan/Standard. Do not expose current mutable legacy answers/samples/N/A as the new shared draft, or legacy submissions/reviews as new shared history.
- For this foundation, only PendingInspection/InProgress with revision 0, no submission/review/final-result history, no populated legacy answer/sample/result metadata and an intact frozen definition is detail-compatible. Legacy IsNa/judgment/measurement/machine metadata must not become shared work implicitly. Require nonempty frozen PlanNo/PlanName/StandardName/LineName, source standard timestamp, at least one frozen item, valid source item identities and positive unique ordering. Optional historical header fields stay nullable; raw definition strings are not reinterpreted as confirmed future policies. Definition validation does not claim future inspection validation has passed.
- List retains truthful basic metadata for in-scope incompatible rows, marks `readFoundationStatus=UnsupportedSnapshot` or `UnsupportedState`, and includes them once in total. Compatible rows are `FoundationOnly`. Detail returns 409 `TaskSnapshotUnavailable` / `TaskReadContractUnavailable` for incompatible rows without exposing fabricated business content. This makes pre-existing records visible for diagnosis rather than silently filtering them out or enabling them. No migration repairs them.
- All new payloads explicitly state `canExecute=false`. A task-access marker is validation of permitted foundation data, not future offline execution entitlement; later read contracts must supply complete workflow/cache readiness before Android inspection is enabled.
- A persisted task's identity wins if its Plan's LineId/workflow/name or Standard later changes. Unknown workflow/LegacyPersonalV1 never goes through the new detail; old endpoints continue isolating shared records. No mutation is triggered by any read.

Full new states, Day/Night generation snapshots, inspection attempts/dispositions/samples/PIC/reference versions, shared drafts and version-aware PQE/reporting remain later phase work. R32-02 does not satisfy or change PQE's approved immediate-read requirement for future submitted shared NG; it cannot produce such submissions. Before any later activation, inventory incompatible existing rows and ship the required new projections/validation. Production may legitimately return no compatible shared tasks at this phase.

## 6. Exact proposed implementation files

These are planned changes, not changes made during this audit. Prefix `api/JaxPower.Ipqc.Api/` applies to the following application paths.

| Existing file to modify | Bounded purpose |
| --- | --- |
| `Domain/ApplicationUser.cs` | Two nullable refresh-family fields; rotating Id preserved |
| `Data/IpqcDbContext.cs` | Family mapping/indexes and task line/workflow paging index only |
| `Data/Migrations/IpqcDbContextModelSnapshot.cs` | Additive model and retained provider normalization |
| `Services/LocalAuthService.cs` | Stable issuance/rotation/logout, password proof and atomic security closure |
| `Services/ApplicationUserService.cs` | Shared account/session transaction coordination, security reset/role/activity rollback; preserve other business rules |
| `Contracts/AuthContracts.cs` | Nullable additive session response fields |
| `Endpoints/AuthEndpoints.cs` | Caller-aware logout, own-family reconciliation route, auth transient response/no-store handling |
| `Security/IpqcPermissions.cs` | Dedicated IPQA LinePatrol.Read; existing roles unchanged otherwise |
| `Program.cs` | Register narrow personal services/options/policies and map new routes; retain global legacy JWT behavior, retirement and HTTPS arrangement |

| New application file | Purpose |
| --- | --- |
| `Services/PersonalSessionStore.cs` | Provider-specific transaction/lock protocol for account/session writes, bounded failure handling |
| `Security/PersonalAccess.cs` | Personal environment/two-line option contracts, current-family validation and dedicated policies/requirements |
| `Contracts/LinePatrolContracts.cs` | Receipt, directory, read-foundation DTOs and validated-access marker |
| `Services/LinePatrolReadService.cs` | Current configured line directory, task filtering/detail/integrity projection; no device joins |
| `Endpoints/LinePatrolEndpoints.cs` | Verify/directory/list/detail with explicit new authorization/error contracts |
| `Data/Migrations/<generated timestamp>_R3202PersonalSessionReadFoundation.cs` | One additive migration, generated only after implementation approval |
| `Data/Migrations/<same generated timestamp>_R3202PersonalSessionReadFoundation.Designer.cs` | Matching provider-neutral target model |

Exact numeric migration timestamp must come from later scaffolding; no migration file was generated now. No new NuGet/Node dependency is expected.

Backend test paths have prefix `api/JaxPower.Ipqc.Api.Tests/`:

| Test file | Planned work |
| --- | --- |
| `LocalAuthUserRbacTests.cs` (existing) | Preserve/extend existing login/cookie/lockout/security-action tests and old-JWT compatibility |
| `PersonalSessionFixture.cs` (new) | Disposable separate-connection SQLite fixture, injectable auth clock/fault/barrier seams, synthetic configured lines/users and token generation; no real employee data |
| `PersonalSessionTests.cs` (new) | Stable identity, deadline, owner closure, historical NULL families, DB-authoritative route checks |
| `PersonalSessionConcurrencyTests.cs` (new) | Forced overlaps and rollback at rotation/account-security boundaries, not only Task.WhenAll timing |
| `PersonalSessionMigrationTests.cs` (new) | Fresh/latest-populated/historical upgrades, original-column comparison including refresh and device/enrollment/audit, indexes/FKs/provider SQL/model checks |
| `LinePatrolReadTests.cs` (new) | Directory/RBAC/filtering/identity/frozen data/unsupported snapshot and legacy isolation |
| `OfflineAccessReceiptTests.cs` (new) | Server clock/env/72-hour/family-expiry/no-bearer contract tests, not Android behavior claims |
| `PersonalSessionPostgresTests.cs` (new) | Opt-in disposable live PostgreSQL upgrade/lock/race tests; absent infrastructure reported SKIPPED explicitly |
| `TestAccounts.cs` (existing, only if required) | Minimal synthetic shared test setup; no change to production seeding |

Existing WorkflowLineFoundationTests, WorkflowLineMigrationTests, ProvisioningMigrationTests, EnrollmentMigrationTests, PatrolTaskWorkflowTests, PatrolPlansAndSchedulerTests, CompletedPatrolRecordTests and DeviceProvisioningRetirementTests run unchanged as regression gates. Reuse their helpers read-only where sufficient; modify them only if a new additive index requires a precise assertion adaptation, never to weaken old guards.

Other planned files:

- `web/src/services/authSession.test.ts`: prove old/new nullable extra auth fields leave Web login/refresh/me/logout unchanged; no new visible strings or business UI.
- `README.md`, `docs/03_RBAC.md`, `docs/10_DEPLOYMENT_CHECKLIST.md`, `docs/11_POSTGRES_BACKUP_RESTORE.md`: additive current-contract notes, personal configuration, owner closure, restore revocation and ordinary HTTPS gates; preserve historical evidence.
- New `docs/16_R3202_PERSONAL_ACCESS.md`: exact wire/receipt/read contracts, acceptance evidence and commands.
- `.github/workflows/ci.yml`: preserve backend/frontend gates and add a disposable PostgreSQL-backed test job with synthetic credentials and explicit opt-in environment. This is test infrastructure, not production deployment. If runner/service availability prevents execution, report that gate SKIPPED, not PASS. The test harness must support local explicitly named disposable targets and refuse an unmarked target.

No default changes to Web authSession.ts/authFetch.ts/types/auth.ts/permissions.ts/router/i18n, ProductionLineService, ProvisioningStore, device/enrollment services, PatrolTask domain/business services or legacy DTO/endpoints. Review these as regression dependencies. An unexpected need to modify them must be explained against this scope. Approved architecture and historical Handoffs remain unchanged.

## 7. Additive migration and legacy compatibility

One proposed migration after `20261109202320_P103ADeviceEnrollment`:

1. RefreshTokenSessions.PersonalSessionId nullable: PostgreSQL uuid / SQLite TEXT; no default/backfill.
2. RefreshTokenSessions.PersonalSessionEnvironmentId nullable, max 200: provider-native string mapping; no default/backfill.
3. Unique filtered current-family index and UserId/family history index described in §4.1, with equivalent predicate for both providers. Existing NULL families excluded from current-family uniqueness.
4. PatrolTasks index `(WorkflowVersion, LineId, ScheduledOccurrenceUtc, Id)` to support the new line/workflow page filter. No Task column/state/owner change.

No new tables, identity conversion, deleted migrations, device cleanup, global data update, changed FK cascades or production seeding. Retain all historical token hashes, replacement links, dates, business rows and append-only audit. SQLite should use additive columns/indexes; review actual generated SQL and stop an unrelated rebuild/drop. PostgreSQL index creation can lock writes; review duration on an appropriate populated disposable dataset and schedule controlled rollout. Do not add an unreviewed concurrent-index/transaction suppression workaround.

Upgrade evidence must compare **every original column** before/after, including live/revoked/expired/rotated refresh rows, security fields, pending Failed/no-PEM external revocations, old candidates, line reservations and typed audit. New fields must be NULL for pre-upgrade rows. Retain restrictive user/line/device/audit FK behavior and verify SQLite foreign_key_check. Test fresh database and populated P103A-to-current, plus historical Phase1G and pre-identity Phase1F upgrades through the full chain. Never query an old schema using today's EF refresh entity before applying its migration.

Preserve snapshot store-type normalization and both-provider pending-model checks without warning suppression. Generated PostgreSQL SQL/model PASS does not prove a live PostgreSQL migration or lock race. Provide separate evidence.

Operational rollback uses a compatible prior application against retained expanded schema, not destructive Down; never remove family fields/history as routine rollback. A prior binary cannot enforce new-family revocation: if rolling back R32-02, disable the new personal business surface and do not advertise offline/LineShared availability. Restore recovery must close ingress, rotate/reconcile personal JWT signing trust as needed and revoke restored refresh authority before reopening authentication; do not let an older database resurrect revoked families. This is an operating runbook contract, not a device epoch or a production SQL action authorized here. F1/Submit/evidence restore reconciliation remains a later implementation dependency.

| Legacy boundary | Required unchanged behavior |
| --- | --- |
| Old JWT without family | Existing `/api/me`, authorized legacy business routes continue until existing token/AuthVersion expiry rules; new personal business API requires online reauthentication |
| Old refresh with NULL family | Continues legacy rotation/delivery at original absolute expiry, no silent upgrade |
| Web cookie | Same HttpOnly, Secure outside Development, SameSite Strict, `/api/auth` path, absolute cookie expiry; access in memory |
| Legacy task owners | Same owner-first 403, supported owner legacy behavior and LineShared owner 409 on draft/Submit |
| Legacy reports/history | Same final immutable submission/review facts, N/A, Machine/Series/Mold, approved-unqualified semantics, times, HTML/XLSX and role permissions |
| Shared isolation | Old Plan/Task/Completed lists filter legacy before counts; old authorized detail/report export cannot project LineShared as personal |
| Device retirement | Same 401/403/410 retired Admin writes, Bootstrap 404, preserved history/controlled closure and truthful pending cleanup; new auth ignores device state |
| Admin line management | Same code uniqueness/normalization/revisions/audit/deactivation guard; new personal directory is a separate projection |
| Ordinary logout | New-family logout closes new-family authority immediately; legacy access JWT remains governed by existing global JWT policy until expiry. Do not silently introduce all-API family checks or bump AuthVersion for a single logout. |

## 8. Focused acceptance and threat-test matrix

**All R02 cases below are proposed / NOT RUN.** R32-01 CI does not prove them. API tests must assert response plus database/token/audit side effects. Use deterministic barriers/fault injection and independent DB connections for races; in-memory fake stores are insufficient.

| ID | Scenario | Required acceptance / evidence |
| --- | --- | --- |
| R02-01 | Fresh login, multiple refreshes, second login | One stable family per login; row IDs/tokens rotate, family/env unchanged, second login different; raw refresh never persisted |
| R02-02 | Access/refresh/receipt near absolute expiry | No sliding extension; new routes fail at family expiry even if access JWT time tolerance remains; UTC serialized with Z |
| R02-03 | Two concurrent refreshes of same token | Exactly one successor/response; one 401, no family split; repeated token cannot mint more rows |
| R02-04 | Fail old-row update, successor insert, link save or transaction commit | Entire rotation rollback; old token still usable, no partial family/link or issued response |
| R02-05 | Refresh transaction paused while new route validates | Readers observe committed current family before/after, no intermediate-gap 401; contention is bounded/transient |
| R02-06 | Logout using current token, rotated ancestor, signed family, missing token | Owned new family closed idempotently; no live descendant after closure wins; unrelated families survive; cookie cleared |
| R02-07 | Logout versus refresh, both interleavings | If refresh wins its successor is revoked; if logout wins refresh fails; no resurrection |
| R02-08 | Same-user new login closes own earlier ended family; B supplies A's ID/token | Own closure 204 idempotent; foreign closure no-op with same response, A unaffected; no token retained in pending shared bookkeeping |
| R02-09 | Login/refresh versus password change/Admin reset/deactivation/role change | Serializes with security mutation; no old-password issuance or live old-version successor after closure; rehash cannot overwrite reset hash |
| R02-10 | Failure after session revoke but before account/security commit | Account/AuthVersion/hash and revocations roll back together; no false success; last-Admin/assignment protections retained |
| R02-11 | Failed-login concurrency, unlock and display-only edit | Existing five-failure lockout and unlock preserved; no lost security update, unintended family revocation or last-Admin race regression |
| R02-12 | Legacy JWT/NULL-family refresh/extra auth response fields | Old routes/cookies/payloads continue; new personal calls require fresh login; no inferred lineage or environment promotion |
| R02-13 | Forged/duplicate/empty SID, wrong owner/env/AuthVersion, no live row | New API denies; JWT verification plus DB authoritative; no body/cookie/device/receipt fallback |
| R02-14 | Anonymous, bad JWT, Admin/PQE, forced-change, inactive and eligible IPQA | Correct 401/403 ordering without target disclosure; IPQA new read permission grants no Admin/PQE review/user rights; own closure uses separate non-business policy |
| R02-15 | Both configured lines, unconfigured third, rename, missing/inactive/config duplicate | Valid pair only; order and actual names accurate; invalid pair visible 503, third-line query404; no seed/mapping/partial directory |
| R02-16 | Same IPQA with no registry, revoked/replaced/candidate/reserved devices | Same valid directory/task access independent of device/certificate/BindingVersion; retired module unchanged |
| R02-17 | Mixed workflows/lines, valid legacy assignee, paging edge/overflow | Shared/actual line filtered before total/page; deterministic order, no duplicates/leakage; 400 invalid paging/LineId |
| R02-18 | Detail resolved actual line versus expected line and later Plan/Standard edits | 409 mismatch, no move; task-owned workflow/line/frozen definitions unchanged; legacy/unknown/out-of-scope404 |
| R02-19 | Null/empty snapshot, zero items, invalid item IDs/order, legacy N/A/result/submission shared fixture | Truthful list availability and total; detail409 for unsupported contract; no today's Standard fallback, legacy-result reinterpretation or execution |
| R02-20 | All new line/task read endpoints | No draft/start/status/assignee/task/audit mutation, WorkContext or credential authority; canExecute=false; no new Submit/worker action |
| R02-21 | Successful receipt at t, repeat at t2, <72h lifetime, exact boundary | Server clock fields, min(t+72h,family expiry), no expiry grace or family extension; only successful verify refreshes receipt |
| R02-22 | Receipt/marker submitted as Authorization or login/refresh input; fake IDs | Rejected as credential; no signing/key service or server receipt fallback; no tokens/proofs in receipt/logs |
| R02-23 | Per-user/task/env markers and local protection contract | Only returned task scope validated; no whole-line/other-user/unknown-task grant; document encrypted integrity state, same-user resume and logout lock; Android behavior NOT RUN |
| R02-24 | New policy during revocation/security commit and transient DB failure | Fresh post-closure request denied; no cached positive auth; no false invalid-account response for contention; linearization/allowed in-flight boundary documented |
| R02-25 | Existing task execution/review/scheduler/Completed report fixtures | All current legacy tests pass unchanged; adversarial LineShared cannot bypass old guards or appear in legacy report; exact frozen identity preserved |
| R02-26 | Fresh SQLite, populated P103A/Phase1G/Phase1F upgrades | Eight historical migrations intact plus one additive; original-column facts/FKs/audits preserved, NULL old family fields and intended indexes only |
| R02-27 | Both EF provider models and PostgreSQL SQL | No pending changes, correct uuid/string/index predicates; no unrelated DDL or warning suppression; generated SQL distinct from execution |
| R02-28 | Disposable PostgreSQL fresh/populated migrations and session races | Real server separate connections exercises R02-03..11/24 critical interleavings and constraint enforcement; missing environment explicitly SKIPPED |
| R02-29 | R32-01 retirement/history/closure/line management regression | All retained RBAC/history/audit/pending obligations pass; cleanup remains opt-in, no new Issue or device activation |
| R02-30 | Web old/new auth responses, route permissions, EN/ZH regressions | Login/refresh/me/logout works with new optional metadata; existing UI access unchanged; lint/test/typecheck/build pass |
| R02-31 | Restore/rollback rehearsal contract | No destructive Down; prevent restored refresh/JWT authority resurrection before reopen; no production action or claim of completed drill |
| R02-32 | Full delivery and scope | Release build/full backend, Web gates, provider checks, whitespace/secrets and exact-HEAD CI evidence; one scoped PR, no deployment/merge/Android/WorkContext/photo/Submit implementation |

Architecture AUTH-01/02/03, LINE-01/02 and COMP-01/02/03 are addressed only to this phase's read/session extent. OFF-01..05/SEC-02 include future Android storage/reboot/worker behavior; R32-02 tests the server contract, not completed device acceptance. SYNC/F1/F2/F3/form/PQE/photo/retention business cases remain proposed for later phases.

## 9. Future verification commands and environment gates

These commands are a later implementation checklist; they were **not executed as R32-02 tests during this audit**:

```powershell
dotnet restore api/JaxPower.Ipqc.slnx
dotnet build api/JaxPower.Ipqc.slnx --configuration Release --no-restore
dotnet test api/JaxPower.Ipqc.slnx --configuration Release --no-build --filter "FullyQualifiedName~PersonalSession|FullyQualifiedName~OfflineAccessReceipt|FullyQualifiedName~LinePatrolRead"
dotnet test api/JaxPower.Ipqc.slnx --configuration Release --no-build
dotnet test api/JaxPower.Ipqc.slnx --configuration Release --no-build --filter "FullyQualifiedName~MigrationTests"
dotnet tool restore
npm --prefix web ci
npm --prefix web run lint
npm --prefix web run test
npm --prefix web run typecheck
npm --prefix web run build
git diff --check
```

Run EF `dotnet tool run dotnet-ef migrations has-pending-model-changes --project api/JaxPower.Ipqc.Api --configuration Release --no-build` twice in scoped child processes: Testing with SQLite `Data Source=:memory:`, then Testing with Postgres model-only dummy connection. Restore process configuration afterward. These checks neither apply migrations nor prove a live connection. Generate/review PostgreSQL idempotent SQL with `Database__Provider=Postgres` and the pinned EF CLI, and exercise the same migration on a confirmed disposable server. Never point development migration commands at an unverified configured database. No production connection string in commands, logs or Git.

For the proposed live PostgreSQL test harness use explicit `IPQC_TEST_POSTGRES_CONNECTION` secret configuration and `IPQC_TEST_POSTGRES_DISPOSABLE=true`; isolate a uniquely named test database per run with scheduler/outbound integrations off. A missing opt-in must produce visible skipped cases, never a vacuous passing return. Check xUnit 2.9.3-compatible explicit skip reporting during implementation. CI uses an ephemeral PostgreSQL service and synthetic test-only credentials; no production environment. Test both lock order/interleavings and rollback on that provider rather than asserting a SQL string alone.

| Gate at end of this audit | Result / interpretation |
| --- | --- |
| PR12 reviewed HEAD, reviews/conflicts/scope checks, authorized merge | PASS, observed |
| PR12 and merged-main GitHub backend/frontend CI | PASS, observed; main 182 backend / 68 Web |
| R32-02 source/model/test/readiness audit | COMPLETE, documentary evidence only |
| R32-02 proposed implementation/tests/new migration/model checks | NOT RUN; no implementation authorized here |
| R32-02 live PostgreSQL migration/concurrency execution | SKIPPED in this read-only audit; no disposable target used or execution attempted |
| Deployment LineShared inventory and configured two real IDs | PENDING external gate; no deployed database inspected |
| Server FQDN/port/TLS trust and both PAD HTTPS connectivity | PENDING IT; no invented endpoint/trust result |
| Actual SM-X210/API35 and SM-T500/API30 offline/reboot/upgrade behavior | SKIPPED, later Android implementation/UAT; one minSdk30 app remains target |
| Production deploy/database changes/cleanup enablement | NOT PERFORMED |

IT/operations must supply ordinary HTTPS FQDN/port and trusted server chain, two actual line IDs, isolated environment ID, disposable PostgreSQL and restore ownership. Public internet availability is not required for reachable internal HTTPS. No device CA, CSR, mTLS, NativeSession or BindingVersion activation dependency is restored. Line 2's old security patch assessment remains an IT release gate. Existing dependency/bundle advisories from R32-01 are not remediation scope here; do not silently upgrade packages.

## 10. Ready-to-use R32-02 implementation prompt

The following is a **proposed next authorization prompt**, not an instruction executed by preparing this report. Peter must separately approve its scope before implementation.

```text
# Codex — R32-02 Stage B Implementation Authorization

Peter approves the R32-02 scope and acceptance criteria in
docs/architecture/IPQC_R32-02_IMPLEMENTATION_READINESS_REPORT_2026-11-10.md.

Repository: peterl-ai/IPQC-system.
Audited baseline: main bcbb842f1f53def49c890986354d6cebfe3312f8 (PR12 merged).
Verify current origin/main, clean tracked/index state and any intervening changes.
Preserve unrelated untracked work. Create codex/r32-02-personal-access-read-foundation
from verified current origin/main; stop and explain any material scope drift.

Read AGENTS.md, CLAUDE.md, README.md, the approved Rev3.2 architecture,
Handoff Supersession, this report and actual current source. Do not reopen
architecture sign-off or edit historical signed-off artifacts.

Implement one scoped PR, in reviewable commits:
1. Stable personal refresh-family ID and frozen personal environment, additive
   nullable response/JWT metadata, unchanged rotating row IDs and absolute expiry.
   Historical NULL families remain legacy-only and never silently upgrade.
2. Atomic provider-appropriate login/refresh/logout/password/reset/role/activity
   lifecycle using the shared account/session transaction protocol. Preserve
   lockout, last-Admin, assignment protections and old Web/legacy API behavior.
   Include rollback, password-proof/rehash races, stale-ancestor logout and
   owner-only ended-family reconciliation after same-user reauthentication.
3. Dedicated new current-family policy and IPQA LinePatrol.Read permission;
   no global family requirement for already-issued legacy JWTs. Validate current
   user/AuthVersion/family/environment/absolute lifetime from the database.
4. POST /api/line-patrol/access/verify: protected-local-policy receipt v1,
   capped at verified server UTC +72h and original family expiry. It is never
   accepted by any server as a bearer/login credential. Specify local encryption,
   same-user resume, clock anchors, ended-session lock and independent shared
   work storage; implement no Android persistence or business code.
5. Personally authorized configured two-line directory and read-only LineSharedV1
   task list/detail routes in the report. Use persisted Task.LineId/workflow and
   frozen definition fields; filter before totals/paging, reject expected-line
   mismatch and surface incompatible snapshots/states truthfully. No assignee
   reporting semantics, mutable shared results or fabricated future fields.
   Every response is non-executing; no task mutations or WorkContext issuance.
6. One additive migration for the two family fields, current/history family
   indexes and line/workflow paging index. Preserve all eight old migrations,
   snapshot provider normalization, restrictive FKs, legacy data and audit.
   No backfill, seed, destructive rollback, historical identity conversion or
   RegisteredDevice dependency. No real production identifiers/secrets in Git.

Implement/test the exact file inventory and R02-01..32 acceptance matrix in
the report. Use deterministic race barriers, independent connections and fault
injection. Verify both SQLite and real disposable PostgreSQL, migration upgrade
from populated latest and historical schemas, both EF models and provider SQL.
Old auth/task/review/scheduler/record/report/retirement tests remain required.
Keep the existing Web UI unchanged; test extra nullable auth metadata and
full Web regressions. Add ordinary configuration, closure and restore notes.

Use PersonalAccess:EnvironmentId and LinePatrol:LineIds as configurable real
deployment inputs; no guessed hostname/port/CA/line IDs, device PKI, environment
promotion on refresh or startup failure of legacy APIs for missing new settings.
Owner session closure cannot impersonate A through B or retain A tokens in
shared work. Keep F1 provenance revocation distinct for its later implementation.

Run backend Release restore/build/focused/full tests; Web lint/test/typecheck/
build; fresh/populated SQLite upgrades; both EF pending-model checks; PostgreSQL
SQL review and real disposable migration/concurrency tests; Git whitespace and
secret checks. Add/run disposable PostgreSQL CI as scoped in the report.
If a live environment is unavailable, report SKIPPED with the precise reason,
not model-only/SQLite/fake PASS. Do not weaken tests or claim Android behavior.
Verify GitHub backend/frontend/PostgreSQL results against final PR HEAD.

Explicitly excluded: R32-03 plan/scheduler activation, WorkContext/F1 writes,
shared drafts, new task states/attempts/Submit/F2, photos/F3, PQE shared-history
implementation, full Android or feasibility apps, NativeSession, OfflineGrant,
device enrollment/certificates/mTLS/binding, production cleanup/deployment.
Preserve Rev3.2, original F2 business invariants and legacy compatibility.

Open a review PR; do not merge or deploy. Return PR URL, branch/final SHA,
changed-file/DDL summary, R02 acceptance evidence, precise PASS/FAIL/SKIP
counts and commands, rollback/migration safety, remaining risks and external
IT/PAD/restore gates. Do not report proposed full-architecture cases as passed.
```

## 11. Stop condition and remaining risks

Stopped after this readiness report and implementation prompt. Local branch remains main at the verified merge commit. No R32-02 code/schema/branch/PR or production action was performed.

Implementation approval must include the two nullable family fields, owner-only closure route, conservative non-executing read projection and provider concurrency tests above. Primary residual risks are concurrency coverage across every account writer, real PostgreSQL locking/DDL execution, restore resurrecting old authority, and unknown deployed shared snapshots. The ordinary offline clock/identity limits remain explicitly accepted architecture tradeoffs, with actual Android protection/behavior deferred. Full F1/F2/F3 and future PQE reads are unchanged requirements for their own phases.
