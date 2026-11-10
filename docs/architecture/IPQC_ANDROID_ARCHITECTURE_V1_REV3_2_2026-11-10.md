# JAX Power IPQC Android Architecture v1 Rev3.2 — APPROVED

Date: 2026-11-10, America/New_York.

Status: **APPROVED ARCHITECTURE TARGET — IMPLEMENTATION AND ACCEPTANCE TESTING PENDING.** Peter formally approved Rev3.2 on **2026-11-10**. Personal login → server-backed Line Selection → Patrol Tasks replaces mandatory physical-device authentication and fixed PAD-to-Line binding. Architecture approval is not evidence of implemented capabilities or permission to code, migrate, commit, push, merge or deploy.

Final sign-off, Peter, 2026-11-10: approval includes dual-tablet offline conflict preservation, the three server-acceptance categories after source deactivation, independent shared-work protection after logout, and all 47 proposed acceptance cases. Peter explicitly accepts that a server-issued WorkContext cannot independently prove who physically performed each offline edit (§7.4). Do not reopen these architecture decisions. The next authorized task is the R32-01 read-only implementation-readiness audit; implementation still requires separate approval.

Verified documentation baseline: local `main`, cached `origin/main` and live GitHub `main` at `d24eed7adc6f7302bacd02182ce5a7417fe15b1e` on 2026-11-10. P1-01, P1-02 and P1-03A / PR #11 are merged. No P1-03B implementation exists. Existing approved Rev3.1 and the original Implementation Handoff remain unchanged historical documents. This approved document is a complete target design, not merely a device-authentication delta and not a description of current code.

Companion: [Approved Rev3.2 implementation-impact and handoff supersession addendum](IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md). The supersession matrix in §18 identifies exact historical sections. Rev3.1 inspection rules, F2 business/concurrency decisions and F3 remain binding except for the explicitly replaced identity dependencies.

Implementation reference: §5 personal/offline access; §7 F1 scope and its explicitly accepted attribution limitation; §9 unchanged F2 guarantees with revised fields; §14 retirement compatibility; §§16–17 small PRs and acceptance gates. The retained inspection/evidence/retention contracts are restated in §§10–13 as the complete approved target baseline.

## 1. Executive recommendation and approved direction

Android V1 uses **personal IPQC authentication over ordinary server HTTPS, followed by explicit Line Selection**. Every appropriately authorized active IPQA may work on either configured production line. The server authorizes the person and the actual persisted task; it does not prove which tablet is being used.

The operator flow is Login → Select Line 1 or Line 2 → Patrol Tasks → inspection / Save Draft / explicit online Submit. Switch Line changes the visible task filter. ProductionLine IDs come from the server; labels, selected-line preference and installation-local identifiers are not credentials. A task never moves lines because an operator switches the screen filter.

Do not implement the previous P1-03B-Transport or P1-03B-Activation proposals. V1 requires no physical PAD registration, device keypair/CSR, device certificates, device CA integration, mTLS, separate Bootstrap/Native TLS listeners, device Current activation, BindingVersion authorization or device-bound OfflineGrant. Ordinary Android Keystore protection of refresh tokens and local encryption keys remains appropriate. There is no replacement physical-device trust service, attestation project or device security epoch.

Retain the ordinary 72-hour offline policy, shared drafts, F1 truthful attribution, all signed-off F2 handoff invariants, NG/reinspection/PQE behavior, photos/reference images, F3 and five-year automatic retention. Their device-dependent fields are replaced as specified here; business outcomes are not redesigned.

The smallest next implementation PR after sign-off should **retire active device-enrollment/provisioning surfaces while retaining data and safe cleanup**, without enabling LineShared execution. Personal-session, line-query and contribution contracts follow in separate scoped PRs (§16).

## 2. Existing baseline and old-versus-new decisions

AS-IS evidence: Web/API v1 has personal local accounts, JWTs, rotating refresh-token rows, legacy Plans/Tasks/reviews/reports, stable ProductionLines and workflow version guards. P1-02 adds provisioning metadata/reservations/audit. P1-03A adds candidate enrollment and recoverable dev/test issuance; candidates are non-authorizing. The Android folder has an enrollment-probe README, not the patrol application. Shared drafts, user contribution contexts, the revised task states, attachments and offline Android behavior still need implementation.

Existing `RefreshTokenSession.Id` identifies a rotating token row, not a stable personal session. Existing JWTs contain user/auth-version claims, not the proposed stable personal-session claim. Existing legacy endpoints remain legacy-only. A source-code scaffold or architecture approval is not activation evidence.

| Topic | Rev3.1 / prior P1-03B direction | Rev3.2 target |
| --- | --- | --- |
| Execution identity | Person plus registered PAD and device binding | Current authorized personal IPQA session |
| Line access | PAD fixed to one line | Both configured lines available to every authorized active IPQA |
| Line selection | Derived from authenticated PAD | Explicit workflow preference / task-query filter |
| Task identity | Stable TaskId, WorkflowVersion, LineId | Retained; server persisted fields remain authoritative |
| Device enrollment | Keystore keypair, CSR, proof, Admin approval | Removed as a patrol prerequisite; old surfaces retired |
| Transport | Separate Bootstrap HTTPS and Native mTLS | Ordinary server HTTPS through the existing IT-supported arrangement |
| Certificate lifecycle | Issuance, confirmation, Current, renewal/revocation | No new device-certificate lifecycle for patrol |
| Personal secure storage | Keystore-protected token ciphertext | Retained without device identity enrollment |
| Offline access | Device/line/binding-bound OfflineGrant | Protected local receipt of personal online verification, max 72 hours |
| Contribution provenance | Device-bound contributor context | User/session/task-scoped server-issued WorkContext |
| Local shared workspace | RegisteredDevice + line + task | Environment + local installation workspace + actual line + task |
| Submit actor binding | User/session plus device/binding | Original UserId, personal-session provenance and task; current personal auth checked separately |
| F2 handoff and CAS | Frozen intent, query-only old A intent, one base-state winner | Retained in full |
| F3 and evidence retention | Ready / reference locks, durable deletion, automatic five years | Retained in full |
| Already merged schema | Device/enrollment tables and migrations | Preserve; no destructive rollback or automatic identity conversion |
| Hardware | Earlier assumption: two SM-T500s | One SM-X210/API 35 and one SM-T500/API 30; one minSdk 30 APK |

## 3. Login, Line Selection and navigation

### 3.1 Online start

1. Open the English-only IPQC Android app. Home provides Patrol and a non-executable Material Verification placeholder.
2. A new login always verifies the personal username/password online. Existing lockout, active-account, role, forced-password-change and password rules apply.
3. After successful eligible login, show the simple Line Selection dialog. The server returns the actual IDs and names of the two configured V1 ProductionLines, displayed as Line 1 and Line 2. No APK GUID constants, DHCP mappings, device-model mappings or legacy text-to-ID inference.
4. Select a line and request its tasks. The task screen identifies the selected line and offers Switch Line.
5. Switch Line changes the display/query preference. Each draft, operation, photo and Submit intent retains its own TaskId and persisted task line. Existing work is not moved, cleared or relabeled.

The server directory is the source of truth for the configured V1 lines. Unexpected/missing/inactive configuration is a visible configuration error, not an invitation to fabricate a line. An IPQA does not obtain PQE/Admin permissions by selecting a line. New user-selected LineShared execution requires the dedicated IPQA permission; existing Web roles and legacy ownership policies remain independent.

Task tabs remain Pending Tasks, Re-inspection Tasks and Completed Tasks. Android Completed includes submitted pending decisions as well as final outcomes (§10); this does not redefine the Web final-history module. Use landscape two-column layouts where practical and fully functional portrait layouts. Save Draft and Submit stay accessible; status is not conveyed only by color.

### 3.2 Line switching and cached tasks

| Situation | Required behavior |
| --- | --- |
| Online, choose either configured line | Query by its actual LineId, refresh relevant task/draft versions and obtain access/context material for downloaded tasks |
| Offline, same active user with an unexpired local session | Allow selecting either cached line; expose only tasks previously downloaded or online-validated for that user and supported workflow |
| Offline, selected line has no eligible cached tasks | Show an empty/offline explanation; do not invent tasks, silently fall back to the other line or claim a download succeeded |
| Offline, only another user's task-access record exists | Do not treat shared bytes or a line preference as authorization; require online validation for the current user |
| Offline session expired / explicitly ended / clock state invalid | Lock access/editing; preserve all work; require online reauthentication |
| Unsynchronized work exists on the previous line | Preserve its original task/line; show pending work; changing the filter neither acknowledges nor discards it |
| A task has a Submit barrier | Keep that task's barrier/intention intact even when another line/task is displayed |
| Leaving a Preparing Submit screen | Do not continue into an automatic formal POST; resume preparation/status recovery and require explicit online user continuation |

