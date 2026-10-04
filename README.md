# JAX Power In-Process Quality Control (IPQC) System

Internal JAX Power IPQC platform that replaces the **IPQC / Line Patrol portion** of Jinko GQMS for new JAX Power operations.

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

Current plant tablet reference:
- Samsung Galaxy Tab A7
- Model: SM-T500
- Android 11
- One UI Core 3.1

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

Phase 1C made **Patrol Standards Management** persistent. Phase 1D adds persistent Patrol Plans and server-generated task shells. Patrol Task execution, Completed Records, and User Management remain later-phase modules.

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

Open `http://localhost:5173/standards`. Vite proxies `/api` to the local API. `VITE_API_BASE_URL` can override the API base path if needed. The development database is `api/JaxPower.Ipqc.Api/ipqc-dev.db` (ignored by Git). The migration is `InitialPatrolStandards`; the repo-local `dotnet-ef` tool is pinned in `dotnet-tools.json`. The backend solution includes an HTTP/SQLite integration test project; run `dotnet test api/JaxPower.Ipqc.slnx`.

For a PostgreSQL deployment, set `Database__Provider=Postgres` and `ConnectionStrings__Ipqc` via environment or secret configuration, then apply the migration with `dotnet tool run dotnet-ef database update` from the API project directory. Never commit a real connection string. Production requires PostgreSQL, HTTPS at the hosting/reverse-proxy boundary, and a future authentication/RBAC integration before Patrol Standards endpoints can be enabled. The API deliberately fails closed outside Development; the `X-Dev-Role` header (`admin` or `pqe`) is a **development-only** test identity, not production authentication. The browser role switcher is also development-only. Audit actor values are temporary server-side development role labels; real user identity remains an open decision.

The server uses [ClosedXML 0.105.0](https://www.nuget.org/packages/ClosedXML/0.105.0) for `.xlsx` template/import/export. ClosedXML is [MIT-licensed](https://github.com/ClosedXML/ClosedXML/blob/develop/LICENSE), suitable for internal commercial use with its license notice retained in dependency distributions. Excel processing is server-side; imports accept one template-shaped sheet, up to 1,000 inspection items and a configurable maximum upload size (`Excel__MaxUploadBytes`, default 5 MiB). Export downloads the selected Patrol Standard. Excel import/export is not available for the other mock modules.

Known limitations: no production authentication/identity provider yet; no PostgreSQL server integration test in this repository (the provider SQL is generated and checked, while automated relational tests use SQLite); no Patrol Task execution/approval, Android, report, or Material Verification backend; no inspection photo/file storage. Local HTTP is for development only. UI mock data outside Patrol Standards and Patrol Plans does not persist. The scheduler defaults to a 60-second poll and 24-hour catch-up window; see `docs/04_ARCHITECTURE.md` for recurrence, DST, and task-shell rules.
