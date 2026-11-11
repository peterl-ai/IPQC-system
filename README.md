# JAX Power In-Process Quality Control (IPQC) System

Internal JAX Power IPQC platform that replaces the **IPQC / Line Patrol portion** of Jinko GQMS for new JAX Power operations.

## Rev3.2 / R32-01 current direction

[Approved Rev3.2](docs/architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md) and its [Handoff Supersession](docs/architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md) supersede the older device-PKI direction. Future Android uses personal login → server-backed line selection → tasks on both SM-X210/API35 and SM-T500/API30 (one minSdk30 app). P1-03B is cancelled; R32-01 implements no Android, personal-session extension or LineShared execution.

Device provisioning/enrollment is retired at both routes and services. Retired Admin writes retain 401/403 authorization, then return `410 DeviceProvisioningRetired`; the four Bootstrap operations return 404. Admin line management, historical device/enrollment/audit reads, controlled revoke/cancel and all legacy business/auth/report APIs remain available. Existing reservations still block line deactivation until controlled revoke. Historical rows, migrations and typed audit remain intact.

`Enrollment:CleanupEnabled` defaults false; old enabling flags cannot reopen enrollment or signing. Explicit cleanup only closes/reconciles original operations, outside DB transactions for Recover/Revoke, and clears pending only after signer acknowledgement plus atomic audit commit. No production signer is configured. Keep the original environment, issuer keys and receipt journal until old obligations are resolved. See [current cleanup and verification evidence](docs/15_R3201_VERIFICATION.md). The P1-02/P1-03A descriptions below record historical implementation, not commands to re-enable it.

## R32-02 personal session and read foundation

The R32-02 implementation adds stable personal refresh families, atomic account/session lifecycle, a 72-hour local-policy receipt contract and personally authorized **non-executing** line/task reads. See [exact contracts and verification](docs/16_R3202_PERSONAL_ACCESS.md). Configure `PersonalAccess:EnvironmentId` and the two real `LinePatrol:LineIds` separately; missing new settings leave legacy login/Web/APIs usable and return visible configuration errors only on the new personal surface. No line IDs or production endpoints are seeded. Existing legacy JWT/cookie/report/ownership behavior and R32-01 retirement remain; new routes require current database family authority and do not use devices. Android, WorkContext and shared writes/Submit remain deferred; shared planning generation is provided separately by R32-03 below. This feature is for PR review, not production activation.

## R32-03 shared planning foundation

Admin/PQE shared Plans now use actual configured lines, fixed New York Day07/Night19 rounds, revision/overlap controls and server-side ownerless generation. Versioned planning snapshots freeze source facts; all shared task reads remain `canExecute=false`. Legacy personal Plans and R32-01/02 contracts remain. See [implemented contracts, migration safety and verification](docs/17_R3203_SHARED_PLANNING.md). Scheduling is not inspection activation; WorkContext, shared writes/Submit/photos and Android remain deferred. No production rollout is performed.

## Product direction

The product has two major clients:

1. **Web application** — Phase 1. Used by Admin, PQE, and IPQA for Line Patrol configuration, scheduling, approval, history, reports, and administration.
2. **Android tablet application** — later phase. English-only client used by IPQA inspectors to receive, execute, correct, and resubmit patrol tasks.

### Explicitly out of scope for current development

- **Material Verification** business workflow is not being developed by this project team now. It will later require MES work-order/BOM integration and is expected to be continued by the MES vendor.
- Reserve a future **Material Verification icon/entry on the Android app home screen**, but do not invent or implement its workflow.
- No legacy GQMS data migration. Historical records remain available in Jinko GQMS.
- Do not use the MES production database as the IPQC application database.

## Phase 1 web modules

### Line Patrol
- Patrol Standards Management
- Patrol Plans
- Patrol Tasks / PQE Approval
- Completed Patrol Records
- Report Preview / Excel Download

### System Management
- User Management
- Role-based access control

## Languages
- Web: English + Simplified Chinese toggle
- Android: English only

## Roles

| Role | Web access | Android access |
|---|---|---|
| Admin | All IPQC functions + User Management | Support/testing as needed |
| PQE | All Line Patrol business functions; no User Management | Not required for normal workflow |
| IPQA | Completed Patrol Records only | Receives, executes, corrects, and resubmits patrol tasks |

## Confirmed workflow decisions

### Patrol Standard
- Create / Save / Copy
- Export / Import / Download Template as first-level buttons
- No Submit / Approve / Upgrade workflow
- Required standard header fields: **Line Name** and **Patrol Standard Name** only
- Remove legacy **Base** and **Business Unit** fields

