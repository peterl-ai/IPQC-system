# IPQC P1-03 — Codex Stage A (READ-ONLY) Scope & Security Architecture Audit

**Date:** 2026-11-09 (America/New_York)

**Repository:** https://github.com/peterl-ai/IPQC-system  
**Verified merged `main` baseline:** `1ad5470af330b534c7c55e101010fcc4e15d4eb5` (PR #10)  
**Previous PRs:** P1-01 PR #9 and P1-02 PR #10 are merged.  
**Requested mode:** Stage A / read-only discussion and bounded PR planning only. **Do not implement.**

## 0. Mandatory source order and operating rules

Read the following documents completely before forming conclusions:

1. `IPQC_ANDROID_ARCHITECTURE_V1_REV3_1_2026-11-06.md` — APPROVED source architecture, especially §4.1–4.3, §5, §14–15.
2. `IPQC_ANDROID_ARCHITECTURE_V1_IMPLEMENTATION_HANDOFF_2026-11-06.md` — approved implementation handoff; §4.2 F2 A/B handoff is a **binding addendum**. Do not reopen F2, F1 or F3.
3. Repository `AGENTS.md`, `CLAUDE.md`, `README.md`, relevant `docs/` design, RBAC, Android device target, deployment, storage and backup notes.
4. The actual merged code, tests, EF migrations and Web routes at the verified `main` HEAD.

The local architecture Markdown files may be untracked. Read them as provided; do not silently infer that they have been committed. Distinguish approved TO-BE requirements from implemented AS-IS behavior. If a cited requirement is missing or conflicts, report the exact section and impact; do not improvise an unapproved business policy.

Confirm actual branch, local HEAD, remote main, `git status --short`, prior migration chain, CI status and available runtime/deployment tooling. Protect all untracked files (`handoff/`, `pr_diff/`, `prompts/`, local architecture docs and any others).

## 1. Goal and immutable boundary

**P1-03 candidate goal:** convert P1-02's Admin-only `RegisteredDevice` / pending Line reservation into a **cryptographically verified, Admin-authorized device identity** using Android Keystore public/private key proof, a short-lived single-use enrollment process, client-certificate issuance, and a separate Native mTLS transport. The same application server must retain existing Web same-origin functionality. This is a security infrastructure phase, **not** LineShared Task activation.

Baseline facts to verify:

- P1-02 `RegisteredDevice` has `PendingCredential`, `Revoked`, `Replaced`, `LineId`, `Revision`, `BindingVersion`, audit relationships and uniqueness; provisioning metadata has **no device authority**.
- `ProvisioningStore` serializes provisioning mutations: SQLite non-deferred writer transaction and PostgreSQL transaction-scoped advisory lock `49505102` with a five-second lock timeout.
- New Admin-only `/api/admin/production-lines`, `/api/admin/devices` and Web `/provisioning` exist; no pairing, certificate or Native trusted identity endpoint exists.
- The existing `/api/auth/login` accepts `ClientType=Native` and returns a personal refresh token. **This is user authentication, NOT device authentication or a device-bound NativeSession.**
- Existing legacy Web/API Plans, Tasks, reports, JWT and RBAC must keep working as before.

**Must not add in P1-03:** Android inspection screens, Room/cache/WorkManager, new task creation/schedule, shared drafts, F1 contributor operations, F2 Submit coordination, photo ingestion, NativeSession/OfflineGrant business rules (P2), Material Verification, MES, production deployment or automatic approval of a user to perform LineShared tasks. A small test enrollment client/harness may be proposed without creating the full Android application; state exactly what it proves and does not prove.

## 2. Threat model / explicit non-negotiable security properties

Audit and propose a defensible implementation for these properties:

1. **No trust from metadata.** An Admin-created row, line reservation, serial number, Android name, public IP, claimed DeviceId, header, or personal JWT alone never authorizes a PAD.
2. **Server-backed state transitions.** Enrollment approval, trust issuance, revocation, credential rotation, rebind and replacement must be serialized against P1-02 state changes. Use the existing transaction protocol or identify the minimal necessary correction.
3. **Prove private-key possession.** Device key pair generated in Android Keystore, private key non-exportable. Enrollment binds a fresh challenge to the intended registry device, pending reservation, public key, nonce, expiry and BindingVersion. Verify a signature and/or a signed CSR with correct key linkage; never accept a bare public key without proof.
4. **Short-lived, single-use token.** Bootstrap tokens/challenges are high entropy, stored server-side as hashes (or comparably safe design), scoped to a specific device/reservation/version, expiring and consumed atomically; replay and parallel redemption fail. A token is not an authorization credential for task APIs.
5. **Admin approval.** Only authorized Admin can approve a matching request after reviewing stable device identity, fingerprint/challenge and selected LineId. Never silently enroll a PAD from its unverified request alone. Define explicit cancel/expiry/reissue behavior and audit visibility.
6. **Certificate lifecycle.** A trusted client certificate must be issued only after the proof/approval conditions hold. Define CA trust, clientAuth EKU, validity, serialization, thumbprint/public-key fingerprint matching, generation/renewal/replacement/revocation and secure private issuing-key custody. CA/private signing keys MUST NOT be generated or shipped as committed production secrets. Determine whether an IT-managed signer/PKI adapter is needed rather than embedding production CA credentials in code.
7. **Transport proof.** A distinct Native TLS hostname or port on the *same physical server* must genuinely require/validate client certificates. Keep Web same-origin unbroken. Distinguish bootstrap HTTPS entry from mTLS-protected Native API; do not depend on switching certificate requirements by URL path on a connection that has already negotiated TLS. Explain concrete feasible listener/proxy topologies.
8. **Trusted TLS termination.** If proxy terminates mTLS, it validates certificates, strips user-supplied certificate/identity headers, and forwards identity exclusively over a documented trusted backend channel. Never turn on generic forwarded-cert header support without a hardened proxy trust boundary. Define direct-to-backend bypass prevention. Consider Kestrel-terminates-TLS as an alternative.
9. **Every-request authorization.** Verify current device record, cryptographic credential/certificate association, non-revoked state, LineId and BindingVersion on EACH future Native business authorization, including connections reused after revoke/rebind. Certificate authentication alone is insufficient; a stale trust cache must not permit revoked devices.
10. **Combined identity later.** Future new Task execution requires verified device + actual authenticated IPQA + device-bound NativeSession + matching Task.LineId. P1-03 must not accidentally grant these business privileges ahead of P2. Web PQE/Admin authorization stays independent of PAD mTLS.
11. **Failure-safe behavior.** Lost response, duplicate request, stale approval, conflicting admin action, invalid CSR/key, expired nonce, old certificate after rotation, unknown CA, broken proxy trust, and interrupted issuance all fail closed without falsely marking a device enrolled. Design receipt/status reconciliation instead of silently restarting issuance or risking duplicate authoritative state.
12. **Preserve history.** Existing DeviceId and old binding/audit facts remain attributable to the original PAD. A replaced or revoked terminal identity must not be silently reactivated or have its old work transferred. Increment BindingVersion on credential/trust changes per P1-02 contract.

**Special design issue to settle in Stage A:** How to safely make bootstrap reachable by an unpaired PAD while keeping normal Native API genuinely mTLS-only. Present a recommended endpoint/listener topology and rejected alternatives with justification.

**Real Android requirement:** Samsung Galaxy Tab A7 SM-T500 / Android 11, minSdk 30. Android Keystore non-exportable key is required. Do NOT silently impose StrongBox, hardware attestation or device integrity attestation as mandatory; inspect/document hardware-backed capability as an early real-PAD validation. Explicitly analyze install/renewal of the client certificate chain under an Android Keystore private key and feasibility with the chosen Android HTTP client. A local .NET/OpenSSL test certificate is NOT proof of SM-T500 Keystore compatibility.

## 3. Deep repository audit — inspect these concrete touchpoints

- `api/JaxPower.Ipqc.Api/Program.cs` — host, endpoints, JWT setup, middleware order, environments, CORS, production configuration; ensure Web and Native authentication schemes cannot be confused.
- `api/JaxPower.Ipqc.Api/Endpoints/AuthEndpoints.cs` and `Services/LocalAuthService.cs` — distinguish existing personal Native login/refresh from new device auth; avoid regressing personal login, lockout, password changes or auth-version revocation.
- `Domain/RegisteredDevice.cs`, `Domain/ProductionLine.cs`, `Domain/ProvisioningAuditEvent.cs`, `Data/IpqcDbContext.cs` and latest migration `P102DeviceRegistryProvisioning` — state constraints, version counters, immutable relationships, indexes, audit semantics and future credential storage.
- `Services/ProvisioningStore.cs` and `DeviceRegistryService.cs` — shared transaction/lock, stale writes, line reservation rules, revocation/replacement and audit event generation; identify every state-changing route that must invalidate credential authority.
- Admin endpoints, contracts, permissions, Web `/provisioning` page and EN/ZH locale files — smallest Admin pairing UX extension. Preserve Admin-only RBAC (PQE and IPQA must not issue credentials).
- Production deployment documentation/configuration, CI, tests and any existing TLS/proxy service templates. Confirm what is present versus what IT still must supply.

Do not claim it is enough to add a `CredentialStatus=Trusted` enum. Assess data shape for a **versioned credential enrollment request** and **credential/certificate record**, including state transitions, hashes, expiry, nonce consumption, duplicate concurrent operations and audit actor provenance. Propose, but do not implement, exact schema updates and indexes/checks/Restrict FKs.

## 4. Stage A design decisions for Codex to recommend (do not ask Peter to choose implementation minutiae)

Provide a **single recommended, smallest safe architecture** with alternatives only where materially relevant:

- Enrollment sequence (PAD starts → challenge/CSR/proof → Admin verifies/approves → signer issues certificate → PAD installs certificate → mTLS confirms identity → activated/eligible identity). Clearly identify who initiates each step and how the server protects each transition.
- Whether proof is verified before or after Admin approval and how the same public key/challenge is cryptographically bound across steps.
- Separation of an unauthenticated-but-highly-constrained bootstrap HTTPS surface, Admin Web surface and authenticated Native mTLS surface. Propose route/host/port ownership, rate limits and trusted proxy/Kestrel configuration.
- PKI ownership: IT-managed CA and signer interface, validation policy, key vault/OS store/secret injection, safe dev test signer **only in test/dev**, certificate renewal and error recovery; no committed prod keys.
- Effective trusted device status and credential rows, replacing P1-02 `PendingCredential` while preserving `Revoked`/`Replaced` terminal states, safe `BindingVersion` increment and stale credential invalidation.
- The exact policy gate for a verified device identity that can be tested in P1-03 even though Task execution and NativeSession are deferred; define what `canExecuteLineShared` should mean at this phase, and prevent premature `true` semantics.
- Failure/cancellation recovery for Admin-approved-but-issue-failed or issue-succeeded-but-response-lost cases; prevent repeated signing of divergent identities.
- Optional deployment split: P1-03A (server enrollment, Admin review, PKI adapter and test transport) and P1-03B (actual mTLS proxy/Kestrel integration) **only if** a single PR would be unsafe/oversized. Make a recommendation rather than forcing one PR.

Prefer standard algorithms/certificate semantics and battle-tested framework capabilities. No custom crypto protocol where standard CSR/signatures and TLS suffice.

## 5. Required acceptance criteria to propose

Design concrete test matrices, distinguishing automated PASS from environment-dependent SKIP:

A. **Bootstrap:** challenge/token expiry, one-use, high-entropy/hash storage, replay, parallel attempts, wrong device/line/version, invalid nonce and wrong-key signatures, CSR subject/key mismatch, malformed input bounds and rate limiting.

B. **Admin:** RBAC for Admin/PQE/IPQA/anonymous/forced-password-change/revoked-account; explicit reviewed approval, conflict/expiry/revoke/replace races, duplicate clicks and append-only provenance.

C. **Credentials/PKI:** CA chain/clientAuth EKU, validity windows, key linkage, issuer failures, retry/receipt behavior, cert rotation/renewal, old-cert rejection after current-credential change; issuance key protected outside Git.

D. **TLS boundary:** no cert / untrusted cert / wrong cert / forged headers / direct backend bypass, proxy certificate stripping/forwarding, Kestrel trusted TLS alternative, port/host segregation, TLS session reuse after revocation.

E. **Authorization:** every request revalidates DeviceId/LineId/BindingVersion/credential/status; stale/rebound/revoked/replaced devices denied, even with a valid personal JWT. Personal JWT without mTLS and mTLS without valid user must not enable future LineShared Task writes. Preserve existing legacy Web/API responses.

F. **Transactions:** concurrent Admin approve/rebind/revoke/replace and enrollment finalization; no duplicate credentials or stale trust, one complete successful state/audit outcome, provider-specific locking; SQLite automated tests plus **live disposable PostgreSQL** concurrency/migration tests when available.

G. **Migration:** fresh schema and populated P1-02 upgrades; preserve older Plan/Task/Completed facts; both SQLite/PostgreSQL model-diff checks, provider-specific SQL review; no destructive rollback expectation.

H. **Real PAD/IT:** early SM-T500 Android 11 Keystore/keypair + client cert install + actual mTLS connection; TLS termination and trusted proxy configuration; certificate backup/rotation/secret custody. Declare genuinely unavailable hardware/proxy/PostgreSQL tests as **SKIPPED**, not PASS.

I. **Quality:** .NET Release restore/build/tests and Web lint/test/typecheck/build if touched, GitHub CI, `git diff --check`, no secret/material leaks, no unrelated legacy changes.

## 6. Stage A deliverable format

Return a **read-only Stage A report** with these sections:

1. Verified branch/HEAD, clean/dirty tree, PR #10 merge and CI evidence, read documents and relevant inspected files.
2. AS-IS/TO-BE gap table identifying P1-02 facts versus missing P1-03 trust features.
3. Threat model and recommended bootstrap → Admin approval → certificate → mTLS sequence, including sequence diagram or clear text walkthrough.
4. Listener/topology proposal (Web, bootstrap HTTPS, Native mTLS; same backend/server), proxy trust and bypass defenses.
5. Device state machine, enrollment/credential entity design, safe concurrency/BindingVersion semantics and audit behavior.
6. API and Admin UX proposal (route, method, input/output, role, availability, rate limit), avoiding product-policy guesswork.
7. File-level implementation inventory and estimated PR size; propose single PR or bounded A/B split with reason. Include a practical plan to verify Android/IT feasibility early without prematurely implementing Android business features.
8. Acceptance-criteria checklist, negative/security tests, migration/legacy compatibility and exact PASS/FAIL/SKIP verification plan.
9. Open **engineering dependencies** and risks (CA ownership, network/proxy, real PAD, live PostgreSQL). Do not reopen approved Rev3.1/F2 choices or create an unnecessary product-decision questionnaire.
10. Explicit recommended next step and label **AWAITING P1-03 IMPLEMENTATION SCOPE REVIEW**.

**Read-only hard stop:** No branch, code edits, migrations, new files, commits, pushes, GitHub PR, production config modification or deployment. No hidden synthetic demo keys in the tracked tree. Do not implement until separate P1-03 Stage B approval, but do not ask the user to reconfirm Stage A itself.

## 7. Official reference links for use during engineering review

- Microsoft ASP.NET Core 10 certificate authentication: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/certauth?view=aspnetcore-10.0
- Microsoft Kestrel TLS client cert endpoints: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0
- Microsoft Kestrel security/reverse proxy guidance: https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/security-considerations
- Android Keystore system: https://developer.android.com/privacy-and-security/keystore
- Android KeyGenParameterSpec.Builder: https://developer.android.com/reference/android/security/keystore/KeyGenParameterSpec.Builder