The selected line is not saved as a server authorization scope. A valid task write derives its line from the stored Task; any supplied expected line is a consistency assertion that must match. The server does not reject an otherwise authorized task solely because a client currently displays another line. Background processing uses each queued task's identity, not the current screen filter.

## 4. Components, two-tablet compatibility and HTTPS

| Component | Target |
| --- | --- |
| Android | One Kotlin app module; Compose Material 3, stable compatible Navigation 3, ViewModel/StateFlow, Repository, Room and Coroutines; DataStore for non-sensitive preferences; fixed stable dependency versions |
| Networking | Retrofit/OkHttp over normal server HTTPS; personal JWT and refresh only; exact decimal serialization / BigDecimal |
| Local security | App-private data, Keystore-protected refresh/context-secret ciphertext; no stored password, no device enrollment keypair |
| Background work | WorkManager for eligible draft/photo synchronization and receipt queries; never formal Submit |
| Images | CameraX, system album picker with Android 11 fallback, persistent byte copying; Coil/viewer support |
| Server | Existing ASP.NET Core/EF domain services, server-side authorization, scheduling and validation |
| Persistence | Dedicated PostgreSQL in production; SQLite for appropriate development/automated tests; real PostgreSQL concurrency gates |
| Web / storage | Existing bilingual Vue application and same-origin API; separate expandable local photo volume via storage adapter |

```text
Android (personal login → line selection → tasks) ── ordinary HTTPS ──┐
Web (same-origin /api, existing roles) ───────────────────────────────┤
                                                                  ▼
                                                existing ASP.NET Core backend
                                                ├─ current personal-user/session checks
                                                ├─ task/line/version/state validation
                                                ├─ shared draft + contributor context
                                                ├─ exact Submit/review + evidence rules
                                                ├─ PostgreSQL
                                                └─ independent photo data volume
```

| Site reference | Compatibility target | Deployment concern |
| --- | --- | --- |
| Line 1: Galaxy Tab A9+, SM-X210 | Android 15/API 35, One UI 7.0; build AP3A.240905.015.A2.X210XXS8DYG1 | Reported patch 2025-07-01; verify actual app/runtime and IT management |
| Line 2: Galaxy Tab A7, SM-T500 | Android 11/API 30, One UI Core 3.1; build RP1A.200720.012.T500XXU3BVF1 | Reported patch 2022-04-01 requires IT security/update assessment |

Keep minSdk 30. OS versions need not match. The same APK/source must pass on both; compile/target SDK and stable versions are pinned when the actual app project is approved. Keystore protection of local secrets is ordinary platform secure storage, not an assertion of physical PAD identity. No StrongBox/attestation/anti-root gate is added.

Both tablets use JaxPower-Floor Wi-Fi with DHCP. Previously reported Line 1 172.22.15.211 / Line 2 172.22.15.210 addresses are diagnostics, not stable identity or line configuration; they were not reverified during this documentation task. Optional DHCP reservations are IT network management only. Exact server FQDN/port and app TLS trust are PENDING IT confirmation; use configurable placeholders, never an invented server or a tablet IP as the server endpoint.

Use the existing IT-supported HTTPS/reverse proxy. Keep hostname/chain validation, trusted forwarded-header restrictions and Web same-origin cookies. Do not add device-certificate headers, a second Native mTLS port, client private keys or a production device CA. Internal connectivity does not require public Internet access; test the intended HTTPS server even when Wi-Fi says Connected without internet. App trust of an internal server CA must be explicitly verified on both OS versions; do not use trust-all or disabled hostname verification.

## 5. Personal authentication and ordinary offline access

### 5.1 Current online authority

Reuse existing local-account login/refresh/change-password and server permission derivation. Every new LineShared business request requires a valid personal access JWT, current active eligible user, current auth version, no forced password change, and the new personal-session validity check. Role/account changes and resets keep existing revocation semantics. Refresh is single-flight in the app; network failure is not account invalidation; 401/403 are not infinitely retried.

Recommended minimal session extension: add a stable **PersonalSessionId** to the existing refresh-token family and new JWT/session responses. Carry it unchanged across refresh rotation; the existing token-row Id continues to rotate. A new login creates a new family. For new LineShared APIs, verify that the family belongs to the authenticated user and has a current unrevoked refresh row, matching auth version and unexpired absolute lifetime. Rotation/revocation must be atomic so an intermediate state is not mistaken for permanent logout. Do not create a separate device session service or bind this family to an installation, model, IP or selected line.

Existing Web/LegacyPersonalV1 contracts and already-issued legacy JWT behavior remain unchanged during rollout. New LineShared calls with an older JWT lacking the required personal-session context require normal online reauthentication; they are not silently upgraded. The existing `ClientType=Native` controls token delivery, not device trust.

Access tokens remain in memory. Refresh-token ciphertext is stored through ordinary Android secure storage and excluded from backup; never store the password. On logout, stop local authorization and the old user's workers immediately, remove their reusable login material and revoke the personal family online or retain a pending logout for reconnect. Preserve drafts, photos, source-context material needed for reconciliation and historical intents. A new user login/switch is online only; local cached user names are not an offline account chooser.

Use separate protection for the personal credential vault and shared work storage. The shared work encryption key must not be derived from A's password, access/refresh token or personal session, or depend on retaining A's credential-vault key. Deleting A's reusable credentials at logout must leave shared drafts, photos, operation manifests and required WorkContext proofs decryptable by the controlled app coordinator after B's valid online login. B never receives or reuses A's access/refresh token. A pending logout record in shared storage contains only non-secret family identification and reconciliation status, never a reusable A token; server-side family closure must use an appropriately authorized path, not impersonate A through B's synchronization. Shared-data synchronization does not depend on retaining A's login credentials.

### 5.2 Minimal 72-hour replacement

After successful online verification, return a small **offline access receipt** with UserId, PersonalSessionId, EnvironmentId, VerifiedAtUtc, OfflineAccessUntilUtc and the personal family's absolute expiry. OfflineAccessUntilUtc is no later than VerifiedAtUtc + 72 hours or the family expiry. This is protected local app policy state received over authenticated HTTPS, **not a bearer credential accepted by the server**, not a device grant and not a new asymmetric signing infrastructure.

Store the receipt with app-private integrity-protected/encrypted local state. Resume only the same last active, not-explicitly-ended user's eligible session after ordinary process restart/reboot. Do not let someone choose a different cached user offline. Cache task-access markers under UserId + EnvironmentId + TaskId + authoritative LineId + WorkflowVersion; shared work remains separately stored (§8).

Within one boot use the saved server-time anchor and elapsedRealtime. After reboot use the saved verification/expiry anchors, wall UTC and last observed progress. Obvious rollback/corruption requires online verification. Restart, opening a page, Save, line switching, failed refresh and unsuccessful network attempts never reset the deadline. A successful fresh server verification may refresh the receipt within the same absolute personal-session lifetime. This is ordinary business timeout handling, not a trusted-clock or anti-root guarantee.

Expired access locks viewing/editing, not storage. Preserve all unacknowledged work. A currently authorized person can reconcile eligible shared work after an online login. Offline sign-out prevents silent offline resume of that ended session. New login, user switching, unknown task acquisition and every formal Submit require online authentication.

### 5.3 Reconnect

Before any upload, obtain current personal authorization, refresh server task/draft/attempt state and validate the source manifest. Recheck the uploader, newly accepted source contributors/contexts, actual task line/workflow, state and versions. Already accepted facts follow §7.2 rather than a retrospective source-eligibility check. A disabled/revoked uploader stops synchronization and preserves work. Invalid unaccepted source dependencies are quarantined before their photo bytes or business writes are accepted. A source context or offline receipt cannot restore a revoked uploader's authority.

The server cannot instantly revoke a disconnected local session. That accepted ordinary-offline limitation remains. On reconnect there is no grace write based on cached authorization.

## 6. Server task identity, authorization and generation

