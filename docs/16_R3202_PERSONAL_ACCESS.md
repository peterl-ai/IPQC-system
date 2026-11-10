# R32-02 personal access: implemented contracts and verification

Date: 2026-11-10. Scope: personal-session lifecycle and non-executing line/task reads.

Authority: [approved Rev3.2](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md), [Handoff Supersession](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md) and the [approved readiness report](architecture/IPQC_R32-02_IMPLEMENTATION_READINESS_REPORT_2026-11-10.md). Peter separately authorized implementation. The readiness report and its 32 proposed cases remain the unchanged planning baseline; actual implementation evidence is recorded here. No Android behavior, full-architecture acceptance, production rollout or cleanup activation is implied.

## Published baseline and change boundary

The report-only commit `d6dc88a93dc8b7ff3e8a6c8293eba2a79c20c05c` was fast-forwarded to main under explicit authorization. Its GitHub contents API bytes matched local intended SHA-256 `1E21DB88D6A1B236C2793FEFD6F2482B021C6E3780BF61F93FA5CFA15B17DECE`, blob `d00b6da0c72f3b5e98feac9960132e542a84a682`. [Exact main report](https://github.com/peterl-ai/IPQC-system/blob/d6dc88a93dc8b7ff3e8a6c8293eba2a79c20c05c/docs/architecture/IPQC_R32-02_IMPLEMENTATION_READINESS_REPORT_2026-11-10.md). Report status: MERGED_TO_MAIN. Application implementation is confined to `codex/r32-02-personal-access-read-foundation`, for review; it is not in main.

No dependency update, device authority, new patrol write/state, assignment conversion, production identifier, server hostname or PKI configuration is included. Existing device audit/cleanup, Admin line mutations, scheduler, legacy execution/review and report implementations remain unchanged. Temporary tests use synthetic credentials and data only. CI's PostgreSQL password is public synthetic ephemeral-service configuration, never an internal credential.

## Session identity, authority and lifecycle

RefreshTokenSession.Id still identifies a rotating token row. Nullable PersonalSessionId identifies its stable login family; PersonalSessionEnvironmentId freezes the ordinary environment. A new login creates a new family; refresh carries both values and the original ExpiresAtUtc unchanged. Existing NULL-family rows and their descendants stay legacy-only. An environment-less family cannot silently acquire an environment by refreshing.

AuthOutput adds nullable personalSessionId, personalSessionEnvironmentId and personalSessionExpiresAtUtc. JWT claims are personal_session_id and personal_environment_id. Existing fields, Native refresh-token JSON, Web cookie delivery/path/SameSite/secure/absolute expiry and in-memory access token remain. ClientType does not establish device trust. Login, forced-password change, password strength, lockout and account/last-Admin/assignment protections remain.

Every existing-account/session writer uses PersonalSessionStore: SQLite reserves the writer before authoritative reads with a non-deferred Serializable transaction; PostgreSQL uses a parameterized account-row FOR UPDATE lock, then authoritative reads. Role/activity changes retain Serializable for cross-account final-Admin invariants. Lock order is account then tokens. Password verification/rehash is based on the locked current hash, not proof against a pre-reset hash. Refresh consumption, successor and replacement link commit together; account security changes and all-session revocation commit together. Failure before commit rolls back both sides. SQLite writer waits are bounded to at most five seconds per attempt; PostgreSQL lock timeout is five seconds, with up to three whole-transaction attempts. Exhaustion is transient 503, not false credential invalidity; legacy user-change conflicts retain 409.

Ordinary logout closes only the signed owned family, including a successor created by a concurrent refresh. An inconsistent/foreign supplied token cannot redirect or prevent that closure. Without a signed family, only an owned supplied token can resolve an existing family or legacy token row. Repeated logout stays 204. Other families survive. Password change/reset and role/activity security changes retain all-session AuthVersion revocation. Unlock/display-only changes do not revoke sessions.

New family verification is per-request and database-authoritative: active user, current AuthVersion, role/forced-change eligibility when required, exact signed family/environment and current unrevoked unexpired row. It runs after JWT/role authorization but before minimal-API request binding. Duplicate/malformed/empty/wrong-user/wrong-environment family claims fail. An atomic rotation exposes no committed gap. A read authorized before closure may finish; a fresh validation after closure fails. No positive authority cache, body claim, receipt or device identity substitutes for this check.

Existing legacy endpoints keep their global JWT rules, including already-issued tokens without SID. A single logout does not globally invalidate their already-issued short-lived access JWT; expiry/current user/AuthVersion still govern them. New LineShared endpoints reject that family's authority immediately. Duplicate refresh has one winner; the loser receives 401 without automatically revoking the winner. If a committed refresh response is lost, bounded client recovery may need online re-login; the old token is not replayable. No response/cookie is sent before successful transaction completion.

## Configuration and route contracts

| Configuration | Meaning |
| --- | --- |
| PersonalAccess:EnvironmentId | Stable non-secret 1..200-character identifier without surrounding whitespace, supplied per deployment. Separate from Enrollment:EnvironmentId and old signer cleanup. Changing it requires fresh personal login. |
| LinePatrol:LineIds | Exactly two distinct nonempty real ProductionLine GUID strings in display order. Both must exist and be active. No labels, legacy text inference, automatic seeds or tablet mapping. |

These settings have no production defaults in Git. Missing/invalid environment returns 503 PersonalAccessConfigurationUnavailable on the new surface, while legacy login and APIs remain available. A missing/inactive/invalid line pair returns 503 LineConfigurationUnavailable on directory/task reads. Receipt verification does not depend on line configuration. Runtime line names/activity are read afresh. Old reservations never restrict personally authorized line reads; Admin deactivation retains its old reservation guard.

| Route | Contract |
| --- | --- |
| POST /api/auth/personal-sessions/{personalSessionId}/revoke | Current valid personal family, owner-only prior-session closure. Active Admin/PQE/forced-change users may close their own families. Missing/closed/foreign target returns the same idempotent 204, without changing foreign authority. |
| POST /api/line-patrol/access/verify | Eligible active IPQA + current family; empty request, OfflineAccessReceipt v1 response. |
| GET /api/line-patrol/lines | Eligible active IPQA + current family; items with id/code/name/selectionOrder for the configured pair. |
| GET /api/line-patrol/tasks?lineId=GUID&page=1&pageSize=20 | Mandatory actual configured line; LineSharedV1 and line filter before total/page; pageSize 1..100, positive page and checked offset. |
| GET /api/line-patrol/tasks/{id}?expectedTaskLineId=GUID | Resolve actual persisted task, line, workflow and frozen definition. Optional expected line is a consistency assertion; mismatch409, never a move. |

Dedicated LinePatrol.Read is IPQA-only; Admin/PQE review rights and Web routes do not broaden. Invalid current JWT/account/version =>401; authenticated wrong business role/forced-change =>403; after these, unavailable environment=>503, missing family=>401 PersonalSessionRequired, invalid/currently revoked/expired family=>401 PersonalSessionInvalid. Then line configuration, input validation and target lookup. Malformed/missing query input=>400; unsupported/outside-configured/legacy/missing task=>404; snapshot/state conflict=>409. Auth and personal responses use no-store. Stable errors use ProblemDetails code; don't branch on visible text.

## Offline contract and safe logout handoff

OfflineAccessReceipt contains version=1, userId, personalSessionId, environmentId, verifiedAtUtc, offlineAccessUntilUtc and personalSessionExpiresAtUtc. All timestamps are server UTC. Deadline is min(verification+72 hours, original absolute family expiry); no grace past family expiry. Only a successful verify call refreshes this receipt; refresh itself, Save, line switching, browsing or failed requests do not. Receipt is local policy state delivered over current authenticated HTTPS; it is never accepted as a bearer/login/refresh credential. There is no receipt database, client certificate, asymmetric receipt signature or device grant.

Task responses carry validatedAccess markers scoped to the authenticated user/session/environment and the returned task's actual line/workflow. Only those returned foundation bytes are attested; no unknown pages, whole-line/offline execution authority or WorkContext. canExecute is false throughout.

The later Android app must encrypt/integrity-protect personal receipt state, keep access JWT in memory, protect refresh ciphertext and exclude credentials from backups. Same-last-active-user resume uses server/elapsed-time anchors within a boot and persisted progress/wall-clock anomaly handling after reboot. Expiry, corruption or explicit logout locks viewing/editing without deleting shared work. New user login/switch and unknown task acquisition remain online. Restart, Save and failed network activity never reset the deadline. These client behaviors are not implemented or hardware-tested by this PR.

Personal credential storage and shared work protection remain independent. A logout must remove A's reusable tokens and stop A's workers while retaining shared bytes/proofs under their separate key. Pending logout bookkeeping contains only non-secret family identification/status. B cannot impersonate A or close A's family from that record. A's later valid same-user login can close the prior family; otherwise existing account-security closure or natural expiry applies. Offline account revocation cannot be immediate; reconnect always revalidates DB authority. WorkContext issuance/security-provenance integration remains a separate phase, and ordinary session closure never rewrites historical contributions.

## Non-executing read foundation

New DTOs contain actual task/workflow/line IDs, raw stored status/revision, frozen display/header/definition fields and availability, without legacy assigned-inspector/final-inspector interpretation. List includes in-scope incompatible rows in totals with UnsupportedState/UnsupportedSnapshot; no silent repair. Compatible rows are FoundationOnly, not ready for inspection. Detail only supports PendingInspection/InProgress, revision zero, no legacy answer/sample/result or submission/review history, intact required frozen headers/source timestamp and nonempty ordered definition items with stable IDs. Missing/corrupt snapshots are never rebuilt from current Plan/Standard. Definitions retain raw historical sample/photo strings; no confirmed new photo policy or DraftVersion is fabricated.

Unsupported detail returns TaskReadContractUnavailable or TaskSnapshotUnavailable (409); legacy or out-of-scope workflow returns404 after authorization. Persisted Task.LineId/WorkflowVersion/frozen names win even if Plan/Standard/directory subsequently change. No read mutates task/status/owner/audit, issues WorkContext, transfers device work or bypasses old execution guards. R32-03 must define full new projections and validate any deployed shared inventory before later activation. Future PQE immediate reads of submitted shared NG remain required and are neither implemented nor weakened here.

## Migration safety and operational rollback

One migration: `20261110215126_R3202PersonalSessionReadFoundation` after P103A. Up adds only:

- RefreshTokenSessions.PersonalSessionId nullable, PostgreSQL uuid / SQLite TEXT.
- RefreshTokenSessions.PersonalSessionEnvironmentId nullable, PostgreSQL character varying(200) / SQLite TEXT.
- Unique current-family filtered index where family is non-null and RevokedAtUtc is null.
- UserId/PersonalSessionId history lookup index.
- PatrolTasks WorkflowVersion/LineId/ScheduledOccurrenceUtc/Id paging index.

No backfill/default/fabricated history, changed cascade, added table, device/data cleanup, rewritten migration or SQLite table rebuild. Eight existing migrations are untouched. Snapshot provider-default normalization remains; both providers must report no pending model changes. Down explicitly refuses destructive family-history removal. PostgreSQL idempotent SQL consists of two ALTER ADDs, three index creates and EF history recording; model/SQL review is distinct from live execution. Index creation still needs controlled rollout and an appropriate database backup; this PR performs no production migration.

Fresh/populated relational tests check uniqueness, retained restrictive user FK, all remembered original columns, NULL historical family values and SQLite foreign_key_check. The populated P103A fixture copies every prior table/column, including live token hashes, revoked/replacement data where present, device/enrollment/challenge/typed audit and Failed/no-PEM external revocation pending. Existing Phase1G/Phase1F/earlier upgrade and legacy report tests remain required. No test fixture contains real employee/production data.

Rollback retains expanded schema and history while disabling new personal business ingress. Prior binaries cannot be advertised as enforcing the new family contract. Restore closes ingress, disables scheduler/outbound integration/cleanup, reconciles account authority, rotates personal JWT signing trust and revokes restored refresh authority before reopening; do not revive tokens from an older backup. See the [restore runbook](11_POSTGRES_BACKUP_RESTORE.md). No automatic restore script, device epoch, production key generation or real recovery drill is included.

## Acceptance evidence map

R02 IDs below refer to unchanged readiness criteria. Actual test totals and final-head CI are reported in delivery; no mapping claims Android/UAT or an unexecuted restore drill passed.

| Case | Evidence / boundary |
| --- | --- |
| R02-01 | PersonalSessionTests: stable family/env/expiry across three rows, new login new family, token hashes only |
| R02-02 | Receipt/expiry tests: strict family expiry independently of still-live legacy JWT |
| R02-03 | Deterministic paused refresh, duplicate request, one successor; live PG counterpart |
| R02-04 | Failure during consumption, successor insert, replacement-link save and pre-commit; original token recoverable after rollback |
| R02-05 | Separate reader during paused rotation sees committed family; no authorization gap |
| R02-06 | Ancestor/missing/foreign supplied token logout, signed target, other families survive |
| R02-07 | Paused refresh versus logout plus logout-first refresh denial; no live successor after closure |
| R02-08 | Same-owner prior family closure, foreign user no-op and forced-change closure; pending local record contract documented, Android NOT RUN |
| R02-09 | Reset/role/deactivation/self-password versus paused rotation; reset-first login rejects old password under locked hash |
| R02-10 | Injected account save failure restores hash/version/role and unrevoked refresh authority |
| R02-11 | Existing lockout, concurrent failed logins, unlock, display/last-Admin/assignment tests unchanged |
| R02-12 | Old JWT/NULL-family refresh, no promotion, old reports/me, Web extra nullable metadata/cookie tests |
| R02-13 | Missing/empty/malformed/duplicate/foreign/unknown/env-mismatched signed family denial; old JWT denied before malformed input binding |
| R02-14 | Anonymous/Admin/PQE/IPQA/forced-change/current-user checks; legacy RBAC suite |
| R02-15 | Both actual configured lines; invalid/inactive/missing/duplicate pair visible503, legacy unaffected |
| R02-16 | No registry required; pending reservation/revoked/replaced synthetic rows do not restrict either IPQA |
| R02-17 | Mixed workflow/line/valid other legacy owner; stable two-page total, overflow/page validation |
| R02-18 | Expected line409, legacy404; Plan/Standard/directory changes leave frozen task identity/definition |
| R02-19 | Missing header/source time/items/IDs, legacy N/A/sample, Completed/unknown state marked unavailable and detail409 |
| R02-20 | Read leaves row/audit counts unchanged, DTO canExecute=false; owner/non-owner old shared writes retain409/403 |
| R02-21 | Receipt server UTC, 1-day and 14-day absolute lifetimes, successful reverify and no family extension |
| R02-22 | Receipt JSON as bearer denied, no token fields in receipt; no receipt verification/login fallback exists |
| R02-23 | Returned task/user/environment marker assertions; local encryption/reboot/coordinator behavior specified, Android NOT RUN |
| R02-24 | Per-request authoritative join, paused rotation reader, post-logout/reset denial; bounded transient errors classified503 |
| R02-25 | Existing task/review/scheduler/Completed/history/HTML-XLSX and workflow-identity tests run unchanged |
| R02-26 | New fresh and populated P103A tests; existing historical original-column/report comparisons and restrictive FKs |
| R02-27 | Both provider model checks and native PostgreSQL additive SQL assertions |
| R02-28 | Opt-in live PostgreSQL CI: fresh/populated upgrades, actual locks/races/rollback/uniqueness; local no-server cases SKIPPED separately |
| R02-29 | Full device retirement/line/audit/closure/failed pending-cleanup regression suite unchanged |
| R02-30 | Web 70 tests, lint/typecheck/build; no new business UI, unchanged permissions/routes and EN/ZH |
| R02-31 | Forward-only schema rollback/restore controls documented; real IT restore rehearsal SKIPPED / PENDING operations |
| R02-32 | Release/full/provider/Web/whitespace/secrets and final-head GitHub CI; review PR only, no merge/deploy |

## Verification commands and live PostgreSQL isolation

From repository root: dotnet restore/build/test the solution in Release; Web npm ci/lint/test/typecheck/build; dotnet tool restore; EF has-pending-model-changes under scoped Testing SQLite in-memory and Postgres model-only configuration; generate/review Postgres idempotent migration SQL; git diff --check and scoped secret scan. Test evidence/TRX and generated SQL remain under the OS temp ipqc-r32-02-verification directory, not Git. Pending-model checks and SQL generation do not connect to a server.

Focused filter: `FullyQualifiedName~PersonalSession|FullyQualifiedName~OfflineAccessReceipt|FullyQualifiedName~LinePatrolRead`. Migration filter: `FullyQualifiedName~MigrationTests`. Existing backend suite continues to include all legacy and R32-01 tests.

Live tests require IPQC_TEST_POSTGRES_CONNECTION and IPQC_TEST_POSTGRES_DISPOSABLE=true. The base database must explicitly start with ipqc_test; each fixture creates a random ipqc_test_r3202_ database and only that verified target is removed after disposal. Missing opt-in produces xUnit SKIPPED cases. GitHub CI provides PostgreSQL 17 in an isolated ephemeral service and runs the live class separately; it is real server execution, not a Production-environment SQLite override. No CA/PAD/signer is involved. Failed live tests remain FAIL until corrected; never replace them with SQL-string assertions or unconditional passing returns.

## Remaining external gates

Ordinary internal server FQDN/port/TLS chain/hostname trust, actual two line IDs/environment ID, both tablets' network reachability, deployment DB read-only inventory and IT restore/backup rehearsal remain PENDING. No hostname/CA/trust status or real-device success was invented. Actual SM-X210/API35 and SM-T500/API30 storage/offline/reboot/upgrade/UAT remain SKIPPED pending their own app increment. minSdk30 and separate personal/shared protection remain the approved target. Existing npm advisories (13: seven moderate/six high) and the existing large Web bundle advisory are unchanged; no upgrade is bundled. WorkContext/F1, R32-03 generation, shared drafts/states/Submit/F2/photos/F3/PQE shared reports and Android remain deferred.