### Patrol Task approval

```text
Generated / Pending Inspection
        ↓
IPQA executes on Android tablet
        ↓
Submitted / Pending PQE Approval
        ↓
PQE Approve ───────────────→ Completed
        │
        └─ Reject (Reason required)
                ↓
           Return to IPQA
                ↓
        IPQA corrects / re-inspects
                ↓
              Resubmit
                ↓
        Pending PQE Approval
```

Reject/resubmit history must be retained for auditability.

### Completed Patrol Records
- Historical records page only; **no dashboard is required**.
- Details page is required.
- Report preview should be HTML or a web modal/popup.
- Report download remains Excel. Prefer `.xlsx` for the new system unless legacy `.xls` compatibility becomes a hard requirement.

## Android target device

Current plant tablets (reported inventory):
- Line 1: Samsung Galaxy Tab A9+ / SM-X210, Android 15 / API35.
- Line 2: Samsung Galaxy Tab A7 / SM-T500, Android 11 / API30.

The future Android app must run correctly on Android 11. See `docs/08_ANDROID_DEVICE_TARGET.md`.

## Recommended architecture

```text
Web (Vue) ───────┐
                 │
Android (later) ─┼── HTTPS REST API ── ASP.NET Core ── PostgreSQL
                 │                         │
                 │                         ├── Scheduler / task generation
                 │                         └── File storage adapter
                 │                                  │
                 └──────────────────────────────────┴── Internal image/file storage
```

Recommended persistence direction:
- Development/tests: SQLite is acceptable.
- Production: dedicated server relational database, preferably PostgreSQL (SQL Server is also acceptable if it is the company standard).
- Do not couple Line Patrol tables to MES DB.
- Store inspection images/files outside the relational DB; store file metadata and storage keys in the database.

See `docs/04_ARCHITECTURE.md`, `docs/05_DATA_MODEL_DRAFT.md`, and `docs/09_FILE_STORAGE.md`.

## Repository layout

```text
.
├─ README.md
├─ AGENTS.md
├─ CLAUDE.md
├─ .gitignore
├─ docs/
│  ├─ 01_PROJECT_SCOPE.md
│  ├─ 02_UI_FIELD_SPEC.md
│  ├─ 03_RBAC.md
│  ├─ 04_ARCHITECTURE.md
│  ├─ 05_DATA_MODEL_DRAFT.md
│  ├─ 06_PHASE_PLAN.md
│  ├─ 07_DECISIONS_OPEN_QUESTIONS.md
│  ├─ 08_ANDROID_DEVICE_TARGET.md
│  └─ 09_FILE_STORAGE.md
├─ prompts/
│  └─ CODEX_PHASE_1A_BOOTSTRAP.md
├─ web/
├─ api/
├─ android/
└─ reference/
   └─ README.md
```

## Delivery strategy

Do not ask an agent to build the entire product in one prompt. Keep each PR phase-scoped with acceptance criteria and tests.

Phase 1C made **Patrol Standards Management** persistent. Phase 1D added Patrol Plans and server scheduling. Phase 1E added task snapshots, assigned-IPQA execution APIs, immutable submissions, and PQE task review. Phase 1F added Completed Patrol Records and server-generated reports. Phase 1G adds local accounts, persistent User Management, and server-side RBAC. Android UI remains a later-phase module.

## P1-01 workflow and line foundation

`P101WorkflowLineFoundation` adds `ProductionLines` and frozen `WorkflowVersion` / nullable `LineId` on Plans and Tasks. Existing rows default to `LegacyPersonalV1 = 1` with no inferred LineId. `LineSharedV1 = 2` requires a non-null LineId referencing an existing line; unknown versions fail database checks and legacy service policy checks. No production lines are seeded, and no Line management API or LineShared execution is enabled.

ProductionLine codes are unique across this IPQC installation (confirmed by Peter on 2026-11-06), including inactive lines. The domain trims surrounding whitespace and derives an invariant-uppercase `NormalizedCode`; internal characters are preserved. The unique index uses this normalized code. Legacy `FactoryCode` / `LineCode` / `LineName` fields remain independent text snapshots: there is no factory-scoped uniqueness inference or automatic historical mapping. Future multi-factory requirements need an explicit compatibility change.

Legacy Plan mutations reject unsupported workflow before legacy input/assignee validation, after authorization and target lookup. Conditional database writes enforce the legacy version for Plan mutations, Task draft/submit, review/batch, and generation. Plan mutations and generation lock/recheck the Plan within their transaction; a generated Task keeps its own workflow/LineId even if the Plan is subsequently changed. Existing endpoints cannot select or convert workflow/LineId.