All appropriately authorized active IPQAs can work on either configured active V1 line. New version-aware APIs check personal authority plus the **persisted TaskId, Task.LineId, WorkflowVersion, frozen definition and current state**. They do not query RegisteredDevices or device BindingVersion. A line label, selected preference, installation UUID, session Id alone or a legacy provisioning row cannot grant access.

Keep LegacyPersonalV1 = 1 with its historical owner semantics and existing guards. Reuse the existing LineSharedV1 = 2 foundation for the newly implemented LineShared business contract; no implemented device-authenticated execution exists to migrate. Do not activate every existing row merely by enabling a version number: implementation audits must inventory any pre-existing LineShared rows, validate their snapshots/state/schema and report incompatible rows without converting historical results. Never rewrite LegacyPersonalV1 into LineShared automatically.

The new task-list API requires an actual LineId filter before counting/pagination. Task detail/write resolves TaskId on the server. Where an expectedTaskLineId is supplied, reject mismatch without moving the task. Authorization covers either line for IPQA; this consistency check is not a physical-device restriction. PQE/Admin review permissions remain separate and are not obtained through Line Selection.

New supported Plans use stable LineId and LineShared mode without a fictitious personal assignee. The server generates 07:00/19:00 tasks per line in America/New_York, freezing Day/Night, line, policy/configuration and Standard snapshot. Keep occurrence uniqueness, DST rules, catch-up, no future pre-generation and overlap protection for active same-line/round plans. Editing a Plan never rewrites generated Task identity. Approximately 28 items is an estimate, not a limit. Do not generate tasks on Android.

## 7. F1: user/session-based contributor provenance

### 7.1 Minimal WorkContext contract

During an eligible user's online task download/validation, the server issues a task-scoped **WorkContext**. Reuse an applicable context or issue a fresh capture context; do not issue another user's context on behalf of an uploader. Minimal persisted facts:

| Fact | Purpose |
| --- | --- |
| WorkContextId; proof-secret hash | Server record and high-entropy scoped provenance capability |
| EnvironmentId, UserId, IssuedPersonalSessionId | Who was authenticated when it was issued; environment boundary |
| TaskId, immutable LineId, WorkflowVersion | Exact task scope; no arbitrary line reassignment |
| IssuedAtUtc, CaptureUntilUtc | At most the applicable offline receipt's capture period |
| SecurityRevokedAtUtc and reason | Explicit source security invalidation; sticky across later reactivation |

Use a random 256-bit proof secret, hashed on the server, delivered only to that authenticated user over HTTPS and protected locally. An opaque Id or contributor UserId alone is insufficient. This capability only supports provenance reconciliation with a separately authorized current uploader; it cannot log in, extend offline editing, authorize another task or perform Submit. It is not a device certificate, physical identity, general access JWT or new login credential.

Each operation retains stable OperationId, TaskId/ItemId/InspectionAttemptId, SourceWorkContextId, original ContributorUserId assertion, expected line/workflow, dependencies, LocalEditVersion and canonical content hash. The batch carries the required context proof for unaccepted operations in a redacted transport field; accepted references resolve through server records (§7.2). Photo manifests use the same source/dependency relationship. Server resolution always takes ContributorUserId from the validated WorkContext; a conflicting assertion is rejected. Do not put secret proof material in URLs, audit logs, reports or the business canonical hash.

The app stops using A's capture context for new edits when B becomes active. B gets B's context and new B operations. Existing A operations retain A's exact source context and identifiers. A valid current B may forward already queued A context proofs for historical reconciliation. B cannot request a fresh A context, choose arbitrary A attribution or have the server substitute B for missing A proof.

### 7.2 Capture permission and historical provenance are different

CaptureUntilUtc limits normal app editing under that context. Context expiry and ordinary personal logout/session expiry do not erase its provenance record or automatically make earlier queued work unacceptable. When A remains an eligible IPQA and the context has not been security-revoked, current authorized B may upload that historical work; UploadedBy is B, contributor remains A, and formal SubmittedBy is whoever explicitly submits.

Account deactivation, loss of IPQA execution eligibility, Admin security password reset and explicit source-context security revocation must revoke affected capture contexts together with the relevant account/security action. Reactivating an account does not revive security-revoked contexts. Regular refresh rotation, session timeout, ordinary logout or a normal self-service password change do not, by themselves, rewrite or security-revoke historical contribution provenance. Existing AuthVersion still governs **current** authentication; its value at capture may be retained for audit but must not be used as a blanket historical-source expiry test.

Online mutation must coordinate current-user/source checks with security mutations and Task version acceptance using an audited provider-specific transaction protocol. A current uploader and a historical source are checked for their different purposes. Preserve accepted immutable facts if a source is disabled later.

The boundary is **server acceptance**, not whether a client received the acknowledgement:

| Data category when A is deactivated or A's source context is security-revoked | Required behavior |
| --- | --- |
| Local operations/evidence not yet accepted by the server | No new acceptance under the revoked source. Quarantine dependent unaccepted work, preserve originals and bytes, and require B to re-perform affected work using B's own context and new operation IDs. Never relabel A's work as B's. |
| Valid server draft operations/results accepted before the security action | Keep the accepted values, A's contributor identity, evidence and acceptance/audit facts. A currently authorized B may carry unchanged, still-applicable accepted results into **B's own new explicit Submit**. A's later inactivity or revoked context alone does not invalidate those facts or require A's login/proof again. Validate current task eligibility, versions, inspection attempts, required samples/evidence and F3 references normally. B's changes, new work and required re-inspection use B's context; old accepted results cannot satisfy a new inspection-attempt requirement. |
| Formally submitted immutable records | Preserve original contributors, actual submitter, accepted content and audit history. Account/source revocation is not a quality rejection, rollback or history rewrite; normal PQE decisions and re-inspection rules remain unchanged. |

A local unacknowledged/OutcomeUnknown operation may already belong to the second category. With B's current task-read authority, reconcile its stable OperationId and authoritative receipt/server state before treating it as unaccepted. One NotFound does not prove an in-flight write cannot later commit. Resolve races through the same acceptance/security/version transaction ordering; preserve uncertain data until resolved. Referencing accepted results must validate their server-side identities, task/attempt scope and content hashes, not trust a client claim that an operation was accepted. Revocation prevents new acceptance under that source; it does not retrospectively poison valid accepted facts.

### 7.3 A/B synchronization and rejection

| Condition at upload | Server action | Recovery |
| --- | --- | --- |
| A's ordinary session/context capture period expired or A logged out normally; A still eligible; valid source proof | B's current auth may transport the historical operations; resolve A/B separately | Continue shared work; new edits use B's own context |
| A disabled, lost execution eligibility or source context security-revoked | Reject/quarantine unaccepted operations and their unverified dependencies | Preserve original records/files; B re-inspects/re-photographs affected items under new B operations |
| A's valid server draft results were accepted before A was disabled/source-revoked | Keep accepted facts and A provenance; B may include unchanged, still-applicable results in B's own new explicit Submit | Resolve accepted references on the server; apply normal current task/attempt/version/evidence rules, without requiring A's current login or proof |
| A's results already belong to an immutable formal submission | Preserve that submission and its actual actors | Continue normal PQE/history rules; no revocation-driven rewrite |
| Claimed contributor differs from the context owner; unknown/wrong proof | ProvenanceConflict; never relabel as B or accept the claimed name | Preserve and quarantine; controlled re-performance rather than guessed authorship |
| Task/line/workflow/attempt or dependency mismatch | No application to a different task/line; return precise scope/version conflict | Keep both versions; refresh and explicitly reconcile |
| B currently unauthorized / expired / forced to change password | No transfer or mutation | Stop that uploader's queue, retain work and authenticate online |

Submit the source/attachment manifest for validation before uploading unaccepted evidence bytes. Conflict batches are all-or-nothing. Return item/dependency conflict detail, preserve the original batch/receipt, and prevent quarantined A data being smuggled into B's full-draft replacement. Unaffected work may be explicitly resynchronized as a new batch against current versions. Already accepted A operations/history remain A; no retrospective rewrite follows later revocation.

Rejecting a new mixed batch does not remove prior accepted server draft facts. Reconcile unknown receipts, reload that accepted draft, then explicitly construct a new batch from valid accepted references and eligible new operations, excluding quarantined unaccepted A dependencies. Reapplying changed payloads uses new operation IDs and preserves the original conflict records.

Do not automatically expire/delete WorkContext facts or proof material needed by unsynchronized work simply because the 72-hour capture window ended. Retain durable source references for accepted history. Lost/corrupt local proof is a recoverable provenance conflict, not permission to guess authorship or discard local bytes.

