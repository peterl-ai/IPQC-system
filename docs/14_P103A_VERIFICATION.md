# P1-03A review evidence

> Historical evidence for the original P1-03A PR. [Rev3.2](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md) supersedes its device-authentication direction; R32-01 retires active registration and signing. The old 187/72 results and deferred P1-03B instructions below are not R32-01 evidence or authorization. See [R32-01 results](15_R3201_VERIFICATION.md); do not repeat the old probe/enabling procedure.

Baseline verified against origin/main: `a8a4528a83f31865d85f13a09b4fda986e6a9883`.
Feature branch: `codex/p1-03a-device-enrollment`. No merge or deployment authorized/performed. Approved Rev3.1 / binding F2 documents are unchanged. Pre-existing untracked user files are excluded.

## Local final gates (2026-11-09)

| Gate / command | Result |
| --- | --- |
| `dotnet tool restore` | PASS, EF 10.0.12 |
| `dotnet build api/JaxPower.Ipqc.slnx --configuration Release --no-restore` | PASS, 0 warnings / 0 errors |
| `dotnet test api/JaxPower.Ipqc.slnx --configuration Release --no-build` | PASS, 187 passed / 0 failed / 0 skipped |
| Focused `dotnet test api/JaxPower.Ipqc.slnx --configuration Release --no-restore --filter FullyQualifiedName~EnrollmentRevocationTests` | PASS, 8 passed / 0 failed / 0 skipped |
| `npm run lint` (web) | PASS, 0 warnings |
| `npm run test` (web) | PASS, 72 passed / 0 failed / 16 files |
| `npm run typecheck` (web) | PASS |
| `npm run build` (web) | PASS; existing large-bundle advisory remains |
| EF `migrations has-pending-model-changes`, Sqlite | PASS, no changes |
| EF `migrations has-pending-model-changes`, Postgres | PASS, no changes; no DB connection used |
| PostgreSQL idempotent migration SQL generation/review | PASS; native types, restrictive FKs, filtered uniqueness, truthful Admin actor default, no historical Task rewrites/drop |
| `git diff --check` and staged whitespace check | PASS |
| Staged private-key-block / GitHub-token pattern check | PASS; synthetic CA/private keys are runtime-only in temporary directories |
| SM-T500 / actual Android Keystore signing / probe APK build | **SKIPPED**: no JDK, Android SDK, adb, Gradle or PAD available |
| Live PostgreSQL migration / concurrency | **SKIPPED**: no disposable server, psql or Docker available |
| Real IT CA issuance/recovery/revocation | **SKIPPED**: no IT CA adapter/credentials available; default PKI fails closed |

The xUnit skipped count above describes executable local tests, not the separately unavailable environment gates. No simulated test is reported as real PAD/live PostgreSQL/production CA evidence. GitHub backend/frontend CI is checked separately against the PR HEAD; its final result is recorded in the PR and delivery response.

## Acceptance evidence