Task Draft/Submit checks authenticated ownership before classifying workflow: non-owners receive 403, while an owner using the legacy API on a LineShared task receives 409. Existing Plan, Task (including `/my`), and Completed Record read DTOs are legacy-only. Their lists filter by `LegacyPersonalV1` before counting/pagination; authorized direct reads of unsupported records return 404, including detail/report/XLSX. Ownership and role authorization still run first. This is temporary API compatibility isolation, not a change to the approved future PQE requirement to read submitted LineShared NG revisions/photos immediately. Version-aware task/report projections must exist before LineShared activation.

Known scheduler hardening follow-up: insertion rechecks and locks `WorkflowVersion`, `LineId`, and `IsEnabled`, but does not compare every cached scheduling/configuration field. An edit to schedule, effective period, generation boundary, Standard, or assignee after polling but before the insertion claim can still produce a legacy task from the earlier configuration. The occurrence unique key prevents duplicates, not this stale-configuration race; a future configuration-version/reload protocol should address it. Future line-management/import writes must continue using the canonical ProductionLine code normalization and installation-wide unique namespace.

Apply migrations only after backing up the intended database and reviewing provider SQL. PostgreSQL adds columns, checks, indexes, and restrictive line FKs. SQLite uses EF-managed Plan/Task table rebuilds; populated Phase 1D/1F/1G upgrade tests compare all original columns and preserve available history/report facts. Preserve nullable pre-1G owners and pre-1E missing snapshots. Do not infer historical identities from labels.

Operational rollback retains the expanded schema and rolls back application code while LineShared remains inactive. Do not use destructive `Down` migration as a recovery procedure: it removes the new identity fields/table and can reject populated line dependencies on SQLite. Restore a verified backup or use a reviewed forward repair when schema recovery is required. Live PostgreSQL migration/concurrency validation remains a separate gate; SQLite and generated SQL do not establish it.

Android, device authentication, shared drafts, new Submit/state-machine behavior, photos, and retention remain deferred. Approved architecture Rev3.1 and its F2 handoff addendum are unchanged.

## P1-03A candidate enrollment — historical implementation

Admin-only enrollment invitations, P-256 CSR/key proof, explicit physical verification/approval, non-authorizing candidate issuance/recovery and typed enrollment audit are implemented. Devices remain `PendingCredential` / `NotEnrolled`; enrollment never advances their binding version or enables patrol. Bootstrap defaults off and cannot be enabled in production before separately reviewed P1-03B transport. Only an explicit isolated development/test issuer exists; production PKI is unconfigured and fails closed. No mTLS/Native authority is implemented.

See [protocol, API, migration and recovery contracts](docs/13_DEVICE_ENROLLMENT.md) and [outstanding isolated SM-T500 verification procedure](android/enrollment-probe/README.md). Android SDK/real PAD, live PostgreSQL and real IT CA execution are separate environment gates, not simulated PASS results. The approved Rev3.1 and F2 handoff remain unchanged.

## P1-02 provisioning metadata — historical implementation

Admin-only `/provisioning` manages the ProductionLine directory and Device Registry through `/api/admin/production-lines` and `/api/admin/devices`. PQE/IPQA have no provisioning permissions. English and Simplified Chinese are supported. ProductionLine codes keep the P1-01 installation-wide normalized uniqueness rule, including inactive lines; codes cannot be changed after creation. Lines and devices have no hard-delete API.

Line audit history includes line lifecycle events and device provisioning events whose before or after reservation references that line. Rebind appears in both affected line histories; each event is counted once within a history, including when both snapshots reference the same line. Audit responses include nullable stable DeviceId (null for line lifecycle events); the Web drawer displays DeviceId and ChangeId so duplicate device names and correlated replacement events remain distinguishable. Bind/rebind choices filter occupied lines using the loaded device inventory as a convenience; inventory can become stale, so database uniqueness and server conflict responses remain authoritative.

A Device Registry row is provisioning metadata, **not an authenticated PAD**. Its only statuses are `PendingCredential`, `Revoked`, and `Replaced`; responses explicitly report `credentialStatus: NotEnrolled` and `canExecuteLineShared: false`. `LineId` denotes a pending reservation, never a cryptographically verified effective binding. Bind/rebind reserve an active, unoccupied line; unbind/revoke release it. Deactivating a reserved line fails. Replacement requires a different, unreserved pending device and atomically releases the old reservation, marks the old device Replaced, and reserves the line for the replacement. Required reasons and before/after audit snapshots preserve prior device/line identity and binding versions. Existing Tasks, submissions, personal JWT authentication, legacy reports, and historical ownership are not rewritten or transferred.