### 7.4 Explicit offline-attribution limitation

The server proves that it issued a scoped context while A was authenticated and that the uploader possessed its proof. It does **not** prove that A physically typed each offline edit, that a particular tablet captured it, or the exact offline capture time. B using the approved app forwards stored A records; a malicious/compromised client or someone who steals A's scoped capability could fabricate content within that scope. Hashes detect conflicting identities/content and retries; they do not turn a bearer capability into per-operation authorship proof.

The trusted-app coordinator, app-private encrypted storage, server scope/role/revocation checks and truthful contributor/uploader distinction are the proportional V1 controls. Peter explicitly accepted this limitation at final sign-off on 2026-11-10; it is not a promise of non-repudiation. A later demand for independently provable offline authorship is a new requirement; do not quietly restore device PKI, user signing keys, attestation or a hardware clock.

## 8. Shared local workspace and durable synchronization

Recommended organizational key: **EnvironmentId + InstallationWorkspaceId + LineId + TaskId**. InstallationWorkspaceId is a locally generated organizational UUID for Room/outbox isolation, not a registered device, server credential, access condition or proof of origin. Never use it to override Task.LineId or to deny an authorized IPQA access from another installation.

Store shared task data independently of personal login state. Room holds frozen snapshots, draft/sample/PIC/reference state, local operations, accepted receipts and outbox. App-private persistent files hold image bytes. Per-user offline/task-access receipts and personal token storage are separate. A first user's UserId must not permanently own the shared business workspace.

Shared records and necessary WorkContext proofs use the independent work-storage protection defined in §5.1. A's logout removes A's reusable login material without deleting that key or making A's queued work unreadable. After B logs in online and passes current task authorization, only the controlled coordinator reads and forwards eligible saved A proofs; they are not exposed as selectable identities, raw UI values or general-purpose export/API data. Every network request uses B's current credentials. B cannot inspect, obtain or reuse A's refresh/access token, and neither token belongs in the shared outbox. This separation supports shared reconciliation, not independent proof of offline authorship (§7.4).

Save success means the local transaction completed. Photos use temporary file → completed atomic rename → metadata transaction with startup reconciliation. UI must not claim a save for an external URI whose bytes were not copied. Keep unsynchronized records, context material, images and sealed intents through process death, reboot, session expiry, logout, line switch, sync errors and signed in-place APK upgrades. Do not destroy the work-storage encryption key at logout. Uninstall, factory reset or lost local keys are not claimed lossless recovery mechanisms.

Use one task coordinator shared by foreground and WorkManager. LocalEditVersion is local change/acknowledgement progress; DraftVersion is the server mutable write version; CurrentRevisionNo is the formal submission version. A response acknowledging local version 20 never acknowledges edit 21. Serialize per-task replay and apply expected-version/idempotency checks. Reload/reapply conflicts visibly with both sides retained; no silent last-write-wins or generic field-merge platform.

**Two-tablet draft conflict:** each tablet sees only its cached/server-acknowledged state and its own local changes while offline; it cannot see another tablet's unsynchronized changes. If both start Task T at DraftVersion v, each may retain different pending edits. The first eligible synchronization conditionally commits v → v+1. A second write still expecting v receives a version conflict (409), without modifying the accepted draft. Preserve the conflicting tablet's original pending batch, files, source proofs and operation identities alongside the refreshed server snapshot; the other tablet retains its own work. No claim is made that the server holds a copy of a tablet's never-uploaded bytes. The user explicitly reloads/reapplies eligible changes against the current version. Changed/rebased payloads use new OperationIds, retain original provenance/conflict records, and never overwrite either side silently or reuse an ID with different content. New or modified work by B uses B's context. The local coordinator serializes one installation only; server conditional writes resolve cross-installation races, including simultaneous reconnects.

On a new person's online login, stop old workers from using the old person's credentials. After fresh authorization and source reconciliation, the current uploader can process eligible shared queues from either line, using each operation's persisted Task/Line scope. A UI line switch alone does not erase, reassign or authenticate any queue.

## 9. F2: fixed Submit intents and signed-off A/B handoff

### 9.1 Frozen states and canonical fields

Retain Preparing → Sealed → InFlight → Accepted / Conflict / OutcomeUnknown. These are local request states, not Task business states. Exactly one locally sendable intent per Task is permitted across users and both line views in the installation.

| Old device-dependent fact | Rev3.2 treatment |
| --- | --- |
| DeviceId / RegisteredDevice | Remove from authorization and required canonical payload |
| BindingVersion | Remove; do not rename DraftVersion into a device credential |
| Device-bound NativeSessionId | Replace with frozen OriginPersonalSessionId and SubmitterUserId; current transport auth is separate |
| Device-bound contributor grants | Use validated SourceWorkContextIds and resolved contributor facts |
| Selected line preference | Never include as authority; seal actual Task.LineId as a consistency fact |
| Installation workspace Id | Optional local diagnostics only; not required server authorization or actor binding |

The new versioned canonical contract covers SubmitterUserId, OriginPersonalSessionId, SubmitterWorkContextId, TaskId, actual LineId, WorkflowVersion, BaseRevisionNo, ExpectedDraftVersion, frozen header/dispositions/samples/PIC/provenance and exact evidence manifest. Canonicalization specifies ordering, exact decimal text and null/empty rules, and the server recomputes CanonicalContentHash. It excludes current JWTs, refresh tokens, context proof secrets and the current transport session. Do not reinterpret any existing hash under a new canonicalization version.

### 9.2 Foreground preparation and server acceptance

1. An online explicit IPQA Submit persists SubmissionAttemptId, the original personal actor/session/context, BaseRevisionNo and LocalEditVersion; freezes input and photo-file manifest; enters Preparing and establishes the durable task barrier.
2. Wait for prior in-flight operations to resolve, synchronize only through the frozen local version, validate source manifests, and obtain matching AttachmentId/Ready plus persisted draft associations. Do not yet send formal Submit or substitute later server/local content. Preserve conflicts.
3. Seal ExpectedDraftVersion and all frozen business/provenance/evidence fields, including item/attempt/ClientPhotoId/AttachmentId/hash, CanonicalizationVersion and CanonicalContentHash. Sealed is immutable; never rebuild from latest draft on retry.
4. Only the original UserId with valid current online authorization can explicitly send that intent. Network loss, leaving the foreground flow, logout, process death and reboot stop automatic formal continuation. Workers may synchronize permitted preparation and query receipts, not POST Submit.
5. Sealed/InFlight/OutcomeUnknown keeps that task read-only under its coordinator. Late Ready responses update transfer state only; they cannot add evidence to the sealed manifest. Do not cancel/replace an already transmitted unknown request as though it were never in flight.

Server uniqueness remains (TaskId, SubmissionAttemptId). Authorize the current requester first. An existing matching actor/hash attempt returns its original authoritative receipt before trying to accept a new revision. A different content/actor binding conflicts. For first acceptance, check actual task scope, supported workflow/state, expected draft/base versions, current item attempts, sources, sample rules and Ready evidence; atomically create immutable submission, evidence references, receipt, truthful actors and new state. All commit or roll back together.

Source validation distinguishes new unaccepted operations from server-accepted draft facts (§7.2). Apply current source eligibility/revocation checks to newly accepted work; resolve accepted references and their hashes/task/attempt scope against authoritative server records. B may submit unchanged, still-applicable A results accepted before A's deactivation, preserving contributor A and actual submitter B, through B's own new explicit intent. A's current login or old source proof is not a prerequisite for those accepted references. This never permits B to POST A's sealed intent, impersonate A, reuse old results for a required new inspection attempt, or bypass current versions, business/evidence/F3 rules. A rejected new batch or Submit leaves previously accepted draft facts and immutable history intact.

Every accepted Submit advances CurrentRevisionNo and the relevant write version. Review/other changes to editing eligibility also advance write versions. At most one request accepts a given base state. An NG transition back to editable work cannot make a delayed retry create a new NG revision. Receipt includes exact SubmissionId/RevisionNo, AcceptedAtUtc, canonical hash, SubmittedByUserId and outcome. Until it exists, no Submitted/Approved success message.

### 9.3 Same-user reauthentication

A can authenticate online again as the same UserId using a new valid personal session and explicitly retry the exact locally persisted Sealed intent, provided it is still sendable for A and current authorization/source checks allow a new acceptance. OriginPersonalSessionId remains A's historical originating session; it is not replaced by the new session Id. Resolve it through the server-issued submitter context for A/task. Normal origin-session expiry/logout does not require a dead token to be reused. Current transport session/auth version is separately checked and audited; it is excluded from the frozen business hash.

