# AGENTS.md — Coding Agent Contract

## Mission
Build the JAX Power IPQC system according to the repository documentation. Treat `docs/` as product requirements. Do not invent business rules when the docs are silent.

## Mandatory working rules
1. Read `README.md` and the relevant `docs/` files before changing code.
2. Work only on the requested phase / PR scope.
3. Do not implement Material Verification workflow. It is intentionally deferred to a future MES-vendor integration phase.
4. Do not implement Android business workflow unless explicitly requested. Future Android compatibility must still be considered in API/domain design.
5. Web must support English + Simplified Chinese through i18n. Do not hard-code visible display strings in components.
6. Android is English-only and must remain compatible with Android 11 / Samsung Galaxy Tab A7 SM-T500.
7. Do not add legacy GQMS Base or Business Unit fields.
8. Patrol Standards do not have legacy Submit / Approve / Upgrade actions.
9. PQE rejection of a submitted patrol task requires a reason and returns the task to IPQA for correction/re-inspection and resubmission.
10. Completed Patrol Records is a history module, not a dashboard.
11. Report preview is web HTML/modal. Report download is Excel (`.xlsx` preferred).
12. Do not migrate legacy GQMS history into the new DB.
13. Do not use MES DB as the application database. Future MES integration should be isolated behind an integration/API layer.
14. Do not store image binaries in the relational database. Persist attachment metadata/storage keys instead.
15. Add/update tests for every implemented business rule.
16. Keep changes reviewable and avoid unrelated refactors.

## Quality gates
- TypeScript strict mode.
- Lint, tests, and production build must pass for touched projects.
- No secrets, internal passwords, FTP credentials, tokens, or production connection strings committed.
- No raw production/customer/employee data in fixtures.
- Use stable IDs rather than display names for relationships.
- Server-side authorization must remain the source of truth once the API exists; UI hiding alone is insufficient.
- Scheduled task creation must be server-side and idempotent.

## Completion report
At completion, report:
- branch name
- files changed
- implemented behavior
- tests/build commands and exact results
- assumptions/open questions
- intentionally deferred work
