# JAX Power IPQC — Android Architecture v1 Implementation Handoff

**Date:** 2026-11-06 (America/New_York)  
**Purpose:** Handoff from the completed Android Patrol architecture/discovery thread to the next thread for *scoped implementation planning and first PR discussion*.  
**Status:** **Architecture v1 APPROVED by Peter on 2026-11-06**, based on `IPQC_ANDROID_ARCHITECTURE_V1_REV3_1_2026-11-06.md`, with the explicit F2 handoff clarification in §4.2 below. **Program-level permission to begin development has been given.** No particular PR scope, branch, code diff, database migration, deployment, or test result is approved by this handoff alone.  
**Audience:** Peter (PM / architecture sign-off), ChatGPT (architect / reviewer), Codex (staff implementation engineer), QA and IT (validation / dependencies).  
**Repository:** https://github.com/peterl-ai/IPQC-system  
**Existing local checkout:** `C:\Users\kuohao.lai\Desktop\project\IPQC\IPQC-system`  
**Target device:** Samsung Galaxy Tab A7 SM-T500, Android 11; **one registered PAD per production line, two lines**.  
**Language:** Chinese for discussion; Android UX in **English only**; Web English + Simplified Chinese.

> ## New-thread START HERE
>
> 1. **Business decisions and architecture are signed off.** Do not reopen approved choices or ask Peter to choose implementation libraries, clock algorithms, error codes, etc. Escalate only a concrete contradiction / implementation blocker with impact and one recommendation.
> 2. **Use Rev3.1 as the authoritative architecture** and apply the F2 A/B handoff contract in §4.2 as a binding sign-off addendum. This handoff is a portable summary, **not a replacement for the full Rev3.1**.
> 3. **Next action: discuss the first small, reviewable PR (P1-01), its exact files, backward-compatibility plan, and acceptance gates before writing code.** Do *not* ask Codex to implement all P1–P10 in one batch.
> 4. **Verify actual Git state** (`git status`, branch, HEAD, `origin/main`, migrations, tests) before planning changes. Last *documented* main is `86f4d817ac68dfafebaaef341f7f2a5bbaedb73b`; this is **not** a live November 6 Git verification.
> 5. The current repository is **Web/API Phase 1A–1H**, not yet the approved Android workflow. Old README/docs describe legacy personal assignment, N/A and Submit→Approval behavior. **Do not implement those old behaviors for new `LineShared` tasks**; preserve them for historical/legacy workflow versions.
> 6. Implementation approval does **not** mean production deployment approval. PostgreSQL smoke/concurrency, TLS/mTLS, photo volume/backup, real PAD and QA UAT remain separate gates.

---

## 1. Source of truth, document lineage and sign-off

### 1.1 Architecture sign-off chain

| Stage | Artifact / outcome | Authority |
| --- | --- | --- |
| Existing Web/API v1 | `IPQC_Handoff_Phase1H_WebV1_Milestone_2026-10-13.md`, `README.md`, `AGENTS.md`, `docs/01–12` | Existing implementation baseline; verify against current repo |
| On-floor workflow discovery | `ANDROID_PATROL_WALKTHROUGH_DELTA_v0.1_2026-11-05.md`, workflow gap reviews, `IPQC_PROJECT_HANDOFF_2026-11-05.md` | Historical discovery / rationale |
| Decision record | Rev2, D10–D22 and previously confirmed decisions | Superseded where Rev3/Rev3.1 explicitly changes policy |
| Complete target architecture | **`IPQC_ANDROID_ARCHITECTURE_V1_REV3_1_2026-11-06.md`** | **Authoritative approved TO-BE technical/business design** |
| Independent review | `IPQC_ANDROID_ARCHITECTURE_V1_REV3_1_GPT_FINAL_REVIEW_2026-11-06.md` | F1–F3 essentially closed; minor F2 clarification |
| Final Codex/Peter resolution | Explicit F2 handoff rule from the immediately preceding conversation | **Signed-off addendum; incorporated in this file §4.2** |
| Final PM authorization | Peter: “我这里也通过，没有问题，可以开始开发了” (2026-11-06) | **Architecture sign-off + permission to move into development planning** |

### 1.2 Conflict resolution