If the attempt already committed, authorized receipt recovery returns that original result. If current versions conflict, preserve the intent and return conflict; do not silently reseal. If the intent became query-only during takeover, **A logging in again does not make it sendable again**. Missing local frozen payload/files cannot be reconstructed by pretending the latest draft is the old intent; query the receipt and preserve/reconcile the original installation's work.

### 9.4 Mandatory A/B handoff — all six original invariants retained

1. Before B takes over a task, query A's authoritative SubmissionAttemptId receipt and refresh Task/Draft/Revision state. One NotFound does not establish that A's prior request cannot commit.
2. On explicit takeover, persist A's old intent as **query-only**. That installation's coordinator must not resend it, even if A later returns. B explicitly clicks Submit and creates a **new B-bound intent**; no rename or impersonation.
3. At most one locally sendable intent per Task. An A OutcomeUnknown record may coexist with B's current intent only as non-sendable query-only recovery state. Persist the transition atomically in Room and recover it after reboot.
4. Server BaseRevisionNo/ExpectedDraftVersion/conditional checks allow at most one accepted submission from that base, including an A request already in flight when B takes over. The loser reconciles actual outcome; no fake next NG revision.
5. Marking query-only does not cancel network traffic. Handle a late A commit by version checks and receipt reconciliation, keeping both actors truthful.
6. These are the signed-off F2 business/concurrency decisions. Removing physical identity does not relax them.

B may query A's receipt under current task-read authorization, but cannot POST A's Submit using B's credentials. B's own explicit submission may contain already accepted or valid reconciled contributions from A while recording B as actual submitter. Multiple installations may independently create requests; server version/uniqueness controls remain the final cross-installation guard.

## 10. Inspection forms, states and PQE — retained business contract

Task Basic Information retains optional bounded text Customer, Production and Product MO. Production's structured meaning/rename remains V2. New UI does not expose Machine Code/Series/Mold; historical values remain. Qualitative samples use OK/NG. Ordinary quantitative samples have optional repeatable Barcode and required actual Inspection Value; server judges against the frozen numeric operators/limits. New LineShared tasks do not use N/A; historical LegacyPersonalV1 N/A remains unchanged. Support manual entry and keyboard-wedge scanners, not camera barcode scanning.

Initial measured inspection requires frozen SampleCount (missing defaults to 1), maximum 50. Each targeted reinspection is a new attempt with a new sample set and minimum max(frozen minimum, prior applicable formally measured count, inherited requirement), maximum 50. Repeated actual measured values are legal; copied old samples are not new inspection. Non-targeted accepted OK results retain their provenance. Finish each target's permitted treatment before formal resubmission; incomplete work remains draft. Another NG may be formally submitted without waiting for PQE.

NG Type, ordinary NG Reason and Remarks are optional. UnableToMeasureNG requires NG Reason and new current-attempt photo; it creates no fake zero/sample. Removed is per item after Production/EQ confirms absence/non-applicability, optional reason, retained actor/definition, no bulk remove. Neither disposition lowers an established reinspection sample minimum. Warn when removing the last normal item, but allow it and a formal NotPerformed submission. A corrupt task with no frozen items is not the valid all-Removed case.

PIC is an optional per-NG multiselect from an Admin-maintained independent roster with stable Id/name/department/active state, not login accounts. Freeze historical names/departments; no email/Teams notifications.

| Event | Backend state | Android grouping / PQE behavior |
| --- | --- | --- |
| Generated / first draft | PendingInspection / InProgress | Pending |
| Formal Submit with NG | ReinspectionRequired | Re-inspection; submitted NG/photos/history immediately readable by PQE |
| New reinspection Submit still NG | ReinspectionRequired, new revision | Re-inspection; no fake PQE reject event |
| Applicable normal items all pass | PendingApproval | Android Completed, Pending Approval |
| PQE approves exact revision | Completed | Approved, final history |
| PQE rejects with reason and targets | Rejected | Re-inspection, Rejected by PQE |
| All items Removed, formal Submit | PendingNotPerformedConfirmation | Android Completed, Pending PQE Not-Performed Confirmation |
| PQE special confirmation | NotPerformedConfirmed | Distinct final Not Performed outcome |
| PQE rejects special confirmation with targets | Rejected | Restore/reinspect targets |

Draft NG→OK does not replace a formal Submit or move the task out of reinspection. Partial Removed plus all remaining normal items passing uses ordinary PendingApproval, with removal details visible. Ordinary approval cannot confirm all-Removed; terminal records cannot be edited.

Approve/Reject/special confirmation/batch bind exact SubmissionId or expected revision and stable review operation Id. Reject requires a reason and a nonempty valid target set, which may include prior OK/Removed. Targets enter new attempts; unselected valid OK results may carry forward. No independent header-only reject workflow is introduced. Task headers may be corrected during permitted rework and frozen in the next submission.

Every formal revision freezes complete definitions/results/dispositions, header, samples, PIC, evidence, contributor provenance, actual submitter and server Submit time. Final Inspector is the corresponding actual final submitter; business inspection time is formal server Submit time. Review time is separate. No new trusted per-sample measurement timestamp is invented.

## 11. Photos and reference images — retained

CameraX and album selection operate inside the current item/attempt. Copy actual bytes to app-private persistent storage; an unavailable cloud URI is a failure, not a saved photo. Keep the original until Ready and durable draft association are confirmed. Record what compressed bytes were actually accepted; do not claim the server stores an original never uploaded.

Initial configurable technical limits remain up to 5 photos/item/attempt, 5 MiB/upload; starting compression long edge 1920 px, JPEG quality 85, target roughly 2 MiB. QA verifies actual defect/label readability before enabling compression; tune or disable it if necessary. Server limits are authoritative; these are not hard-coded business promises.

Use stable ClientPhotoId and globally unique operation identifiers within the installation workspace; server replay scope also includes actual Task/Item/Attempt/source context. Same Id/different bytes conflicts. The local workspace Id is not authorization. Server checks bytes/type/size/hash and persists files outside the DB plus metadata before Ready. Manifest/source authorization precedes unaccepted photo bytes. Upload/recovery does not create a formal submission.

NG requires a new current-attempt photo. OK follows frozen Required/Optional, except NG→OK requires a new improvement photo; repeated NG also requires new evidence. PQE-targeted prior OK/Removed is reprocessed using those same rules; being targeted does not make every Optional OK item photo-mandatory. Removed alone does not mandate a photo. Old AttachmentId or identical old submitted image bytes cannot satisfy a new-evidence requirement for the same item. Keep Camera/Album source; EXIF is not trusted proof of capture time. No AI authenticity system is added.

Preserve legacy free-text PhotoRequirement; only explicitly confirmed new Standard/Plan configuration uses Required/Optional. Unknown text cannot silently become Optional.

PQE uploads versioned reference images. Task snapshots freeze version/hash; Android caches bytes and supports zoom/offline readiness. Keep any reference still used by Standard/Task/submission history. Reference and inspection evidence have separate purpose/access/lifecycle; five-year evidence cleanup does not delete protected reference versions.

## 12. Storage, five-year policy and F3 — retained

Use one IPQC server with a separate expandable photo volume initially planned at 1 TB. Store protected files outside wwwroot; DB holds metadata/storage keys. Authenticated HTTPS is the only client file interface; no MES FTP, SMB or public raw paths. The approximate 307 GB five-year baseline (two lines, four tasks/day, 28 photos/task, 1.5 MB/photo) excludes reinspection growth, reference, OS/DB and backups; monitor and expand. Disk-full failures preserve the local pending copy.

Each formal evidence reference has SubmittedAtUtc and RetainUntilUtc = SubmittedAtUtc.AddYears(5), UTC. Leap-day maps to February 28 in the fifth year. Hourly server checks plus startup/catch-up automatically remove eligible bytes; no per-batch approval, hold UI or exact-second deletion promise. Retain the original one-time business-policy confirmation and IT-readiness gate before production retention is enabled; it is not approval for each deletion. Later references protect the same object until their later retention date. Valid draft/reference dependencies also protect it. Do not delete inspection results, original metadata, submission/review facts or deletion audit.

