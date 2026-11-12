# R32-04 WorkContext foundation

## Post-merge status and proposed follow-up, 2026-11-12

Foundation status: **MERGED_TO_MAIN**. [PR #15](https://github.com/peterl-ai/IPQC-system/pull/15)
merged as `6fa84de00d5e719ab4474a77daa89d55eea4f086`, from feature HEAD
`846f984b67d74d87595945eaca4e86ad01285ba9`. Merge CI
[43262427172](https://github.com/peterl-ai/IPQC-system/actions/runs/43262427172)
passed: backend374 PASS/0 FAIL/33 opt-in SKIP, separate real PostgreSQL17
33 PASS/0 FAIL/0 SKIP, Web90 PASS/18 files plus lint/typecheck/build.
These verify the foundation, not deferred operation acceptance or execution.

The [proposed shared inspection identity/acceptance contract](architecture/IPQC_SHARED_INSPECTION_IDENTITY_ACCEPTANCE_CONTRACT_V1_2026-11-12.md)
distinguishes approved requirements, implemented facts, technical proposals,
OPEN DECISION D01 and DEFERRED / NOT RUN cases. Real attempt identity is a
prerequisite to operation acceptance; no fake attempt/sample or accepted receipt
is introduced. DraftVersion/ledger/receipts, business manifests/quarantine,
inspection attempts, F2/F3 and Android remain unimplemented. WorkContext preflight
still accepts no work, and all shared tasks remain `canExecute=false`.

Peter's 2026-11-12 standing authorization permits later clearly scoped work under
already-approved requirements without repeated routine implementation approval.
Material unresolved decisions still require Peter; proposal publication does not
decide D01. Merge, production operations and destructive cleanup remain excluded.
This task authorizes only the two-document PR and stops for independent review.

The self-review/authorization language below records the pre-merge foundation
delivery. Its “unmerged” migration wording is historical: that definition is now
merged and must not be rewritten. Persistent early-draft databases still require
owner-reviewed reconciliation. Original tests, findings and deferred evidence
remain recorded below without being relabeled as new-contract results.

## Mandatory developer self-review, 2026-11-12

The complete main-to-HEAD application, test, CI, migration and generated-model diff
was compared with approved Rev3.2 and the readiness report. The prior all-PASS
summary was not used as proof. Confirmed findings on original HEAD
`c8aa12d666c7c9499bb454e6142b82af5f1e4046`:

| Priority | Original evidence | Fix and regression |
| --- | --- | --- |
| P2 | Services/WorkContextStore.cs:73 validated claims/family but never required an authenticated identity | RunAsync rejects unauthenticated principals before SQL; six direct entrypoints reproduced the bypass and now deny401. HTTP was already protected. |
| P2 | Domain/WorkContext.cs:36 and Services/WorkContextSecurity.cs:34 stored only before/after revision numbers | Persist proof-free typed metadata snapshots in BeforeJson/AfterJson, captured before mutation and in the same audit transaction. Audit HTTP regression reproduced missing fields, then passed. |
| P2 | Tests/WorkContextSecurityTests.cs:18,40 ignored logout/password/reactivation responses; WorkContextConcurrencyTests.cs:104 ignored deactivation result; PG upgrade checked selected fields only | Require successful operations and actual account/session/line state; assert sticky context state independently; fingerprint every old PG table/column/constraint/index, not selected DTOs. |

The six unauthenticated-entrypoint regressions plus missing-snapshot regression all
failed before fixes (7 FAIL), then all passed (7 PASS). Additional tests reject use of
proof/context IDs as bearer authentication and exercise issue-versus-role-loss and
deactivation in both orders on SQLite and real PostgreSQL CI. No P0/P1 finding was
confirmed. This is developer self-review, not independent Final Review or merge approval.

The single **unmerged** R3204 migration definition now includes the missing audit
snapshots; no migration through R3203 was changed. A database that applied an earlier
draft of R3204 is not automatically upgraded by the revised definition: disposable
fixtures use fresh databases; any persistent draft installation requires owner-reviewed
reconciliation. No production migration or destructive cleanup is performed.

Audit snapshots serialize only WorkContextMetadata, never entities with proof hashes.
The direct-service guard is confined to WorkContextStore; existing Admin account APIs
and normal logout/self-password behavior retain their prior authentication contracts.

Local self-review validation: Release restore/build PASS (0 warnings/errors), full
backend374 PASS/0 FAIL/33 opt-in PostgreSQL SKIP;62 WorkContext cases PASS in that
full run. Web90 PASS/18 files, lint/typecheck/build PASS. Both EF provider models
clean and native PostgreSQL SQL contains only two new tables plus their checks/FKs/
indexes, including typed text audit snapshots. Real PostgreSQL results must be read
from the final exact-HEAD CI/PR delivery, not inferred from these local SKIPs.

First backend foundation PR, separately authorized by Peter, based on main
`8462014d7d408a1a90ff7fcae50cecac459a7e61`. No merge, production migration,
deployment or shared inspection activation is authorized.

## Implemented boundary

Eligible online IPQA obtains an owner-only task-scoped WorkContext and random
32-byte Base64url proof. Only SHA-256 hash is persisted. Issuance commits context
and audit before returning the proof; lost responses require explicit new issuance.
Immutable facts include environment, owner, origin personal family, Task/actual
Line/workflow and capture deadline. CaptureUntil equals the fresh server receipt
deadline, capped at72h and original family expiry. Context renewal never changes old facts.

Contributor A, current transporter B and future formal submitter C remain distinct.
Proof possession is scoped provenance, not current authentication, physical offline
authorship or proof of capture time. B requires independent current authority.
Preflight is read-only and all-or-nothing; it accepts no work, draft, receipt or evidence
and cannot authorize a later acceptance transaction. Shared tasks remain canExecute=false.

| Route | Authority and outcome |
| --- | --- |
| POST `/api/line-patrol/tasks/{taskId}/work-contexts` | Current eligible IPQA owner; context/proof/receipt,201 |
| GET `/api/line-patrol/tasks/{taskId}/work-contexts/{contextId}` | Current IPQA task-read; nonsecret scoped metadata |
| POST `/api/line-patrol/tasks/{taskId}/work-contexts/validate-sources` | Current B plus scoped saved A proofs; SourcePreflightOnly/acceptsWork=false |
| GET `/api/admin/work-contexts/{contextId}` | Admin UsersRead plus current family |
| GET `/api/admin/work-contexts/{contextId}/audit` | Same, stable time/Id paging with totals |
| POST `/api/admin/work-contexts/{contextId}/security-revoke` | Admin UsersWrite plus current family; expectedRevision/reason |

Issuance/preflight require configured active line, coherent admitted PlanningDefinitionV1
and clean foundation Task state. No historical Task conversion or parent reconstruction.
Legacy reads and ownership remain unchanged. Strict DTOs reject unknown fields.
Authentication/role/current-family run before body binding and new rate limits.
All namespaces emit no-store on success/auth/error. Issuance10/min/user,
preflight30/min/user,queue0; maximum100 distinct contexts and32KiB body, including
unknown-length bodies. These limits are process-local, not a distributed abuse service.

Bad input400; missing/legacy/out-of-config Task404; expected scope/state/snapshot
conflict409. Bad/unknown/owner-mismatched proof is409 SourceProvenanceConflict;
valid scoped but security-revoked/ineligible source is409 SourceSecurityRevoked,
with source context IDs for quarantine. Current auth retains401/403/503 semantics.
Metadata, history and logs never contain proof/hash. Audit records server facts only.

## Revocation and transactions

Ordinary logout/expiry/refresh/self-service password change do not security-revoke
historical contexts. Admin reset, deactivation, IPQA role loss and explicit context
revoke are sticky and atomic with audit. Reactivation never restores contexts.
Existing `/api/users` JSON/auth remains compatible, including old Admin JWTs without
personal family; only new context endpoints require it. Reset receives stable actor ID
internally. Original assignment/final-Admin checks precede mutations.

SQLite reserves a non-deferred writer before authority reads, with bounded timeout.
PostgreSQL discovers immutable source owners, locks requester/owners in sorted GUID
order FOR NO KEY UPDATE, revalidates current authority, locks sorted configured lines
FOR SHARE, Task FOR UPDATE, then sorted contexts. No planning gate is acquired.
Admin explicit revoke does not require active line/configuration/editable Task.
Account hooks participate in existing transactions without nested transactions.
Only known rolled-back contention retries, at most3 attempts; lock waits capped5s.
Unknown commit/network failures are not blindly retried. Audit/precommit failures roll
back the complete graph; secrets/success are never delivered before commit.

## Migration and recovery

`20261111214948_R3204WorkContextFoundation` adds only WorkContexts and
WorkContextAuditEvents, indexes/checks and restrictive FKs. Ten old migrations stay
byte-unchanged. No old table rebuild, contributor backfill or Task mutation belongs to
this migration. Origin family is historical scalar, not FK to rotating refresh rows.
Tracked context identity is immutable and audit append-only; privileged raw DB edits
are outside application guards. Down refuses destructive removal.

Retain schema on application rollback and close context/provenance ingress before
running a binary without source-security hooks. Restore closes ingress/workers first,
reconciles account/session/context security, then reopens. If historical context security
cannot be established, retain bytes/history and quarantine unaccepted dependencies;
use reviewed audited forward repair, never delete provenance to hide uncertainty.
Actual accepted-operation/F2/F3 restore integration remains deferred.

## Acceptance evidence map

Test paths below are relative to `api/JaxPower.Ipqc.Api.Tests/`. Scenario IDs are not
test counts. Local results, unavailable live gates and exact-HEAD CI are reported in PR
delivery. Real PostgreSQL remains SKIP locally without explicit disposable opt-in.

| Case | Executable evidence / review |
| --- | --- |
| R04-01 | WorkContextTests.Foundation; both_actual_lines |
| R04-02 | HTTP/direct current authority; Forged_authority_claims_fail |
| R04-03 | Provenance cross-scope negatives |
| R04-04 | Historical_task_is_not_implicitly_admitted; existing clean-state regressions |
| R04-05 | Proof length/hash-only; malformed proofs; metadata DTO redaction |
| R04-06 | Receipt_caps_at_original_family_and_both_actual_lines |
| R04-07 | Foundation multiple immutable contexts; explicit new-issuance recovery contract |
| R04-08 | Owner derives from authority; unknown UserId body rejected |
| R04-09 | Normal_session_changes_preserve_historical_proof |
| R04-10 | Security reset/deactivation/role/explicit revoke cases |
| R04-11 | Reactivation/role restoration keeps old contexts revoked |
| R04-12 | Current B logout and forged authority negatives |
| R04-13 | Malformed/unknown proof and forged owner; tracked immutable guard |
| R04-14 | Task/line/environment mismatches; no device/workspace authority fields |
| R04-15 | Mixed source rejection, no business/audit side effects |
| R04-16 | Unknown/null/duplicate/oversized input and body tests |
| R04-17 | Rate exhaustion, auth order, no-store, body cap |
| R04-18 | Stale409/current no-op; original audit/cause preserved |
| R04-19 | Metadata/auth tests, append-only guards and restrictive FK/model checks |
| R04-20 | Task remains nonexecuting, no submissions; shared predicate unchanged |
| R04-21 | Historical proof valid after ordinary15-day expiry |
| R04-22 | Issue/security writer both orders |
| R04-23 | Preflight/revoke both orders; subsequent preflight denies |
| R04-24 | Refresh/logout/current uploader races |
| R04-25 | Actual line deactivation/issue both orders; inactive-line Admin revoke |
| R04-26 | Audit fault rolls back account/password/session/context, three actions |
| R04-27 | Issuance audit/precommit failures leave no graph |
| R04-28 | Bounded known-conflict retry/exhaustion; provider lock review |
| R04-29 | Full legacy/R32-01/02/03/backend/Web regressions |
| R04-30 | SQLite old-column fingerprints and foreign_key_check; historical suites |
| R04-31 | Both models/native additive SQL/protective Down; live PG populated upgrade |
| R04-32 | Full Release/Web/SQLite/live PG/Git/CI gates; see exact results in delivery |

WorkContextPostgresTests reuses provider-specific foundation, issue/reset,
preflight/revoke, refresh/logout, line activity and rollback scenarios, plus populated
upgrade. CI adds this class and retains both existing live PostgreSQL classes.
Model/SQL checks are not live-provider evidence. Existing historical SQL assertions are
bounded to their original migration end versions so later CREATE TABLE additions do
not falsely invalidate the older additive-column contract.

## Deferred / NOT RUN

| ID | Dependency |
| --- | --- |
| R04-D01 | Accepted A facts carried into B's own Submit; acceptance ledger/F2 |
| R04-D02 | Operation replay/hash/receipts and security/acceptance races |
| R04-D03 | Shared DraftVersion CAS and two-tablet offline conflicts |
| R04-D04 | Android independent personal/work keys and logout durability |
| R04-D05 | F2 frozen/query-only takeover, late acceptance |
| R04-D06 | F3 photos/Ready/references/retention/restore |
| R04-D07 | Real inspection attempts/dependency graph |
| R04-D08 | IT, both tablets, signed upgrade/UAT/restore drill |

External gates remain actual line/environment IDs, factory HTTPS/DNS/TLS/proxy,
production shared-row/DB inventory, file storage/backup ownership, restore rehearsal,
tablet patch assessment and QA/PQE UAT. No shared business write, new role, Android,
device enrollment/mTLS, production migration or deployment is included.