- `DeviceEnrollmentTests`: real HTTP create/challenge/proof/approval/receipt flow, secret hashing and one-time consumption, wrong key/DER/algorithm/payload/length/padding/purpose, expiry/CSR curve/subject/key pinning, Admin/PQE/IPQA/anonymous/forced-change/revoked-token boundaries, disabled production/default Bootstrap, body/challenge limits, truthful Admin/ApplicantKey/System actors and no Device actor.
- `EnrollmentBindingTests`: correctly signed but altered environment, DeviceId, LineId, source/target versions, CSR/SPKI hash, nonce and ChallengeId; tampered CSR signature; stale Admin snapshots; unavailable issuer; environment drift/rebind invalidation; expired original candidate preserved/revoked on a new invitation.
- `DeviceEnrollmentConcurrencyTests`: concurrent invitation/proof/approval/worker operations, one winner or same-idempotent result, signer outside DB transactions, late issuance after cancel/rebind/unbind/revoke/replace, correlated provisioning/invalidation ChangeIds, original PEM recovery after lost response/restart, Unknown never re-signs, invalid issuer result rejected, audit rollback including replacement's multi-save transaction and post-signing finalization recovery.
- `EnrollmentRevocationTests` (PR #11 first-round P2): all 8 regressions fail against reviewed HEAD `c5300325400f8a6875005416b7b1a7dd83e4d9f9` and pass with the lifecycle fix. A Failed candidate with no stored certificate retains its pending revocation across cancel, rebind, unbind, revoke, replacement, worker expiry and expiry during a new invitation. The recording adapter delegates to the isolated development issuer: it verifies the original issuance operation is called, failed cleanup and an in-flight signer leave the flag set, acknowledgement creates the test tombstone and commits one completion audit, and a later worker pass does not repeat completed cleanup. A separate audit-failure case proves rollback preserves pending state and retries the same idempotent signer operation after lease expiry; completion audit/state commit occurs once. Immutable candidate/request identity, prior audit rows, correlated provisioning ChangeIds and candidate non-authority remain intact. This is isolated issuer evidence, not real IT CA execution.
- `EnrollmentInteropTests`: independent OpenSSL DER -> JOSE -> ASP.NET and ASP.NET -> DER -> OpenSSL verification, malformed integer/sign-padding/leading-zero cases, repository-path refusal. This is software wire-format evidence only.
- `EnrollmentMigrationTests`: populated P1-02 upgrade compares **every original column** (including actual user actors, provisioning identities and snapshots); fresh schema/FK/state/actor constraints; retained candidate/audit history; PostgreSQL script assertions; destructive Down refused.
- Existing `ProvisioningMigrationTests`, `WorkflowLineMigrationTests`, DeviceRegistry/ProductionLine, task/review/scheduler/completed-history/auth tests remain passing, including populated historical legacy upgrades and both-provider model-diff checks. Legacy mutation/read isolation is unchanged.
- Web enrollment tests exercise explicit physical verification before approval, full immutable identity/fingerprint snapshot, retained operation ID after uncertain HTTP response, no automatic mutation retry, invitation visibility without persistence, cancellation reason/revision, candidate-only/cleanup display, pagination, terminal device history, bilingual actor provenance and stable IDs. Existing routing/role guards and all prior Web tests remain passing.

## Migration safety and review boundaries

New migration `20261109202320_P103ADeviceEnrollment` adds three enrollment/candidate/challenge tables, checks, unique indexes and Restrict FKs. Audit gains nullable human actor and explicit actor kind/device/request/credential references; existing audit rows preserve actual human identity and receive Admin=1. No RegisteredDevice is promoted; no existing line/version/task/report is rewritten. SQLite audit-table rebuild needs backup/restore verification and a maintenance window. PostgreSQL SQL was generated to a temporary file (`ipqc-p103a-postgresql.sql`), SHA-256 `5510D5771039E3C7A8655AD6B8F320ACD00F05477FB70F1939AE187F217BA20D`; generated SQL review is not execution evidence.

Keep expanded schema for application rollback; do not destructively downgrade away audit/candidate history. Pre-P1-03A EF audit readers assume non-null ActorUserId and can fail on new System/ApplicantKey rows. Retain a compatible typed audit reader or apply a reviewed compatibility patch; an unmodified P1-02 binary is not an audit recovery plan. No production database was touched.

The only signer is explicit isolated development/test PKI with externally provisioned private material and a durable public receipt journal. It is not a production CA or CRL/OCSP service. Receipt-volume loss/power-loss durability, on-site key provenance, real IT CA semantics and live PostgreSQL races remain environment-specific risks. Production Bootstrap is hard-disabled. P1-03B must separately provide Native mTLS/trusted transport and DB-authoritative confirmation/activation; no candidate, key proof or reservation is an authenticated PAD.

On-site evidence checklist: `android/enrollment-probe/README.md`. Protocol, API, configuration, operation recovery, TTL/rate limits and deferred transport: `docs/13_DEVICE_ENROLLMENT.md`.

First-round review fix changes no schema, migration or approved architecture/F2 document. PostgreSQL regenerated SQL retains the SHA-256 recorded above. Bounded receipt-challenge retention/cleanup and anonymous abuse hardening remain a P1-03B follow-up before production Bootstrap can be enabled; no P1-03B implementation is included.