One AttachmentId maps to one StorageKey/object; no cross-Attachment physical deduplication in V1. Ready requires actual bytes durably stored, size/SHA-256 verified, metadata committed and availability Available. File operations are not rolled back by a DB transaction; recover intermediate states explicitly.

### 12.1 F3 transaction and recovery contract

1. Every add/replace/remove reference and cleanup claim uses the same per-attachment write protocol. PostgreSQL locks attachment rows in deterministic Id order, with consistent Task/attachment order; other providers use a proved equivalent. No cached zero-reference count authorizes deletion.
2. Under those locks, reference writers recheck Ready/Available, task/item/attempt access and versions, then persist references and per-Submit deadlines with revision/receipt in one transaction.
3. Cleanup rechecks all formal deadlines and live draft/reference dependencies. Only if unprotected does CAS Available→DeletePending persist a stable DeleteOperationId, reason RetentionExpired and intent time, then commit.
4. DeletePending/DeleteFailed objects reject new references. The race yields either a successful protective reference or a deletion claim with a recoverable AttachmentUnavailable conflict; never a new dangling revision.
5. Physical deletion runs after commit under retryable leases/CAS. Use the same DeleteOperationId across failures and process/worker restarts. Keep truthful intent/attempt/confirmation times, not a fabricated exact physical deletion time.

| Interruption/result | Required recovery |
| --- | --- |
| Before deletion-intent commit | Roll back; do not delete bytes |
| Intent committed, bytes still present | Continue the same non-referenceable operation |
| Bytes absent after matching durable intent, result not committed | Verify object/intent; idempotently confirm Deleted |
| Delete failure / bytes possibly present | Record DeleteFailed; retry same intent, keep references blocked |
| Deleted already committed | Return original result; preserve metadata/hash/history |
| Missing bytes or wrong hash without matching deletion intent | MissingUnexpected/Corrupt and alert; never fabricate RetentionExpired |

All viewers, HTML and XLSX distinguish policy-deleted evidence from integrity failure. Reference lifetime and the later retained submission reference are not shortened by deleting an earlier reference. Failed retries do not extend the original formal retention date.

### 12.2 Other lifecycle and restore

Unsynchronized local edits/photos have no automatic TTL. Server staging with no valid activity for over 24 hours and Ready objects with no draft/submission/reference association for over 7 days may be cleaned safely; the client retains unconfirmed originals and can re-upload. Protect effective unfinished drafts. Only terminal, completely synchronized local task caches are eligible for the existing 14-day cache cleanup; server formal history remains.

IT supplies coordinated PostgreSQL/files backup outside the server's failure domain. An additional directory on the same host is not that backup. Online five-year deletion does not rewrite every existing backup set; backups expire under IT rotation policy. Result/meta archive policy is separate.

During restore keep relevant business/sync/image ingress closed; restore DB/files, verify object/hash/deletion records, replay available independent tombstones and recompute deadlines/dependencies at current UTC before exposing images. Recover pending/failed deletes and isolate expired bytes even if an older backup says Available. F3 recovery remains automatic policy, not a new approval workflow.

Reconcile personal-account and source-context security revocations before reopening writes. If historical context eligibility cannot be established, invalidate affected restored capture contexts and quarantine unaccepted dependencies while preserving bytes and accepted history; require fresh online personal verification. Reconcile Task versions and known accepted Submit receipts after rollback; an uncertain prior acceptance must not be silently resubmitted against a restored old base. Keep affected tasks under technical recovery control until resolved. Do not add the cancelled device-security-epoch/PKI recovery design.

## 13. Historical compatibility and reporting — retained

LegacyPersonalV1 retains original owners, N/A, Approved-Unqualified results, Machine/Series/Mold, review semantics and timestamps. Do not infer old LineId or rewrite historical records. Existing old endpoints remain guarded and cannot bypass new LineShared rules. New version-aware readers must exist before enabling the new workflow; PQE's immediate access to submitted NG revisions/photos remains required.

Every new submission includes each frozen item with a meaningful result/disposition, including Removed; do not omit items to evade integrity validation. Normal Completed requires the exact Approved review; NotPerformedConfirmed requires its special decision on an all-Removed revision. Both appear in unified final history with distinct outcomes/filter. PendingApproval and PendingNotPerformedConfirmation do not become final Web history merely because Android groups them under Completed.

Inspector is the actual final formal submitter. Show server Submit and approval/confirmation times separately. Preserve contributors, uploaders, prior NG, removal/restoration, PIC, barcodes, evidence and reference versions. HTML/modal preview and XLSX remain; XLSX includes results/samples/PIC/history and photo count/status/authorized links, not bulk full-size images/base64. No PDF, ZIP, new dashboard or legacy GQMS history import is introduced.

## 14. Safe retirement of merged P1-02 / P1-03A

Keep all applied EF migrations and device/enrollment/credential/audit rows. Do not run destructive Down, drop tables, reset BindingVersion, fabricate Retired/Trusted/Current device states or convert a candidate into personal inspection authority. P1-01 ProductionLine and frozen Task identities remain useful.

| Existing surface | Proposed compatibility treatment after an approved retirement PR |
| --- | --- |
| ProductionLines and code uniqueness | Keep Admin management, stable IDs and installation-wide canonical code uniqueness; provide a separately authorized IPQA directory projection |
| RegisteredDevices / reservation fields | Preserve metadata, original relationships and audit; no new patrol authorization dependency |
| Device Registry UI | Admin-only historical/read-only device view; clearly identify its retired patrol role; no PAD prerequisite in Android |
| Enrollment/candidate UI | Read-only history; disable invitation, proof-driven provisioning and approval actions; retain controlled cancel/revoke for closure where needed |
| Bootstrap challenge/proof/receipt endpoints | Disabled/retired for all normal V1 deployments, including accidental development configuration; predictable unavailable response, no authentication fallback |
| New issuance | Disabled; no new certificate signing or Current activation |
| Unresolved old issuance/revocation | Optional explicitly configured cleanup-only drain with original operation IDs; no new Issue calls, no false acknowledgement |
| Provisioning audit | Append-only, existing actor types/identities preserved; do not rewrite facts because architecture changed |
| Candidate DTO non-authority | Preserve its non-authorizing meaning; a new personal task capability is never sourced from this DTO |

Cleanup-only handling must first persist cancellation/revocation intent atomically with truthful audit, then recover/revoke through the configured old signer outside DB transactions. A definitive not-issued result may be closed/cancelled with a durable signer outcome; an unknown or late result remains tracked and non-authorizing. Preserve unresolved ExternalRevocationPending across every closure. Record completion only after actual signer acknowledgement and successful audit/state commit. If signer access is unavailable, leave pending facts visible and disabled; do not clear flags or claim cleanup succeeded. Keep required old key/journal custody until IT reconciles outstanding operations. No new PKI integration is required for the patrol app.

Old nullable unique Device.LineId constraints may stay because they constrain only the retired module. New line/task authorization never interprets a reservation as exclusivity for people. The existing Admin line-deactivation guard currently rejects reservations: preserve that legacy behavior in the first retirement PR and report it explicitly. A later line-lifecycle PR may replace the guard with reviewed active Plan/Task rules or an explicit audited release of retired reservations; never silently clear historical links. Pending compatibility cleanup must not make device binding a prerequisite for personal task execution.

Existing Legacy APIs and audit reads remain available under their current roles. Feature retirement is an intentional change to unused provisioning mutation availability, not a redefinition of historical JSON or results. No automatic deletion of old enrollment challenges/history is bundled into retirement. Any later physical cleanup requires its own scoped review.

## 15. Threat model and accepted tradeoffs

| Threat / limitation | V1 control and truthful boundary |
| --- | --- |
| Caller chooses another line / uses another tablet | Authorized IPQA access to either line is intentional; validate actual task identity, not physical location |
| Caller submits a label, IP, model or fake DeviceId | These grant no rights; server resolves persisted task/line and current personal permissions |
| Stolen personal token | HTTPS, protected storage, short access lifetime, refresh rotation/current-family checks, auth-version/account revocation; no claim of device binding |
| Another person uses an unattended active personal session | Shared-tablet users must use their own login and normal logout/screen-lock discipline; offline resume does not prove continuing physical user presence |
| User tries to claim arbitrary contributor | Require server-issued scoped context/proof and derive owner server-side; reject scope/owner mismatch |
| Shared-app/compromised-client theft of A's proof | Scoped bearer provenance can be abused within its scope; no physical/per-edit authorship proof; stated acceptance limitation (§7.4) |
| Offline account revocation | Cannot be immediate; ordinary max-72-hour access locally, mandatory fail-closed online revalidation |
| Clock rollback / app state tampering | Ordinary anomaly detection and local protection; not a trusted-clock/anti-root guarantee |
| Two users/tablets edit or Submit together | Expected versions, deterministic transactions, stable operation/attempt IDs, immutable receipts and F2 handoff |
| Network response lost | Durable local state and authoritative receipt lookup; no speculative success, automatic Submit or destructive reset |
| Photo/reference races or backup rollback | Full F3 locking, durable intentions and restore reconciliation before exposure |
| Server/certificate not configured | No guessed host, HTTP credential fallback or trust-all; IT validates normal server TLS on both tablets |