1. Verified **code** wins as evidence of current AS-IS behavior; it does not override approved new TO-BE requirements.
2. **Peter's latest approved decisions + Rev3.1 + F2 addendum** win for new Android/LineShared workflow.
3. Existing `AGENTS.md`, `CLAUDE.md`, `README.md`, `docs/01–12` remain binding for unaffected legacy modules, coding standards, RBAC and historical compatibility.
4. Where an old document says “every Submit goes to PendingApproval,” “N/A required,” “personal assignee owns Task,” or “D19 manual QA/IT deletion approval,” this is **not the new workflow**. Use `WorkflowVersion` to preserve legacy records, not ad hoc reinterpretation.
5. Rev3.1 **replaces** the former manual photo-retention approval/hold plan: server automatically cleans expired evidence. No V1 hold/approval workflow.
6. Do not claim any new system capability is implemented merely because Rev3.1 specifies it.

---

## 2. Actual starting point: Web/API v1

- Project: JAX Power IPQC, replacement for the Line Patrol portion of GQMS for new JAX Power operations.
- Existing modules: Standards, Plans + server scheduler, Tasks/Draft/Submit/Review, Completed Patrol Records + HTML preview + XLSX, local User Management / RBAC, bilingual Web.
- **Stack:** Vue 3 + TypeScript + Vite + Ant Design Vue + vue-i18n; ASP.NET Core 10 + EF Core; PostgreSQL production target, SQLite dev/tests; local JWT + rotating refresh sessions.
- **Last documented main:** `86f4d817ac68dfafebaaef341f7f2a5bbaedb73b` (PR #8, Phase 1H). October evidence: 67 backend and 44 frontend tests passed, builds/CI green. **Numbers are historical, not a promise of current HEAD or latest test run.**
- Implemented scheduler supports `Daily` selected times and idempotent occurrence creation, but legacy Plan/Task ownership is `AssignedInspectorUserId` (personal IPQA).
- Implemented flow uses current N/A semantics, every Submit→PendingApproval and older PQE review; the Android/NG-first design is **not implemented**.
- No complete Android client, no production photo upload/storage lifecycle or Reference Image viewer. Do not infer presence from draft data fields.
- No verified **live production PostgreSQL** migration/API/concurrent transaction or backup/restore smoke test at the milestone. Hosting/capacity/TLS remain to be delivered with IT.

### Repository rules that remain in force

- Read `AGENTS.md`, `README.md` and relevant `docs/` before changing code; scope each PR narrowly.
- Keep Web same-origin `/api`; new Android native TLS entry can use a separate hostname/port on the **same server**.
- Web bilingual EN/ZH; Android EN-only, Android 11 compatible.
- Server validates all authorization, ranges and submissions. Stable IDs rather than display names. Changes to business rules require tests; no unrelated refactors; no hardcoded credentials/production data.
- No MES DB as the IPQC database. No Material Verification implementation; Android Home reserves **non-executable** Material Verification entry only.
- Completed Patrol Records is history, not a new dashboard; preview HTML/modal and export XLSX. Legacy record semantics preserved.
- Separate API/Web/Android CI pipelines/builds are allowed within the **same monorepo**; Android location `android/`.

---

## 3. Approved product scope & behavior

### 3.1 Site, users, schedule and offline

- **2 lines / 2 PADs** (one SM-T500 per line). PADs are **Admin-registered and line-bound**, not IPQA-bound. Different IPQAs use their **own online login** on the same line PAD and may continue its shared Task and Draft.
- Web Admin manages PAD pairing, binding, revocation/replacement and separate NG PIC roster (PIC does not need a login); QA owns PIC data/content.
- Server creates Task for each line at **07:00 and 19:00** plant-local (`America/New_York`), Day/Night; ~28 items/task is an estimate, **not** a cap. Freeze Standard, Shift, Line and configuration at generation; prevent overlapping active plan occurrences for same line/round.
- Only previously downloaded tasks can be viewed/edited offline. Local data persists through app kill/PAD reboot. **Online-only** login, user switch, new-task fetch and official Submit.
- Connected devices automatically synchronize **Draft and photos** only; **never** auto-Submit. Explicit IPQA Submit needs online server acknowledgment.
- **72-hour ordinary OfflineGrant** after successful online validation. Restart recovery allowed; time rollback/corruption requires online revalidation; expired grant locks editing/access but **does not erase local work**. Not an anti-root/secure-clock project. Offline revocation cannot be instantaneous; check server before online sync.
- V1 signed APK installed manually by IT; future server update detection/download is a roadmap item, **not V1**. Always protect local unsynced data during upgrade.

### 3.2 Android screens and fields

- Home: **Patrol** and disabled/future **Material Verification**.
- Patrol Tasks tabs: **Pending Tasks**, **Re-inspection Tasks**, **Completed Tasks**. The Android Completed tab includes submitted **Pending Approval**, final **Approved**, special **Pending PQE Not-Performed Confirmation**, and final **Not Performed — PQE Confirmed**. Do not mistake Android tab placement for DB Completed state.
- Patrol Items, Item Detail, task Basic Information, sticky Save Draft/Submit. Landscape two-column when practical, portrait fully functional.
- Optional task header: `Customer`, `Production`, `Product MO` as constrained text. `Production` meaning intentionally not specified for V1; revisit in V2. No new Machine Code/Series/Mold fields on Android (preserve legacy values).
- Qualitative samples: OK/NG. Quantitative samples: optional Barcode (manual or external keyboard-wedge scanner; duplicates allowed), required actual numeric Inspection Value, server judgment using **frozen** PQE numeric limits. No camera barcode scanner V1; no fabricated value.
- Each ordinary item requires **at least frozen Standard.SampleCount**; missing count defaults 1; **maximum 50**. IPQA may Add Sample.
- NG Type, ordinary NG Reason and Remarks optional; special **UnableToMeasureNG** requires NG Reason and **current-attempt new photo**; do not create fake `0` or numeric sample.
- A genuine missing/not-applicable item may be individually marked **Removed** after Production/EQ confirmation; optional removal reason, no bulk delete; keep frozen item + action actor/history. **No N/A for new LineShared Tasks**; old historical N/A persists.
- Deleting **last** active item shows Warning but is allowed. All-Removed official Submit enters independent `PendingNotPerformedConfirmation`; PQE confirms `NotPerformedConfirmed` or rejects with specific items to restore/reinspect. Must never silently count as normal OK.

### 3.3 Reinspection, approval and samples

- First official Submit **with NG → ReinspectionRequired** directly, **not** PQE PendingApproval. PQE can read NG revisions/photos/PIC/history immediately, but no artificial PQE rejection/approval action is required to enter reinspection.
- Reinspect **target NG items** or exact items PQE rejected (including previously OK or Removed); complete a **new attempt and new sample set** per targeted item. Untargeted previous OK results may carry forward **with their original provenance**.
- Reinspection minimum when returning to measurable inspection: `max(frozen minimum, prior actual formally submitted measured sample count, preserved inherited minimum)`, capped at 50. An intermediate UnableToMeasureNG or Removed **does not reduce** the established minimum. Identical new measurement values are allowed; do not reuse old sample records.
- Repeated NG is a valid new immutable submission and remains Re-inspection. NG→OK requires **new improvement photo**. NG again also needs a **new photo for the current attempt**.
- All normal items OK after Submit → `PendingApproval`; PQE Approve exact revision → DB `Completed` / Android Approved. PQE Reject requires reason and **specific target items**, may select previous OK/Removed, then back to Re-inspection. PQE approval/rejection is bound to `SubmissionId` / expected revision; stale decisions fail.
- All-Removed uses a **separate PQE confirmation decision**, not normal Approve. Terminal `NotPerformedConfirmed` remains auditable and visible in Android Completed and Web history with distinct outcome/filter.
- Formal business inspection timestamp is **server Submit time**, not individual item/sample timestamp. Final Inspector comes from real final `SubmittedBy`, not legacy personal assignment.

### 3.4 NG PIC, inspection evidence and Reference Images

- PIC: per-NG-item optional multiselect from **Admin-managed** independent roster of name/department/active flag (e.g. Production/EQ/BOM); freeze PIC names/departments in submission; no email/Teams notifications V1.
- Per item/attempt multiple photos; **NG requires ≥1 current new photo**. OK requires photos only if frozen PQE `PhotoRequirement = Required`, except NG→OK requires at least one new improvement photo. Removed not mandatory photo. Reference images and evidence images are distinct types/lifecycles.
- Camera and Gallery/Album supported; copies actual bytes to app-private persistent storage (not external-only URI). Compress automatically subject to QA readability acceptance; keep original local file until server Ready **and draft association confirmed**.
- Initial **technical**, server-configurable parameters (not frozen business promises): up to **5 photos/item/attempt**, up to **5 MiB** per uploaded photo, long edge ~1920 px, JPEG quality ~85, target ~2 MiB. QA may require adjustment based on real defect/label samples; Android must not hardcode final policy.
- Upload is idempotent via `ClientPhotoId` / `AttachmentId`, hash, staging→Ready. Official Submit requires required attachment(s) fully Ready. Submitted revisions reference immutable evidence sets; no overwrite of historical photos.
- PQE uploads reference image **versions**; Task snapshot freezes version/hash; PAD caches and can view/zoom offline. Reference versions still used by Task/Standard/history **are not removed by five-year evidence-photo retention**.

### 3.5 Retention and infrastructure

- Hosting target: **one IPQC server** for Web, API, PostgreSQL and **separate photo data volume**, initially **1 TB expandable**. Does **not** use existing MES FTP; PAD/Web obtain photos via authenticated HTTPS API, never SMB/FTP/direct file paths.
- Capacity planning assumption: `2 lines × 2 tasks/line/day × 365 days/year × 5 years × 28 photos/task × 1.5 MB/photo = 306,600 MB ≈ 307 GB` (decimal). Photo count varies widely with NG; 1 TB is capacity headroom, **not an exact five-year guarantee**; exclude DB, OS and backups from photo-volume capacity.
- Formal evidence retention: **five calendar years from EACH official Submit** (`SubmittedAtUtc.AddYears(5)`); a later official revision/reference can protect a shared physical attachment longer. Metadata, immutable quality result, submission/review history and deletion audit **remain**.
- **Latest approved policy is automatic hourly expiry cleanup** with startup/catch-up; **NO D19 manual per-batch QA/IT approval and NO V1 hold workflow.** QA/IT policy email should confirm this interpretation before enabling deletion in production; it is **not a runtime approval step**.
- Delete state/lock protocol protects in-use references; deleted-by-policy and unexpected-missing/corrupt are different historical UI statuses. Backup sets rotate under IT policy; restore requires re-evaluating expiration **before** re-exposing stored photos.
- Separate, failure-domain-independent **joint PostgreSQL + photo backup** and restore test required. IT must verify server/disk ownership, capacity monitoring, TLS, service account, backup, and the actual SM-T500 deployment environment. These items remain **pending infrastructure/UAT**, not finished work.

---

## 4. Approved architecture and critical engineering contracts

### 4.1 Components and boundary diagram

```text
                       JAX Power IPQC (single monorepo)

 Web (Vue 3 / TS, EN+ZH) ---- same-origin HTTPS /api ----+
                                                         |
 Android SM-T500 (Kotlin, EN) -- HTTPS + device mTLS ---+--> ASP.NET Core 10
    |                                                        |  - Native auth/session/JWT/RBAC
    +- Compose / ViewModel / StateFlow                         |  - LineShared policy + scheduler
    +- Room / operation outbox / app-private photos            |  - DraftVersion / immutable revisions
    +- WorkManager (draft/photo only, NEVER submit)            |  - attachments / Ready / retention
    +- Keystore / OfflineGrant                                |  - Web PQE + history / XLSX
                                                             +--> PostgreSQL (production)
                                                             +--> Storage Adapter
                                                                    +--> local expandable photo volume

 IT managed: TLS/mTLS + independent DB/file backups + monitoring
 Future: Material Verification via separate MES/vendor integration (not V1)
```

- Android: Kotlin, Jetpack Compose Material 3, Navigation 3 stable, single app module with clear packages; StateFlow, Room, Coroutines, Retrofit/OkHttp, WorkManager, CameraX + Album, Coil. `minSdk = 30` for Android 11, stable pinned toolchain at project bootstrap.
- Device identity: Android Keystore non-exportable key pair, Admin pairing and fixed `LineId`, client certificate / **mTLS** on a separate Native TLS host/port on the same backend, plus personal JWT/NativeSession. Verify real PAD/TLS support with IT early; Android identity is **not** IP address or arbitrary client `DeviceId`.
- Authorization for new Task writes: active IPQA + current session + registered/non-revoked PAD + correct device line and binding version + Task.LineId; Web PQE/Admin continue role-specific Web permissions. No use of a fictitious assignee to satisfy older constraints.
- Shared Task workspace key: Environment + RegisteredDevice + LineId + TaskId. Contributing user, uploading user and final submitting user remain separate.
- Server-authoritative: task generation, frozen limits/standards, valid item dispositions, sample bounds, photo requirements, revision state, RBAC, data retention and stale review checks.

### 4.2 **Signed-off F2 addendum: A/B submission handoff**

**This paragraph is newer than Rev3.1 and is part of Peter's final architecture sign-off.** It clarifies rather than replaces the Rev3.1 §9.1 contracts.

1. **Before B takes over**, B must first query A's authoritative `SubmissionAttemptId` receipt and refresh the server Task/Draft/Revision state. A `NotFound` alone does **not** establish that the prior in-flight Submit can no longer commit.
2. If B explicitly takes over, **A's old intent remains persisted for `query-only` recovery and must not be sent/retried again by this PAD's coordinator**. B must explicitly click Submit and create a **new intent bound to B**; never rename A's intent or impersonate A.
3. **At most one locally sendable intent per Task.** Historical `OutcomeUnknown`/A intent can coexist only in **non-sendable query-only** form during B takeover. Task-level coordinator must serialize the transition and preserve audit/recovery state across reboot.
4. The backend uses `BaseRevisionNo`/`ExpectedDraftVersion`/conditional atomic checks so **at most one submission is accepted from the same base state**, even if an A request was already in flight when B took over. Loser retrieves authoritative outcome, never produces a fake next NG revision.
5. Implementation must **not** claim that marking A query-only cancels a request already on the network. The possible late A commit must be handled by version conditions and receipt reconciliation. Both operators' provenance remains truthful.
6. This is an engineering contract / additional **F2-05 acceptance case**, **not** an invitation to reopen product choices or start another architecture revision.

### 4.3 F1: provenance, offline author and uploader

- Each local operation: stable `OperationId`, Task/Item/Attempt, contributor context bound to previously issued device/user/line/binding; actual uploader authenticated separately. The server resolves author from trusted stored contributor context, **not from request-supplied name**.
- A grant expired / normal logout but A is still authorized and context valid → currently authorized B can transport A's work, retaining A as author and B as uploader.
- A account disabled / qualification removed / explicit security revocation / invalid provenance → quarantine not-yet-accepted dependent operations; retain bytes and conflict details; B must reperform, not relabel A. Already accepted immutable history is not retroactively rewritten.
- PAD revoked or line/binding mismatched → reject sync; no cross-line transfer. Local unsynced data never deleted by this authorization failure.

### 4.4 F2: draft synchronization and idempotent Submit

- `LocalEditVersion` is local acknowledged edit sequence, `DraftVersion` is server draft version, `CurrentRevisionNo` is successful formal submission count. They are **different counters**.
- `Save Draft` uses stable `OperationId` and expected `DraftVersion`, server conditional write. Conflict shows both versions and keeps unsynced local content; **no silent last-write-wins**.
- A foreground Submit creates durable `Preparing` intent and freezes local content/photos; task sync barrier waits for eligible in-flight operations, finishes Ready uploads and persistent draft associations, then seals exact manifest, `CanonicalizationVersion` and hash. `Sealed` content **cannot change on retries**.
- Only explicit online IPQA action sends official Submit. `SubmissionAttemptId` is unique per Task; backend transaction checks authorization, base revision, draft version, sample rules, photo Ready, provenance, state; atomically creates immutable submission + attachment refs + receipt. Same attempt/same content yields original receipt; same attempt/different content conflicts.
- Lost response → `OutcomeUnknown`, query receipt before any retry. Workers may prepare/sync/query but may **not** post Submit. An unconfirmed attempt is never casually discarded; cross-user handoff follows §4.2.
- PQE decisions similarly require exact submission/revision and idempotency; stale approvals/rejections fail.

### 4.5 F3: physical attachment and auto-retention races

- In V1 each `AttachmentId` maps to its own `StorageKey`; no cross-attachment physical deduplication. Existing submitted photo can have multiple references from later submissions, each with its own 5-year `RetainUntilUtc`.
- PostgreSQL transactions for *new/replace/remove* draft/submission/reference dependencies and cleanup **lock the same Attachment row in consistent order**. New reference requires `Ready + Available`; deletion claims `DeletePending` atomically after checking every live dependency. Only one wins the race.
- File deletion happens **after DB commit**, with durable `DeleteOperationId`, retryable state, worker lease and crash recovery. `FileNotFound` is policy deletion only when matching persisted delete intent exists; otherwise it is unexpected missing/corrupt with an alert.
- History viewer and XLSX distinguish policy-deleted from integrity failure. Old backup restore must recalculate/reference-check expiry and reconcile before exposing file reads.

---

## 5. Approved task state machine (new LineShared workflow)

| Event | Backend state / outcome | Android tab / label | Web/PQE action |
| --- | --- | --- | --- |
| Generated / first Draft | `PendingInspection` / `InProgress` | Pending | None |
| Official Submit with any NG | `ReinspectionRequired` | Re-inspection — NG | May **read** submitted NG immediately; no approval needed |
| Reinspection Submit still NG | `ReinspectionRequired` + new immutable revision | Re-inspection | Read latest + prior revisions |
| All applicable inspected items pass | `PendingApproval` | Completed — Pending Approval | Approve exact revision or reject with reason + targeted items |
| PQE Approve | `Completed` | Completed — Approved | Immutable completed record |
| PQE Reject | `Rejected` | Re-inspection — Rejected by PQE | Must select target items and require reason |
| All items Removed, official Submit | `PendingNotPerformedConfirmation` | Completed — Pending PQE Not-Performed Confirmation | Separate confirmation or targeted rejection |
| Special PQE confirm | `NotPerformedConfirmed` | Completed — Not Performed — PQE Confirmed | Distinct terminal history outcome |
| Special PQE reject | `Rejected` | Re-inspection | Restore/reinspect exact targeted items |

**Do not equate Android “Completed Tasks” tab with backend `Completed` state.** Web *final* Completed history only includes Approved and specially Confirmed Not Performed; pending decisions remain visible in appropriate Task/PQE screens. Legacy records continue existing semantics under old WorkflowVersion.

---

## 6. Delivery phases (planning order, not permission to implement in one change)

| Phase | Target deliverable | Notes |
| --- | --- | --- |
| P1 | WorkflowVersion / LineShared policy, device pairing/binding/revocation, Native TLS/auth | Preserve existing personal assignment; validate mTLS path early |
| P2 | Shared draft versions, operation/contributor context, NativeSession/OfflineGrant | Conflicts, offline provenance, recoverable author transfer |
| P3 | Item/sample/Barcode/Removed/UnableToMeasureNG, Task headers, PIC roster | Frozen standard + server validation; ≤50 |
| P4 | NG/reinspection/all-Removed state machine, immutable/idempotent Submit, precise PQE review | Preserve old workflow and report invariants |
| P5 | Storage adapter, inspection evidence and reference image versions, Ready upload/links | File+DB lifecycle, retries, history |
| P6 | Web PQE review/special confirmation, completed history, viewer, XLSX | EN/ZH Web, no dashboard/PDF/ZIP |
| P7 | Android scaffold, Admin pairing, auth, offline cache, task/navigation | SM-T500 Android 11, English-only |
| P8 | Android forms, shared draft, photo capture/auto-sync and conflict UX | No automatic formal Submit |
| P9 | Android official Submit/reinspection/all-Removed end-to-end | F1/F2, idempotency, photo verification |
| P10 | Five-year cleanup, local GC, backup-restore and signed APK/UAT | Policy confirmation + IT/QA operational gate |

**Break these into SMALL PRs.** P1–P10 are ordering buckets, **not** 10 mandatory gigantic PRs. Follow actual dependency graph; Android development may begin once contract and authentication interfaces are stable, without waiting for every Web page.

### 6.1 Recommended new-thread topic: first PR / P1-01

**Recommendation for discussion (not yet an agreed PR scope):** a small **API/domain foundation** PR for stable `LineId`, `WorkflowVersion` and `LineShared` vs legacy-personal policy boundaries, plus migration safety and regression tests. **Do not mix an entire mTLS PKI, Android client, scheduler rewrite or UI redesign into the first PR.**

Before the first implementation prompt, Codex should inspect current models/services/migrations and propose one precise PR that states:

1. Current `HEAD` and working-tree verification (do not overwrite existing work).
2. Why the minimal domain contract is needed and exactly how old Task/Plan/User relationships survive.
3. Proposed schema changes + EF migration approach, backfill/default behavior and rollback/data safety.
4. Which services/endpoints change and which are deliberately unchanged; no bypass via older endpoints.
5. Exact acceptance tests including legacy N/A/history/owner regression, LineShared non-fake-assignee ownership and explicit server policy guards.
6. Commands/results for targeted backend tests, TypeScript strict/lint/frontend build if touched, fresh migration, and PostgreSQL design/checks.
7. Clear out-of-scope list (no Android UI/photo engine/full device PKI in P1-01).
8. Branch/PR naming and review checkpoint.

**Proposal first, then obtain agreement on that PR's boundaries.** Architecture approval gives permission to implement a *scoped, agreed* first PR, not to perform broad, unsupervised rewrites.

### 6.2 Minimum PR quality protocol

- Before change: `git status`, `git branch --show-current`, `git log -1`, `git fetch` if network allowed, and `origin/main` comparison. Report if network unavailable; never claim fresh verification without evidence.
- Avoid unrelated edits, fragile migrations, and backfilling unsupported history. Add server-side guards for new `WorkflowVersion` branches.
- Unit/integration regression for existing personal/N/A/Approved-Unqualified workflow; add PostgreSQL integration whenever testing concurrency, locks, and provider-specific migrations.
- Preserve immutable submissions/reviews; data migration must not rewrite or fabricate old historical facts.
- Security: service authorization not UI-only, no credentials in Git, no production customer data fixtures.
- PR review output: changed files, behavior, tests and exact PASS/FAIL/SKIP, risks, deferred work, link/commit when actually produced.

---

## 7. Required test gates and external dependencies

### 7.1 F1–F3 tests (Rev3.1 §15.1, 14 groups)

- **F1-01–03:** valid historical A grant + B transport; disabled/revoked A quarantines; revoked/rebound PAD and forged provenance blocked.
- **F2-01–06:** Save/Submit concurrency; late photo Ready; stale version/intent mismatch; commit-success-response-lost and reboot; **A/B explicit takeover with query-only old intent (§4.2)**; old NG retry vs genuine new NG revision.
- **F3-01–05:** attachment-lock delete/reference race; shared reference with later retention; crashes across DB/file steps and multi-worker retry; unexpected missing/corrupt; restore older backup and re-evaluate expiry before serving.

These tests are **required to be written/run in relevant implementation phases; none is reported as executed by architecture approval**.

### 7.2 Broader acceptance / UAT

- Sample boundaries 1, 50, 51; frozen SampleCount and reinspection lower bound retained through UnableToMeasureNG/Removed.
- Last Removed warning but successful special Submit; PQE special confirmation/rejection; NG→NG/NG→OK required new photo; stale review fails.
- Weak Wi-Fi, airplane mode, app-kill, reboot, full storage, offline 72-hour expiry/clock anomaly, same-PAD A/B handoff and device revocation.
- Real reference image version freeze and offline cached zoom; Album bytes copied to app-private file, no accidental unique-copy deletion.
- Historical N/A, Approved-Unqualified, assignee, old time fields and reports remain readable.
- Live PostgreSQL migrations, conditional writes/lock semantics and backup/restore, not just SQLite tests.
- Real SM-T500 + keyboard-wedge scanner + native TLS/mTLS + signed in-place upgrade retaining Room/photos.

### 7.3 External owner/action (separate from architecture sign-off)

| Owner | Requirement / dependency | Current known status |
| --- | --- | --- |
| **QA** | PIC roster, frozen Standard/reference image inputs, NG/OK photo samples, compression readability, user UAT | Inputs pending |
| **QA / IT** | One-time policy confirmation that formal evidence auto-deletes after five years, results/history/meta remain, backups rotate; no per-delete approval or V1 hold | Confirmation pending before production enabling retention job |
| **IT / Alex** | IPQC server, PostgreSQL, separate expandable 1 TB volume, HTTPS + distinct Native mTLS entry, secure keys/certs, failure-independent coordinated backups, monitoring | Server expected/provisioning; actual config/restore not verified |
| **IT** | PAD install/management, signing key, scanner, native TLS port/network validation | Deployment validation pending |
| **Codex** | Implement per scoped PR, prove code/test results, provider-specific PostgreSQL tests | Development phases not yet executed |
| **Peter** | Agree on first PR scope/review gates and subsequent incremental merges | Architecture **APPROVED**; PR-specific scope discussion is next |

### 7.4 Explicitly out of V1

- Material Verification workflow or live MES/WMS integration; new dashboard; PDF/ZIP reporting; notifications; O365/Entra/SSO; legacy GQMS migration.
- Android camera barcode scanner, first-release automated APK updater, elaborate clock anti-tamper/MDM security system, retention hold/approval worklist, photo AI authenticity analysis.
- Moving photos into MES FTP or PostgreSQL binaries; adopting a second Android repo.

---

## 8. Immediate work plan in the next thread

1. Attach **this handoff** and ideally also **the full Rev3.1 architecture file**. Treat the existing local repo as an AS-IS source, not as a verified implementation of Rev3.1.
2. Ask Codex (or use ChatGPT for architecture scoping first) for a **read-only P1-01 first PR scope proposal** grounded in current files and services. This scoping inspection may use read-only Git and code searches.
3. Review impact on historical personal-assignee/N/A workflow, deployment/migration and tests. If no concrete blocker, approve a bounded P1-01 implementation prompt.
4. Implement, test, review and merge P1-01; then agree the next small PR. Later address mTLS device enrollment early rather than leaving deployment feasibility to the end.
5. Keep project decision log and PR reports current; do not silently reinterpret approved Architecture v1.

### Ready-to-paste new-thread kickoff prompt

```text
We have completed and formally signed off JAX Power IPQC Android Architecture v1 on 2026-11-06.
The authoritative design is IPQC_ANDROID_ARCHITECTURE_V1_REV3_1_2026-11-06.md,
plus the signed-off F2 A/B submit-handoff clarification in the attached
IPQC_ANDROID_ARCHITECTURE_V1_IMPLEMENTATION_HANDOFF_2026-11-06.md.

Peter approved moving into development. We are now planning the FIRST SMALL PR,
not requesting the whole Android app. Do not reopen confirmed business decisions.

First: verify the current repository branch, HEAD, working tree and source files.
Then propose P1-01: the smallest safe foundation for LineId, WorkflowVersion,
and new LineShared policy while preserving all existing legacy Web/API behavior.
Show exact scope/files/migration strategy, tests, risk, explicit exclusions,
and a suggested Codex implementation prompt. Do not modify code or create a
branch/PR until we agree on this first PR scope in this thread.

Repo: https://github.com/peterl-ai/IPQC-system
Local checkout: C:\Users\kuohao.lai\Desktop\project\IPQC\IPQC-system

Important: historical personal ownership/N/A and Approved-Unqualified reports
must survive. Do not implement Material Verification. Work incrementally.
```

---

## 9. Traceability / integrity notes

- This file was authored for handoff **outside** the Git repository. It is a summary of the approved Rev3.1, previous IPQC milestone documentation and Peter's latest explicit F2 sign-off. It is **not a database migration, executable design, branch or PR**.
- The full Rev3.1 contains more detailed F1–F3 rules and 14 acceptance cases. When an edge case is not fully restated in this handoff, use Rev3.1 rather than infer a new rule.
- No current repo Git state or production environment was newly verified while writing this handoff; do not present historical commit or tests as today's observed facts.
- Architecture-approved != tests passed != production rollout ready.

**END OF HANDOFF — next thread: P1-01 scoping and first PR discussion.**
