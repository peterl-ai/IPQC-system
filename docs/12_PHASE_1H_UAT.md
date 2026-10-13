# Phase 1H UAT and hardening evidence

Date: 2026-10-11 (America/New_York). Branch: `phase-1h-uat-hardening`. This document records checks actually run; it does not certify a production deployment.

## Backlog disposition

| Item | Disposition | Evidence / reason |
|---|---|---|
| H1 Quantitative readiness | Fixed | Standard create/update reject invalid quantitative bounds/counts; enabled Plan create/update/enable checks the referenced Standard; updates cannot make a Standard on an enabled Plan non-executable. Submit also rejects malformed frozen non-N/A Task sample counts without creating a revision or advancing status; blank counts still require one sample and N/A behavior is unchanged. HTTP tests exercise frozen Task snapshots as well as Standard/Plan readiness. |
| H2 Scheduler eager load | Fixed | Poll loads enabled Plan/assignee candidates; Standard and items load only when a missing due occurrence requires a Task snapshot. Existing idempotency, concurrent generation, catch-up, disable/re-enable, schedule edit, DST, and broken-plan tests pass. |
| H3 Qualitative `OK · OK` | Fixed | Detail and HTML preview format qualitative sample once; quantitative samples retain value plus judgment. Browser inspected the completed UAT record. Authoritative report DTO unchanged. |
| H4 Shared detail/report DTO | Verified, retained | Detail and report use one authoritative immutable final-revision resolver. No demonstrated performance benefit justified two DTOs and extra consistency risk. |
| H5 Corrupt ledger row | Fixed for final revision/review header integrity | Completed rows remain visible and expose `HasIntegrityIssue` when final revision or Approved review is missing. Detail/report/XLSX retain 409. The flag is a header-level signal; deeper item corruption still surfaces on detail/report. |
| H6 `/api/me` permissions | Fixed | `MustChangePassword=true` returns empty effective permissions; server policy was already restrictive. HTTP test added. |
| H7 Concurrent login failures | Fixed | Atomic conditional database update counts failures and sets 15-minute lockout at the fifth attempt. Concurrent SQLite HTTP test added. PostgreSQL behavior remains to be validated live. |
| H8 Refresh cookie expiry | Fixed | Cookie expiry uses the authoritative session's absolute expiry on login and rotation. Configured one-day lifetime and rotation expiry tested. |
| H9 Same origin | Fixed | Web API paths are `/api` on the current origin; misleading override guidance removed. Production requirement documented. |

## Local quality gates

| Command/check | Observed result |
|---|---|
| `dotnet restore api/JaxPower.Ipqc.slnx` | Passed; projects up to date |
| `dotnet tool restore` | Passed; pinned `dotnet-ef` 10.0.12 |
| `dotnet build api/JaxPower.Ipqc.slnx --configuration Release` | Passed; 0 warnings, 0 errors |
| `dotnet test api/JaxPower.Ipqc.slnx --configuration Release --no-build` | Passed; 67 tests, 0 failed/skipped |
| `npm ci` | Passed; 248 packages, 0 reported vulnerabilities; npm emitted dependency install-script notices |
| `npm run lint` | Passed; 0 lint warnings |
| `npm run test` | Passed; 44 tests in 12 files |
| `npm run typecheck` | Passed |
| `npm run build` | Passed; Vite reported a bundle-size advisory for the 1.71 MB JS chunk |
| Fresh SQLite `dotnet-ef database update` | Passed through Phase 1G migration on a disposable database; no Phase 1H schema change |
| `dotnet-ef migrations has-pending-model-changes` | No pending changes |
| PostgreSQL idempotent migration SQL | Generated and inspected locally; 26,544 bytes, includes task occurrence unique index |
| GitHub Actions CI on PR #8 | Frozen Task fix commit `7ac52d1` passed backend and frontend jobs in both push and pull request runs. The [workflow status for the current branch](https://github.com/peterl-ai/IPQC-system/actions/workflows/ci.yml?query=branch%3Aphase-1h-uat-hardening) tracks the latest PR head. |

The initial sandboxed .NET/Vitest commands encountered local named-pipe/`EPERM` restrictions. The reported passing commands ran outside that sandbox against the same checkout. No tests were deleted.

## Browser and API UAT

A disposable local SQLite database, local API, and Vite Web app were used. Development seed users were initialized with a local test password supplied only through the process environment; it is not in Git.

| Scenario | Observed result |
|---|---|
| Admin login and bilingual Web | Admin login reached Home. English and Simplified Chinese toggle changed visible navigation and page copy. Chinese User Management loaded and showed plant-local timestamps. |
| PQE/IPQA Web RBAC | PQE reached Standards and was routed to 403 for Users. IPQA landed on Completed Records and was routed to 403 for Users. Direct backend role tests are part of the 67-test suite. |
| Scheduler to final report | API created a three-item Standard and enabled daily Plan. Real scheduler generated one assigned Task at the configured plant-local time. IPQA saved/submitted qualitative OK, two quantitative samples, and N/A: revision 1 Qualified. PQE rejection without reason returned 400; reasoned rejection preserved revision 1. IPQA corrected a quantitative value to NG and resubmitted revision 2 Unqualified. PQE approved. Ledger exposed revision 2 with no integrity flag; report retained two revisions; XLSX download returned 9,475 bytes. |
| Completed browser detail/preview | Chinese ledger showed the Unqualified completed row and New York completion time. Detail showed both revisions, rejection reason, N/A, and multiple samples. Qualitative detail showed a single result (`样本 1 · 合格`); HTML preview showed `1: 合格`, while quantitative showed value plus judgment. |
| Browser console | No errors or warnings after authenticated role/report checks. Initial anonymous `/api/auth/refresh` produced an expected 401 resource-console entry during session discovery. |

Automated backend tests cover scheduler duplicate/concurrent generation, recovery/catch-up, disable/re-enable, schedule edit, DST, invalid assignee/per-plan isolation, submission/review revision history, Completed integrity conflicts, Excel formula safety, auth/session/RBAC, and pagination behavior. This browser pass was a targeted UAT, not a pixel-level review of every page in both languages.

## Production validation still open

- No local `docker`, `psql`, `pg_dump`, or `pg_restore` executable was available. **No live PostgreSQL migration/API smoke or restore rehearsal was run.** Generated PostgreSQL SQL and SQLite tests do not replace live provider validation.
- GitHub Actions passed for the frozen Task fix commit `7ac52d1` on PR #8; check the linked workflow status for the latest PR head before merge.
- Production PostgreSQL host, deployment host, reverse proxy/TLS, backup owner/retention, monitoring/log retention, and future file storage remain JAX Power operational decisions. See the deployment checklist and backup runbook.