Audit records server-observed facts separately from client-reported capture times and installation diagnostics. Logs redact passwords, bearer/refresh tokens and WorkContext proofs. No production secrets, private keys, signing keys or raw employee/customer fixtures enter Git.

## 16. Revised phases and smallest next PR

These are sequencing buckets, not permission to implement them as giant PRs. Each gets a fresh exact-file audit, acceptance agreement and implementation approval. Do not resume P1-03B under a renamed task.

| Sequence | Small scoped objective | Deliberately deferred |
| --- | --- | --- |
| R32-01 | Disable active device provisioning/enrollment and new signing; preserve Admin history, existing closure/drain safety and ProductionLine management | No new LineShared execution, session schema, Android app, dropped tables or migration rewrite |
| R32-02 | Personal-session family/receipt contract and personal-authorized server line/task read foundation | No shared write, Submit, photos or device trust |
| R32-03 | Explicit new LineShared Plan/scheduler configuration, version-aware task projections and frozen definitions | Do not enable inspection until its relevant validation/write gates pass |
| R32-04 | WorkContext/F1, draft versions, operations, source manifest, quarantine and security-revocation integration | No formal Submit side effect from sync |
| R32-05 | Inspection samples/dispositions/header/PIC and exact state/review contracts, split by actual diff | Preserve old personal workflow |
| R32-06 | Attachments/reference/Ready/F3 and explicit retention-reference contracts | No premature Ready, no public files |
| R32-07 | Frozen/idempotent Submit, F2 A/B takeover and exact PQE decisions; coordinate evidence availability | No auto-Submit |
| R32-08 | Version-aware PQE/history/HTML/XLSX projections and policy-deletion display | No dashboard/PDF/ZIP |
| Android increments, parallel once contracts stable | One app: personal login + line selection, cache/ordinary offline, shared forms/photos/sync, explicit Submit/recovery | No device enrollment, separate helper apps or full app in one PR |
| Release increments | Retention worker, restore drills, both-tablet UAT, signed in-place upgrade and operating documentation | No automatic production deployment |

**Recommended immediate PR: R32-01, non-authorizing retirement guard.** Expected implementation touchpoints are Program/options, DeviceEnrollmentEndpoints, DeviceRegistryEndpoints mutation gating, issuance/lifecycle cleanup-only behavior, the provisioning/enrollment Web views and EN/ZH strings, focused security/rollback tests and historical documentation notices. No database migration should be needed for this first PR. Preserve existing Admin GET/audit responses and cancellation/revocation semantics needed for old obligations. Explicitly test that disabled feature options cannot be bypassed in Development/Testing, while ordinary login, ProductionLine management and legacy workflows remain usable. Do not combine the session/WorkContext design or Android scaffold with this retirement change.

R32-02 should add only the minimal personal-family fields/indexes and additive contracts after a separate audit of refresh/logout/reset concurrency. New WorkContext and Task write/receipt/evidence schema belongs to its relevant later PR. Migrations preserve all existing rows and both providers' model normalization; no broad rollback. Source security mutations, Task writes and file-reference transactions must be tested together where they intersect.

## 17. Proposed acceptance criteria and evidence matrix

All **47 independently numbered cases** in this section are **proposed / NOT RUN for Rev3.2**. The original 43 IDs are retained; SYNC-01, F1-06, F1-07 and SEC-02 address the conditional review additions. Historical main CI evidence (187 backend / 72 Web tests) does not prove these contracts. Future reports use PASS/FAIL/SKIP accurately; unresolved infrastructure/hardware inputs are PENDING external gates.

| ID | Scenario | Required acceptance |
| --- | --- | --- |
| AUTH-01 | Fresh/first login offline; inactive/wrong-role/forced-change user | Deny inappropriate access; only online eligible personal login creates the new session/context |
| AUTH-02 | Native string, fake DeviceId, model, IP, workspace UUID or line label | No authentication/permission elevation |
| AUTH-03 | Refresh rotation, logout, account security change and concurrent write | Stable personal family; atomic rotation/revocation; new requests cannot use revoked online authority |
| LINE-01 | One IPQA selects each actual server line | Both task lists accessible without any registry/certificate record |
| LINE-02 | Query pagination/filter; write a TaskId with mismatched expected line | Correct filtering before totals; reject mismatch; no Task.LineId mutation |
| LINE-03 | Switch with pending drafts/photos or an uncertain Submit | Other-line work preserved, scoped and not auto-submitted; original task barrier remains |
| OFF-01 | Same-user app kill/reboot at <72h, 72h and >72h | Remaining eligible time preserved; expired access locks without deletion |
| OFF-02 | Failed requests, refresh loss, line switching, normal Save, rollback/corrupt clock | No false deadline extension; anomaly requires online verification |
| OFF-03 | Offline cached tasks for both lines / no cache / another user's cache | Only current user's eligible cached tasks; no invented download or offline user change |
| OFF-04 | Offline logout then restart / different user login | Ended session not silently resumed; new user requires online auth; shared work retained |
| OFF-05 | Account revocation detected before upload | Stop affected uploader; no cached-authorization grace write; preserve work |
| SYNC-01 | Both tablets edit the same cached Task at version v while offline, then reconnect sequentially and concurrently | No visibility of peer unsynchronized edits; first valid write accepts v+1, stale write gets 409 without overwrite; retain original local work/files/proofs and accepted server snapshot; explicit reapply uses new IDs for changed payloads and preserves provenance |
| F1-01 | A capture window/session expires or normal logout; valid B upload | Contributor A, uploader B, actual final submitter; no device checks or actor relabeling |
| F1-02 | A disabled/lost eligibility/security-revoked context | Quarantine unaccepted dependent operations/photos; retain originals; B redoes with new IDs |
| F1-03 | Forged owner, missing proof, wrong task/line/context; arbitrary installation IDs | Scope/owner proof checked; no cross-task application or inferred author; no physical identity claim |
| F1-04 | Account reactivated; normal password change versus security reset | Revoked contexts stay revoked; ordinary session lifecycle does not retroactively invalidate accepted provenance |
| F1-05 | Context capture expiry followed by valid historical upload | No new ordinary offline editing, but no automatic destruction/rejection of eligible prior queued work |
| F1-06 | A's valid draft is server-accepted, then A is disabled/source-revoked before B submits | B's own new explicit Submit may use unchanged eligible accepted results without A login/proof; contributor A and submitter B retained; normal current versions/attempt/evidence rules hold; formal history remains immutable |
| F1-07 | Mix accepted A draft facts, revoked unaccepted A work and a lost/in-flight acknowledgement | Reconcile stable operation receipts before classification; quarantine unaccepted dependencies, no relabeling or rollback of prior accepted draft; rejected batch is atomic; retry/reapply preserves originals and cannot spoof an accepted reference |
| F2-01 | Save/WorkManager versus Submit and stale acknowledgements | Task barrier and frozen LocalEditVersion; later input not overwritten/confirmed |
| F2-02 | New/late photos during Preparing or after Sealed | Exact frozen manifest/hash; late Ready cannot mutate it |
| F2-03 | Stale versions or same attempt/different content/submitter | Conflict, no altered intent or incorrect revision |
| F2-04 | Commit response lost; process kill/reboot | Unique receipt recovery; explicit retry only; no fake Submitted or automatic POST |
| F2-05 | A in flight, B takeover, A NotFound then late response | A becomes query-only locally; B new explicit intent; one base-state winner; truthful actors |
| F2-06 | Old NG retry versus genuine new NG reinspection | Old receipt returned; new attempt/new work required for another revision |
| F2-07 | A reauthenticates into a new personal session | Exact sendable sealed payload/hash/origin unchanged, current auth separate |
| F2-08 | A returns after B takeover; two installations compete | Query-only A intent not re-enabled; server CAS remains final cross-installation guard |
| FORM-01 | Sample minimum/default/1/50/51; frozen comparisons | Server enforces minimum, cap and exact numeric rules |
| FORM-02 | Prior measured five → unable/removed → measured again | Applicable five-sample requirement retained; no fake samples |
| FORM-03 | Last Removed / all-Removed / zero-item corrupt snapshot | Warning and valid special outcome; no Qualified shortcut; corrupt snapshot rejected |
| PQE-01 | NG, all-OK, all-Removed; stale single/batch decisions | Correct distinct routes, immediate submitted-NG visibility, exact review/targets and atomic batch |
| PHOTO-01 | NG/NG→OK/PQE-targeted/optional OK and old identical evidence | Required new evidence enforced with retained exceptions; no EXIF authenticity claim |
| PHOTO-02 | Album unreadable, disk full, process death, upload response loss | No false local save/Ready; unique local copy preserved |
| F3-01 | PostgreSQL reference/Submit versus deletion | Same lock protocol yields protection or reference rejection, never dangling acceptance |
| F3-02 | Later submission plus live draft/reference | Latest valid reference/dependency protects bytes |
| F3-03 | Crash at each DB/file step; multiple workers | Same DeleteOperationId, recoverable state and truthful attempt/result audit |
| F3-04 | Missing/hash-corrupt bytes without intent | Integrity fault, not fabricated RetentionExpired |
| F3-05 | Restore older DB/files/tombstones | Recompute/reconcile/isolate before exposure; old Available does not resurrect expired bytes |
| RET-01 | UTC fifth year / leap day / hourly catch-up / retries | Fixed automatic policy; no approval/hold workflow or false deletion success |
| COMP-01 | Existing personal owners/N/A/Approved-Unqualified/report | Unchanged semantics and responses; new endpoints do not weaken legacy guards |
| COMP-02 | Disabled device module plus pre-existing reservations/candidates; proposed retirement API matrix | Preserve Admin line management and existing guards, history reads and controlled cancel/revoke; deny retired provisioning/approval/Bootstrap/new Issue even with old enable settings; pending cleanup never falsely cleared; ordinary login/legacy flows unchanged |
| COMP-03 | Fresh/populated SQLite and PostgreSQL upgrades | Original facts, FKs and audit preserved; both EF models/provider SQL; no destructive Down |
| PAD-01 | Same APK on SM-X210/API 35 and SM-T500/API 30 | Login/line switch/cache/restart/weak network/forms/photos/scanner/50 samples verified independently |
| PAD-02 | Signed in-place upgrade, logout and expiry | Room/photos/outbox/context/intents retained; no uninstall-as-upgrade recovery |
| NET-01 | Actual internal HTTPS with no public Internet; invalid hostname/chain | Intended server works where reachable; bad TLS fails, no invented trust result |
| SEC-01 | Captured A proof available to a malicious client | Report the scoped-bearer attribution limit; do not label it per-edit or physical proof |
| SEC-02 | A logout removes A's personal credential material/key, B logs in online, then app restarts | Independent work key still decrypts shared drafts/photos/necessary A proof; controlled sync uses only authorized B credentials, no A token in shared outbox/UI; invalid B auth stops transfer without deleting work |