Every mutation except creation supplies `expectedRevision`; replacement also supplies `replacementExpectedRevision`. Creation starts `Revision` and `BindingVersion` at 1. Metadata edits increment Revision only; bind/rebind/unbind/revoke/replacement increment both device counters. Line mutations increment the line Revision. A stale revision, occupied/inactive line, terminal device, or invalid transition returns 409 with a provisioning error code. Invalid input returns 400; permission and missing-target responses retain normal 401/403/404 behavior. Clients must refresh after a conflict rather than automatically retrying with a new revision.

Provisioning transactions deliberately serialize low-volume mutations, including reservations of previously empty lines. SQLite acquires its writer reservation **before reading** with a non-deferred transaction; it does not use PostgreSQL row locks. PostgreSQL uses transaction-scoped advisory lock `49505102` with a five-second lock timeout. Revision concurrency tokens and a unique nullable Device.LineId index provide additional protection. Entity changes and append-only audit events commit or roll back together, including replacement's two saves. Audit history retains authenticated actor ID/username, UTC time, reason, correlated change ID, and before/after metadata with restrictive historical line/device/user foreign keys. Application SaveChanges rejects audit modification/deletion; this does not replace database access controls against privileged SQL clients. See [SQLite transaction behavior](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions) and [PostgreSQL advisory locks](https://www.postgresql.org/docs/current/functions-admin.html#FUNCTIONS-ADVISORY-LOCKS).

`P102DeviceRegistryProvisioning` adds ProductionLine.Revision (default 1), RegisteredDevices, and ProvisioningAuditEvents with provider-appropriate types, restrictive foreign keys, positive-counter/status checks, and reservation uniqueness. SQLite uses an EF-managed ProductionLines table rebuild to add its revision check; PostgreSQL adds the column/check without rebuilding historical tables. It creates no devices, mappings, or fabricated audit events for historical rows. Fresh SQLite and populated P1-01/historical upgrade tests preserve original data and reports. PostgreSQL idempotent SQL is generated/reviewed separately; live PostgreSQL migration and concurrency execution remain a required environment-specific gate. Back up the intended database before applying migrations. Operational rollback keeps the expanded schema and rolls back application code; destructive Down is not a data-recovery procedure because it removes provisioning/audit history. Use a verified backup or reviewed forward repair for schema recovery.

P1-03A integrates candidate enrollment invalidation with this same provisioning transaction protocol; see the enrollment section above. Effective device binding, production public pairing, mTLS/trusted-proxy identity handling, NativeSession, OfflineGrant, the Android patrol app, shared drafts, LineShared task activation and new Submit states remain deferred. Future Native authorization must validate the current DeviceId, credential, line and BindingVersion; stale/revoked/replaced identities must fail. Reservation or an unconfirmed candidate alone cannot pass existing legacy workflow guards. Architecture Rev3.1 and the binding F2 handoff addendum remain unchanged.

The shared EF snapshot resolves inferred store types through the active provider rather than retaining SQLite's inferred TEXT/INTEGER types for PostgreSQL. Three existing long submission FK/index names are explicitly preserved to match historical migrations instead of introducing provider-specific naming differences; this changes no historical DDL. When scaffolding future migrations, retain the snapshot's provider-default normalization step and run pending-model checks for **both** SQLite and PostgreSQL, plus provider SQL review. Explicit custom store types require revisiting this normalization. No pending-model warning is suppressed. This is a bounded compatibility measure for the current shared migrations, not evidence of live PostgreSQL execution.

## Phase 1C local development

Prerequisites: .NET 10 SDK and Node.js/npm. From the repository root:

```powershell
dotnet restore api/JaxPower.Ipqc.slnx
dotnet tool restore
cd api/JaxPower.Ipqc.Api
dotnet tool run dotnet-ef database update
dotnet run
```

The development API listens at `http://127.0.0.1:5050`; OpenAPI JSON is at `http://127.0.0.1:5050/openapi/v1.json`. In another terminal:

```powershell
cd web
npm ci
npm run dev
```

Open `http://localhost:5173/standards`. Vite proxies same-origin `/api` requests to the local API. Authenticated Web v1 requires the Web app and `/api/*` to share one browser origin; `VITE_API_BASE_URL` is not supported. The development database is `api/JaxPower.Ipqc.Api/ipqc-dev.db` (ignored by Git). The repo-local `dotnet-ef` tool is pinned in `dotnet-tools.json`. The backend solution includes an HTTP/SQLite integration test project; run `dotnet test api/JaxPower.Ipqc.slnx`.

