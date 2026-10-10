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
- Generated tasks contain `PendingInspection`, the selected standard ID, assignee key, scheduled UTC occurrence, generated UTC timestamp, and `Scheduler` generation source. Phase 1E adds the immutable Plan/Standard header and ordered Standard Item snapshot in the same task insert. Existing tasks never execute against a later Standard edit.
- Phase 1G Plan assignments use active IPQA `ApplicationUser.Id`; generated tasks freeze that User ID and the username snapshot. Pre-1G development rows retain nullable User IDs rather than invented historical links. A blank Plan No. receives `PLN-` plus a server GUID; Task No. uses `PT-` plus a server GUID. These are implementation identifiers, not approved business numbering formats.

### Phase 1E execution and review
- The current draft stores Shift (`Day`/`Night`), explicit nullable N/A answers, and ordered samples. Temporary Save permits incomplete work and never creates a submission. Rejected tasks remain `Rejected` while corrections are saved.
- A non-N/A Qualitative sample requires IPQA `OK` or `NG`; a Quantitative sample requires a number and receives a server-calculated judgment from its frozen limits. Supported operators are `>`, `>=`, `<`, and `<=`. Unusable quantitative configuration blocks submission. The required sample count is the frozen positive `SampleCount`, or one by default; no more than 50 samples per item are accepted.
- Every item must answer N/A before submission. N/A is judged `NA` and needs no samples. Any NG sample makes its item NG; any NG item makes the task `Unqualified`, otherwise a validated task is `Qualified`. Shift is required on submit. Photo requirements remain snapshot information and are not submission blockers in Phase 1E.
- Submission atomically freezes Shift, item judgments, item/sample inspection times, sample values/results, and optional metadata into numbered immutable revisions. Unchanged draft inputs preserve their original inspection times; changed or cleared inputs update or clear the mutable timestamp. PQE approval or rejection creates one review per revision; rejection requires a reason and returns the task to IPQA for correction and resubmission. The review uniqueness key and conditional task update protect competing reviewers. Batch approval commits all selected pending tasks or none.
- IPQA identity comes from the authenticated user ID. Execution ownership compares that ID to the task's assigned User ID; request headers and display keys cannot grant ownership. Android UI remains deferred.

## Time handling
- Persist timestamps in UTC.
- Display in plant-local timezone.
- Schedule definitions must explicitly record the plant timezone.

## Completed records and reports (Phase 1F)
- A Completed Record is a `PatrolTask` with `Status = Completed`; no duplicate transactional record table exists.
- The read-only report service requires `CurrentRevisionNo`, its matching immutable submission, and its matching Approved review. Missing final workflow data returns HTTP 409. Definition fields come from task-time item snapshots; results and inspection timestamps come from the final immutable submission. Earlier rejected revisions and reasons remain visible in history.
- The paginated list projects header fields without loading item/submission graphs. A `(Status, CompletedAtUtc)` index supports history queries.
- Plant-local completed-date filters and report times use `America/New_York`, with UTC persistence and DST-aware conversion.
- Web preview is HTML over the report DTO. ClosedXML generates a two-sheet `.xlsx` from that same DTO (`Inspection Report`, `Revision History`), with business strings written as text.
- Admin, PQE, and IPQA can read/export Completed Records through server-side permission policies.

## Phase 1G authentication and users
- Local `ApplicationUsers` hold unique normalized usernames, ASP.NET Core password hashes, role, active/forced-change/lockout state, auth version, and username audit fields. No hard delete is exposed.
- Signed 15-minute access JWTs identify user ID and auth version. The API reloads the user for each request, rejecting inactive or stale sessions and deriving current permission claims from the stored role. New/reset users have no business permissions until password change.
- Random rotating refresh tokens are SHA-256 hashed in `RefreshTokenSessions`; the 14-day expiry is absolute across rotations. Web refresh uses a same-origin HttpOnly, SameSite Strict cookie, Secure outside Development; native clients receive the token in the response. Logout, password change/reset, role change, and deactivation revoke sessions.
- Production requires PostgreSQL, HTTPS, a secret signing key, and an explicitly configured initial Admin for an empty user table. Development seeding requires a supplied password and only runs for an empty user table. Web and future Android share this backend. Entra/O365/LDAP are outside v1.

## Legacy migration
None. Jinko GQMS remains the system used for historical lookup before JAX IPQC go-live.
