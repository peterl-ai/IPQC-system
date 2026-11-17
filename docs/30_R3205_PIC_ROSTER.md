# R32-05 Independent PIC roster foundation

2026-11-17. Peter authorized one backend-only PR and selected A: one global roster
per IPQC installation, shared by both lines, with no PIC line ownership. Admin maintains
independent PIC identities; PICs are not login accounts. Optional per-NG multiselect and
formal frozen submission snapshots remain future separately scoped work.

## Exact-file audit before schema

PR29 reviewed HEAD `1ee062a9097a3e2037bb3a5b06cab471e238c9ce` merged as current main
`9be6ecf5024dc8417d62e6a633a32b302cf0c67b`, identical trees. Merged-main
[CI44118240489](https://github.com/peterl-ai/IPQC-system/actions/runs/44118240489)
succeeded before this branch/schema: backend1034 PASS/623 opt-in PG SKIP/0 FAIL;
real PG17.12 623 PASS/0 SKIP/FAIL; Web127 PASS/20 files, lint/typecheck/build;
Release0 warnings/errors. No open PR existed. Original checkout and all22 unrelated
untracked paths/SHA-256 hashes match baseline. Previous migration:
`20261116221536_R3205SharedTaskBasicInformationDraft`.

Authority: [Rev3.2 §10](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md),
[Supersession](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md),
Peter's explicit PIC authorization/global-roster decision. The
[identity reference](architecture/IPQC_SHARED_INSPECTION_IDENTITY_ACCEPTANCE_CONTRACT_V1_2026-11-12.md)
does not globally approve its technical proposals. Relevant docs18–29 remain binding.

| Actual file / mechanism | Conclusion |
|---|---|
| Domain/PatrolPlan.cs; Contracts/PatrolTaskContracts.cs | Legacy NG has abnormal type/cause/remarks, no PIC identity or roster. Do not infer PIC from text or convert historical rows. |
| Domain/ApplicationUser.cs; Services/ApplicationUserService.cs; Endpoints/UserEndpoints.cs | Login accounts and Admin maintenance exist. A PIC is a separate stable identity; audit actor FK is not a PIC-to-account mapping. Old Admin API family compatibility stays. |
| Services/ProductionLineService.cs; ProvisioningStore.cs | Bounded reference-data reads, explicit lifecycle and revision conflicts are patterns; line-code uniqueness does not justify PIC-name uniqueness or line ownership. |
| Security/PersonalAccess.cs; Services/WorkContextStore.cs | Current database role/family validation and account-first transaction protocol can be reused without changing source-security hooks or PR17's publication fence. No WorkContext proof is needed for online Admin reference maintenance. |
| Domain/SharedDraft.cs, TaskHeader.cs, PatrolPlan.cs; Services/SharedDraft* | Accepted samples/dispositions/header/photos and legacy formal submissions stay separate; roster edits must not advance Task DraftVersion or alter accepted facts. |
| Data/Migrations; provider fixtures/fingerprints | Add independent empty tables, preserve applied migrations and old rows/columns/index/FKs. Upgrade tests must explicitly start at the previous migration; model-only PG is not real execution. |

## Scoped technical contract

Two append-only tables: PicRosterEntries holds server-issued PicId/creation time;
PicRosterRevisions holds each complete name/department/active snapshot and actual
Admin actor/family/environment/time/action. Current state is the greatest revision,
not a second mutable value copy. No hard delete or name uniqueness. Display name and
department are required, trimmed bounded text (200 Unicode scalar values), no controls
or malformed Unicode; no Unicode normalization or department enumeration.

Admin create/edit/activate/deactivate uses a client RequestId, exact expected revision
for existing entries, and immutable typed original receipts. Exact accepted intent
replays its original receipt; changed known RequestId conflicts. Duplicate names with
different server PicIds are legal. A repeated create with the same RequestId cannot
create another PIC. No-op lifecycle/edit requests with a new ID conflict rather than
accidentally reactivate or create phantom changes. GET never creates accepted facts.

SQLite reserves the writer before authoritative reads. PG reuses account-first current
authority, then locks the PIC identity before comparing/appending its next revision;
native sequential-revision checks/uniqueness protect append-only history. Identity,
revision/audit and original receipt facts commit together. Unknown responses recover
by RequestId; no blind new-ID retry. Normal account changes never rewrite history.

New Admin routes require current Admin database role and personal family. Active lookup
uses current IPQA family and an actual authorized shared Task/line; the global PIC has
no LineId. PQE/IPQA get no roster mutation permission. Existing users/lines/Standards
APIs keep their authorization. Future selection must separately revalidate eligibility
and freeze selected exact historical values; this lookup is only a point-in-time read.

## Exclusions and validation boundary

No per-NG selection, mandatory PIC, formal Submit/PQE/reinspection, submission snapshots,
notifications, Android/Web execution UI, F2/F3, photo coverage proof or production action.
Shared canExecute=false/canSubmit=false remain. No production migration or destructive Down.
Exact implementation API, executed tests, final-HEAD CI and adversarial review evidence
will be recorded in this PR; the audit above does not claim those gates already ran.

## Implemented API and revision contract

Admin prefix `/api/admin/pic-roster`:

| Method / suffix | Contract |
|---|---|
| GET `/` | All entries by default; optional active filter, page>=1, pageSize1..100/default20. Ascending immutable PicId, HasMore; no mutable-name sorting. |
| GET `/{picId}` | Latest complete revision values including inactive. |
| POST `/` | Required RequestId, Name, Department, IsActive. Server issues PicId;201 for new creation,200 for exact replay. |
| PUT `/{picId}` | RequestId, required ExpectedRevision, Name, Department. Active state is retained. |
| POST `/{picId}/activate` or `/deactivate` | RequestId and required ExpectedRevision; changes only active state. |
| GET `/{picId}/history` | Bounded page/pageSize, descending exact Revision; original snapshots and actor facts. |
| GET `/requests/{requestId}/receipt` | Original typed revision receipt; point-in-time404 is not cancellation or proof no delayed acceptance can occur. |

Current-IPQA GET `/api/line-patrol/tasks/{taskId}/pic-roster` uses the same bounded
pagination, optional expectedTaskLineId and existing actual shared Task/configured active
line checks. It returns only active current entries; task read does not select or freeze
PIC. Admin/PQE have no IPQA task-read bypass. New narrow PicRoster.Read/Write permissions
are Admin-only, with current database role/family checked again inside the transaction.
Forced password change denies business access. Existing Admin APIs remain unchanged.

All new paths emit no-store on success/auth/error. Auth/current-family precede binding;
mutations reuse the strict duplicate/unknown-property and numeric-coercion rejection,
128KiB/depth32 and shared process-local30/min/user bounds. The existing middleware's
InvalidDraftJson code also covers malformed PIC JSON; this does not make reference
maintenance an inspection draft operation. Invalid input400, authority401/403, missing
resource404, stale/known-ID/no-op409, limits413/429, existing configuration/contention503.

Text is validated before trimming (raw ingress <=400 UTF-16 units), then stored as
1..200 Unicode scalar values; no controls, ill-formed Unicode, normalization or name
uniqueness. Different RequestIds may intentionally create distinct same-name PICs.
The V1 fingerprint covers environment, RequestId, action, target PicId (null on create),
exact expected revision and normalized editable fields; never credentials/current actor.
Revision receipts retain the original actor/family/time, not the last retry transporter.
No mutable collection is accepted. Exact existing-entry retries recheck RequestId after
the PG identity lock, so a request committed while waiting recovers its original receipt.
Concurrent creates with the same RequestId may safely409; transaction rollback removes
the losing uncommitted identity, and a later exact retry recovers the winner's PicId.

Each page is a single-statement current-state observation. Stable ordering is not a
multi-request frozen roster snapshot; refresh/revalidate before a future selection.
No count is assembled from a different statement/version. Roster revisions are separate
from Task DraftVersion; no existing accepted work or formal state is changed.

Migration `20261117161849_R3205IndependentPicRoster` adds only the two empty tables.
PG uses native uuid/bigint/boolean/timestamptz/varchar, SQLite its existing types.
Restrictive identity/audit-actor FKs, unique RequestId, composite PicId/Revision PK,
bounded closed fields and native sequential/action/actor guards protect history.
Native and tracked UPDATE/DELETE are denied for both tables; Down throws. Audit actor
FK to ApplicationUsers records maintenance, not a login relationship for the PIC.

## Executable acceptance map and proof limits

| Attachment case | Executable evidence |
|---|---|
| 01 stable server identity | PicRosterTests.Lifecycle; Lost |
| 02 name/department edit | Lifecycle; Replay |
| 03 activation/deactivation | Lifecycle; Cas(deactivate) |
| 04 inactive omitted from lookup | Lifecycle; Paging |
| 05 truthful history/audit | Lifecycle; Integrity; Logout |
| 06 IPQA cannot mutate | Authority, all four mutation routes |
| 07 PQE cannot mutate | Authority, all four mutation routes |
| 08 current family/database role | Authority HTTP/direct; Logout both orders |
| 09 missing/invalid/oversized fields | Invalid8 cases; Shape; direct malformed Unicode |
| 10 duplicate/stale conflicts | Replay; Cas; CreateRace |
| 11 concurrent Admin edits | Cas6 cases, distinct real Admins/connections and PG observed lock waits |
| 12 retries/recovery | Replay; Lost after actual commit; CreateRace2 cases |
| 13 pagination/order | Paging; Lifecycle history |
| 14 not login identity | Lifecycle account count/IDs; model FK inspection; Shape rejects account mapping |
| 15 no legacy conversion | Populated Upgrade; Lifecycle old-business fingerprint |
| 16 fresh/populated providers | Real fixtures; Upgrade explicitly starts at Previous; model/DDL checks are separate |
| 17 rollback/native guards | AcceptanceRollback5; MigrationRollback3; Integrity single-parameter native probes |
| 18 prior security/draft/header/preflight | Complete backend/PG regressions; Lifecycle; Upgrade retains accepted header/photo/sample/binding facts |
| 19 no formal state/notification | Lifecycle/Upgrade old-business fingerprints; actual scope diff |
| 20 disabled execution/Submit | Lookup/Lifecycle current preflight assertions; no preflight implementation change |

All integration cases have explicit real-PG twins; two provider-model/DDL cases do not
open a PG connection and are never called real provider execution. PG CAS/logout races
require pg_blocking_pids observations before releasing the holder. The separate create
race deliberately lets another connection commit before releasing an unexecuted first
revision INSERT, proving unique-request rollback leaves no orphan PIC identity.

Development test-oracle corrections: exact concurrent request recovery is200, not a
second accepted change or an obligatory409; authentication's new login session is
captured before comparing PIC maintenance's business side effects. The original
failed local run was32 PASS/3 FAIL, not final evidence. Final updated-HEAD counts,
complete diff self-review and CI links belong in the PR delivery. No independent
review, production inventory/migration or deployment is claimed by these tests.

## Mandatory complete-diff developer self-review

Reviewed the actual staged production/endpoints/auth/CAS/history/retry, native migration,
tests/CI/docs and generated-model diff. Snapshot only adds101 lines for the PIC entities;
the new designer's entire target body equals it. No applied migration, old authorization,
account-security hook, source revocation or accepted SharedDraft/Header/Preflight code
changes. Old-schema fingerprints exclude only the two new tables and are complemented
by independent PIC integrity and populated-upgrade checks.

P3 test instrumentation finding: PicRosterConcurrencyTests.Race originally observed only
FOR NO KEY UPDATE for an account waiter, whereas PersonalSessionStore uses FOR UPDATE.
The account-scoped barrier now recognizes both lock forms and still requires an actual
PG lock-manager path, distinct from the PIC-row-only CAS barrier. The two real-PG logout
orders exercise this correction; their execution must be read from final CI.
No confirmed production defect was found in this review. The post-PIC-lock RequestId
recheck is an explicit idempotency improvement, preserving original maintenance actor.

Local union:137 PASS/0 FAIL/0 SKIP, covering37 new cases (35 connected SQLite,2 provider
model/DDL) plus100 historical migration cases. The sole xUnit assertion-style warning
was corrected; affected concurrent-create2 and strict-shape/logout3 checks then passed
with a clean Release compilation. These are overlapping validations, not extra unique
case counts.35 PG cases are appended to CI without dropping any existing class; none
is counted executed until actual disposable PG logs confirm it. Final-HEAD Push/PR
CI remains a separate mandatory gate; independent ChatGPT Final Review remains required.