Implementation gates: backend Release restore/build/tests; Web strict/lint/test/build when touched; Android unit/lint/build/instrumented and signed upgrade evidence; fresh/populated migration tests, live disposable PostgreSQL concurrency, both EF pending-model checks, SQL review, Git whitespace and secret checks, GitHub CI. Real-device and real-PostgreSQL gates are not replaced by simulators/SQLite. IT supplies internal server TLS/DNS/firewall/time, PostgreSQL/photo storage/backup and tablet/security inputs; production device CA and mTLS inputs are removed from the dependency list.

## 18. Supersession map, review boundary and references

### 18.1 Rev3.1 section disposition after Rev3.2 sign-off

| Historical Rev3.1 section | Disposition / replacement |
| --- | --- |
| §1 device-authentication decision; §2 two-identical-tablet assumption | Replace only those facts with §§1–4 here; ordinary offline/retention decisions retained |
| §3 device mTLS networking / device credentials | Replace with ordinary HTTPS/personal secure storage; other stack choices retained (§4) |
| §4.1 device registration | Superseded for V1; legacy compatibility only (§14) |
| §4.2 device-bound NativeSession | Replace device dependencies with personal-session family (§5); account/token rules retained |
| §4.3 device/line/BindingVersion OfflineGrant | Replace with ordinary personal offline receipt (§5); 72h/reboot/no-data-loss policy retained |
| §5 registered-PAD execution gate | Replace with current personal authority and persisted Task scope (§6); scheduling retained |
| §§6–7 | Retain business forms/samples/dispositions/state/PQE rules (§10) |
| §8 workspace and device-dependent parts of §8.1 F1 | Replace identity/provenance mechanics (§§7–8); truthful attribution/quarantine retained |
| §9 / §9.1 | Retain F2 semantics; replace only physical-binding fields and authorization mechanics (§9) |
| §10.1 upload authority / device-workspace naming | Replace only identity/scope terms (§§7–8,11); photo/reference business rules retained |
| §§10.2–10.3, 11.1–11.5 | Retain photo/reference/retention/F3 rules (§§11–12); ordinary personal authorization replaces device gate |
| §12 | Retain versioned history/report requirements (§13) |
| §§13–14 | Remove mandatory PKI work; replace sequencing with §16 |
| §15 device-only tests; §15.1 F1-03 device preconditions | Replace with personal/scope/context tests (§17); retain other business outcomes and all F2/F3 guarantees |
| §16 IT/PAD requirements | Update hardware and remove device PKI dependency; retain ordinary HTTPS/storage/backup/security/UAT inputs |
| §§17–18 historical review/reference framing | Historical; use this approved document's scope/references for Rev3.2 |

Original Handoff §4.2's six F2 handoff invariants are retained explicitly in §9.4. Its §4.1 device diagram, §4.3 device-bound provenance and §7 device-PKI dependencies are superseded only as detailed in the companion addendum. Do not edit either signed-off historical artifact to make the change look previously approved.

### 18.2 Decisions and pending inputs

Peter's final approval on 2026-11-10 resolves the architecture direction: both lines are intentionally available to eligible IPQAs, physical-device proof is removed, and F2/F3/inspection rules are retained. The offline capability/trusted-app limitation in §7.4 is explicitly accepted; independently provable per-edit offline authorship is outside the simplified V1 promise. No repeat architecture review is required.

Engineering/operational inputs remain: actual server URL/normal TLS trust, two configured ProductionLine IDs, disposable PostgreSQL, storage/backup ownership, tablet patch assessment and QA content/readability/UAT. These are not requests to select crypto libraries or restore the cancelled PKI architecture. Do not invent endpoint/CA/trust values or block architecture review on their availability.

### 18.3 Sources and stop condition

- Peter's 2026-11-10 Architecture Direction Change request: binding new direction; supersedes the previous P1-03B Stage A recommendation.
- Peter's 2026-11-10 final sign-off and R32-01 implementation-readiness audit authorization: approves this architecture and authorizes documentation finalization plus read-only application-code audit, not implementation.
- [Approved historical Rev3.1](IPQC_ANDROID_ARCHITECTURE_V1_REV3_1_2026-11-06.md) and [original Handoff, especially §4.2](IPQC_ANDROID_ARCHITECTURE_V1_IMPLEMENTATION_HANDOFF_2026-11-06.md): retained business/F2/F3 baseline.
- Actual repository at d24eed7: AGENTS/CLAUDE/README; account/refresh/provisioning/enrollment services, workflow models, Web views and migration/test evidence. Personal-family/WorkContext/offline/LineShared write contracts above are approved target additions, not observed code.
- [Android Keystore](https://developer.android.com/privacy-and-security/keystore): ordinary local-secret protection remains supported; it does not imply device enrollment.
- [Android network security configuration](https://developer.android.com/privacy-and-security/security-config): normal app-specific HTTPS trust and cleartext restrictions.

**Execution boundary:** finalize the approved documents and complete the R32-01 read-only implementation-readiness report and next implementation prompt, then stop for separate R32-01 scope/implementation approval. Do not modify application code or migrations, create implementation branches, commit, push, open PRs, merge or deploy under this authorization. Historical Rev3.1 and the original Handoff remain unchanged; P1-03B stays cancelled. All 47 acceptance cases remain proposed / NOT RUN until separately implemented and verified.
