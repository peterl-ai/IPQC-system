# 04 — Architecture

## Target system

```text
┌──────────────────────┐
│ Vue Web Application  │
│ Admin / PQE / IPQA   │
└──────────┬───────────┘
           │ HTTPS JSON
           ▼
┌───────────────────────────────┐
│ ASP.NET Core Web API          │
│ - Authentication/RBAC         │
│ - Line Patrol domain logic    │
│ - Scheduler / task generation │
│ - Excel/report generation     │
│ - Attachment API              │
└──────────┬──────────────┬─────┘
           │              │
           ▼              ▼
┌──────────────────┐   ┌─────────────────────────┐
│ IPQC Database    │   │ File Storage            │
│ PostgreSQL       │   │ image/report files      │
└──────────────────┘   └─────────────────────────┘

Future:
┌──────────────────────┐
│ Android IPQA App     │
│ Android 11 compatible│
└──────────┬───────────┘
           └──────────────→ same HTTPS API

Future Material Verification integration:
MES ── API/read-only integration adapter ── IPQC/MV module
```

## Database decision

### Development / automated tests
SQLite is acceptable for:
- local development
- lightweight automated tests
- temporary prototypes

### Production
Use a dedicated **server relational database**. Preferred default: **PostgreSQL**.

SQL Server is also a valid choice if JAX Power IT already standardizes and operates it.

Do not use SQLite as the production shared database because the system will have:
- concurrent Web/API/Android users
- scheduled writes
- approval/resubmission writes
- long-lived operational history
- backup/restore and audit requirements

Do not use MES production DB as the IPQC application database.

## MES integration boundary
Line Patrol must work without MES.

When Material Verification is implemented later:
- prefer MES API/service integration
- if direct DB access is unavoidable, use a dedicated read-only integration account through an adapter/service
- do not create cross-database foreign-key dependencies into MES tables
- do not persist IPQC transactional data inside MES DB

## Scheduling
Patrol Plan scheduling and Patrol Task creation must run on the server, not in the Vue browser.

Requirements:
- idempotent task generation
- restart-safe
- prevent duplicate tasks for the same plan/scheduled occurrence
- audit generation timestamp/source

Phase 1D enforces a unique key on `(PatrolPlanId, ScheduledOccurrenceUtc)`.

### Phase 1D scheduler contract
- Plans store a normalized structured schedule: Every N Hours (1–168), Daily times, or Weekly weekdays/times. The web editor uses plant-local wall-clock values; the API converts them using the persisted `America/New_York` IANA timezone.
- The ASP.NET Core `BackgroundService` polls every 60 seconds by default. Its independently testable generation service evaluates due occurrences in a configurable 24-hour catch-up window. It never pre-generates future tasks.
- Every N Hours is anchored to the plan's effective start wall-clock time. Spring-forward nonexistent local times are skipped; fall-back ambiguous times use the earlier UTC instant once.
- The effective start and optional effective end are inclusive. `GenerationNotBeforeUtc` is exclusive. A schedule-affecting edit or re-enable advances that boundary, so the new definition cannot create historical tasks or backfill a disabled period. Generated tasks are not changed by later plan edits or disabling.
- One enabled plan has exactly one active execution assignee. One due occurrence creates one task. A unique database index on `(PatrolPlanId, ScheduledOccurrenceUtc)` is the final concurrency guard; duplicate insert races are handled as an idempotent outcome.
- Task shells contain `PendingInspection`, the selected standard ID, assignee key, scheduled UTC occurrence, generated UTC timestamp, and `Scheduler` generation source. Inspection values, review history, and immutable standard/item snapshots belong to Phase 1E.
- Until Phase 1G, assignees use stable development keys `dev-ipqa-1` and `dev-ipqa-2`; these are fixtures, not production user identities. A blank Plan No. receives `PLN-` plus a server GUID; Task No. uses `PT-` plus a server GUID. These are implementation identifiers, not approved business numbering formats.

## Time handling
- Persist timestamps in UTC.
- Display in plant-local timezone.
- Schedule definitions must explicitly record the plant timezone.

## Reports
- Browser preview rendered as HTML/modal.
- Excel file generated server-side.
- Prefer `.xlsx` for the new system.

## Legacy migration
None. Jinko GQMS remains the system used for historical lookup before JAX IPQC go-live.
