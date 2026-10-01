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

A unique key such as `(PatrolPlanId, ScheduledOccurrenceUtc)` should be considered.

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
