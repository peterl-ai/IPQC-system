# Web v1 deployment checklist

This checklist describes application requirements. JAX Power has not selected a production host, service manager, reverse proxy, or database operator.

## Required configuration

R32-02 adds optional-for-legacy, required-for-new-personal-API configuration: `PersonalAccess__EnvironmentId` (stable ordinary application environment, separate from historical Enrollment environment) and `LinePatrol__LineIds__0` / `LinePatrol__LineIds__1` (two distinct existing active ProductionLine GUIDs). Supply actual reviewed values through operations configuration; Git contains none. Missing/invalid settings produce visible 503 on the affected new surface, without breaking existing Web/login/line administration. No device/CA/mTLS setup is required. Normal HTTPS/hostname/chain trust on both tablets remains a separate IT gate. See [R32-02 contract and migration safety](16_R3202_PERSONAL_ACCESS.md). No production migration, feature rollout or cleanup activation is authorized by this document.

Supply these through the deployment secret/configuration system, never through committed files:

| Setting | Purpose |
|---|---|
| `Database__Provider=Postgres` | Required production EF provider |
| `ConnectionStrings__Ipqc` | Dedicated IPQC PostgreSQL connection |
| `Authentication__JwtSigningKey` | Secret signing key, at least 32 UTF-8 bytes |
| `Authentication__AccessMinutes` | Access token lifetime, default 15; supported 1–60 |
| `Authentication__RefreshDays` | Absolute refresh session lifetime, default 14; supported 1–14 |
| `BootstrapAdmin__Username` | Initial Admin username when user table is empty |
| `BootstrapAdmin__DisplayName` | Initial Admin display name |
| `BootstrapAdmin__Password` | Initial Admin password; must meet policy |

Set the ASP.NET Core environment explicitly to Production. The API requires PostgreSQL outside Development/Testing and rejects a missing or weak signing key. Bootstrap is for an empty user table only; it hashes the password, requires a password change, and never resets an existing account on restart. Remove bootstrap secrets from the runtime environment after initial setup if operations permit.

## Web, API, and TLS

Serve the built Vue site and `/api/*` on **one browser origin** over HTTPS. The API process may be behind a reverse proxy. The Web refresh token uses a same-origin HttpOnly, SameSite Strict cookie with Secure enabled outside Development; access tokens are kept in memory. Do not configure an arbitrary cross-origin API URL. Development Vite proxies `/api` to the local API.

If TLS terminates at a reverse proxy, verify trusted forwarded headers, scheme, host, and HTTPS redirect behavior for the selected topology. The project does not select IIS, Nginx, Linux, Windows Service, or a certificate system. Check that logs do not include passwords, bearer tokens, refresh tokens, or connection strings. Operations must choose log collection, rotation, retention, and alerting.

## Database and startup

1. Provision a dedicated PostgreSQL database and least-privilege application/migration credentials. Do not use the MES database.
2. Back up any existing database before migration. Apply repository EF migrations with the pinned `dotnet-ef` tool in a controlled deployment step using the production configuration. Review generated SQL before execution.
3. Start one scheduler-active API process initially. Scheduled generation is server-side and idempotent under the `(PatrolPlanId, ScheduledOccurrenceUtc)` unique key; multi-process scheduling needs an operational review before scale-out.
4. Verify HTTPS, initial Admin password change, login, one Standard/Plan/Task workflow, Completed report, and Web language toggle. Display plant time in `America/New_York`; persist UTC. Do not depend on the server's local timezone.
5. Follow [backup/restore runbook](11_POSTGRES_BACKUP_RESTORE.md) and record the first restore rehearsal.

## Decisions still required from JAX Power operations

- Production PostgreSQL host and DB operations owner.
- API/Web host OS, service manager, reverse proxy, TLS termination, and forwarded-header trust configuration.
- Backup owner, frequency, retention, offsite/isolated copy, and restore target.
- Monitoring, application log destination/rotation/retention, and incident ownership.
- Whether final report Excel must match legacy GQMS layout exactly.

Inspection image storage is not implemented in Web v1; its future storage and retention decisions remain open.

## R32-03 shared planning

See [R32-03 implemented contracts and verification](17_R3203_SHARED_PLANNING.md): separate current-family Admin/PQE shared Plan APIs; provider-specific planning/line/account serialization; ownerless 07/19 New York scheduling and immutable planning snapshots. Shared tasks remain read-only/non-executing. The single additive migration preserves historical null markers, old indexes/FKs and retired enrollment history. SQLite rebuild interruption after schema commit requires offline verified-backup restoration before retry; production rollout/restore remains an external gate, not authorized here. Legacy scheduling, authentication, reports and reservation/cleanup controls remain in effect.