For production setup, see [deployment checklist](docs/10_DEPLOYMENT_CHECKLIST.md), [PostgreSQL backup/restore runbook](docs/11_POSTGRES_BACKUP_RESTORE.md), and [Phase 1H UAT evidence](docs/12_PHASE_1H_UAT.md).

For a PostgreSQL deployment, set `Database__Provider=Postgres` and `ConnectionStrings__Ipqc` via environment or secret configuration, then apply the migration with `dotnet tool run dotnet-ef database update` from the API project directory. Never commit a real connection string. Production requires PostgreSQL, HTTPS at the hosting/reverse-proxy boundary, a strong secret `Authentication__JwtSigningKey` (at least 32 UTF-8 bytes), and an explicitly configured initial `BootstrapAdmin__Username`, `BootstrapAdmin__DisplayName`, and `BootstrapAdmin__Password` when the user table is empty. The initial Admin must change their password. Bootstrap never resets an existing account. Local account credentials are shared by Web and future Android clients. Development can seed four local accounts only when the database is empty and `DevelopmentSeed__Password` is supplied. There are no default passwords. Audit actors are authenticated usernames.

The server uses [ClosedXML 0.105.0](https://www.nuget.org/packages/ClosedXML/0.105.0) for `.xlsx` template/import/export. ClosedXML is [MIT-licensed](https://github.com/ClosedXML/ClosedXML/blob/develop/LICENSE), suitable for internal commercial use with its license notice retained in dependency distributions. Excel processing is server-side; imports accept one template-shaped sheet, up to 1,000 inspection items and a configurable maximum upload size (`Excel__MaxUploadBytes`, default 5 MiB). Export downloads the selected Patrol Standard. Excel import/export is not available for the other mock modules.

Known limitations: no PostgreSQL server integration test in this repository (provider SQL is generated and checked, while automated relational tests use SQLite); no Android UI or Material Verification backend; no inspection photo/file storage. Local HTTP is for development only. The scheduler defaults to a 60-second poll and 24-hour catch-up window; see `docs/04_ARCHITECTURE.md` for recurrence, DST, snapshots, and workflow rules.

### Phase 1E local development

Apply the new `Phase1ETaskExecutionApproval` migration before running the Phase 1E API. Phase 1D development databases may contain task shells without generation-time snapshots. These rows remain distinguishable by null snapshot fields and cannot be submitted. Reset disposable local development task data or use a fresh database; do not reconstruct historical snapshots from today's Standard. Preserve any local data you need before a reset.

Assigned IPQA API requests require a local account bearer token. Task ownership is checked by `ApplicationUser.Id` against `PatrolTask.AssignedInspectorUserId`. The username remains a task-time audit snapshot. IPQA Patrol Tasks review web route remains blocked; Admin/PQE can list and review tasks in the web app.

### Phase 1F local development

Apply the `Phase1FCompletedRecordsReporting` migration. `/api/completed-patrol-records` is a read-only view over `PatrolTask` rows with `Status = Completed`; there is no separate Completed Record table. Detail, preview, and `.xlsx` use the task-time definition snapshot joined to the final immutable submission selected by `CurrentRevisionNo` and its matching Approved review. Earlier rejected revisions remain in audit history. Inconsistent completed workflow data returns HTTP 409. The workbook has `Inspection Report` and `Revision History` sheets.

The Completed Records page accepts plant-local completion dates (`YYYY-MM-DD`) in `America/New_York`; the API converts boundaries to UTC with DST rules. Report and browser times use the same plant timezone. Admin, PQE, and IPQA can read/export Completed Records; IPQA Web remains limited to that module.

### Phase 1G local authentication

`POST /api/auth/login` accepts username/password and client type (`Web` or `Native`). Access JWTs last 15 minutes by default and carry user ID, username, and auth version; each authorized request checks the current database user, active status, auth version, and permissions. Random rotating refresh tokens are stored only as SHA-256 hashes, with a 14-day absolute session expiry by default. Web refresh tokens use a same-origin HttpOnly, SameSite Strict cookie (`Secure` outside Development); access tokens stay in memory. Native clients receive the refresh token in the response. Logout revokes the refresh session. Password change and Admin reset revoke all sessions. New and reset users must change their password before business API access. Passwords use ASP.NET Core's password hasher and require 10–256 characters with upper case, lower case, and a digit. Five incorrect logins lock an account for 15 minutes; Admin may unlock it. User Management has no hard delete, protects the final active Admin and active IPQA operational assignments, and uses stable User IDs in Plan assignment and Task ownership. Existing pre-1G Plan assignee and Task user IDs remain nullable for development migration compatibility; no historical relationship is fabricated.
