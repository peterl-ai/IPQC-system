# Android V1 A03-01 protected shared-work and outbox foundation

A03-01 provides an internal Android ledger for immutable local operations, protected source material and durable outbox state. The implemented typed domain is the backend's sample V1 envelope. It is exercised by synthetic tests. The production app continues to use its existing read-only Patrol paths.

## Verified A02 baseline

PR #36 merged reviewed HEAD `0d71f3df841661d2a2c97154e95a590a1e9b6d51` into main as `79231eaa43604db7f5bbe30c337a4165960895f4`. The merge and reviewed HEAD have the same tree. Before implementation the merged-main Push run on that merge commit passed all four jobs. Android ran 52 JVM tests and passed lint and both APK assemblies. Backend Release passed 1,186 tests and skipped 766 opt-in PostgreSQL cases. The separate PostgreSQL 17.12 job passed all 766. Web passed 166 tests in 23 files, lint, typecheck and production build. Each actual job log was checked against the merge SHA. [Merged PR #36](https://github.com/peterl-ai/IPQC-system/pull/36) · [Merged-main CI](https://github.com/peterl-ai/IPQC-system/actions/runs/44758861582)

## Audited server contracts

The current routes below are relative to `/api/line-patrol/tasks/{taskId}`. They require current personal authority. The sample mutation additionally requires `LinePatrol.ContributeDraft`. A future online adapter must use B's current authenticated server requests, validate the results against the task and actor that requested them, and then pass scoped evidence to this ledger.

| Route | Current contracts and future use |
| --- | --- |
| `POST /work-contexts` | `WorkContextIssueInput` binds expected line, workflow and environment. `WorkContextIssued` returns metadata, one proof secret, ordinary offline receipt and `CanExecute=false`. A future issuer validates the metadata and pins the context, original user and issuing session alongside the protected secret. |
| `GET /work-contexts/{contextId}` | Returns metadata for the matching task and context, including issuing session, auth version, capture bounds, security revocation and revision. A future coordinator can refresh metadata without receiving the proof again. |
| `POST /work-contexts/validate-sources` | Takes expected line/workflow/environment and context ID, proof and contributor assertion for each source. A successful `WorkContextPreflight` returns the verified context ID, contributor and capture-until per source, with `Purpose=SourcePreflightOnly` and `AcceptsWork=false`. The future adapter binds that result to the task and current uploader. |
| `GET /shared-draft` | Returns actual TaskId, LineId, WorkflowVersion, DraftVersion, availability, current item/attempt pairs, sample data and optional item projections. The coordinator uses the authoritative scope, versions and attempts for a current task grant. |
| `POST /operation-batches` | `DraftBatchInput` binds the sealed batch, expected scope and DraftVersion, typed operations, accepted dependency references and separate source proofs. The server computes hashes, resolves both actors and applies shared-draft CAS. `DraftBatchResult` may include a batch receipt or return previously accepted operation receipts with `ReplayOnly`. |
| `GET /operation-batches/{batchId}/receipt` | Returns the stored `DraftBatchReceipt`, including request fingerprint, original actual uploader/family, versions and operation receipts. It can reconcile an attempt whose local outcome is unknown. One 404 leaves the outcome unknown. |
| `GET /operations/{operationId}/receipt` | Resolves the accepted operation for the scoped task. The generic endpoint currently dispatches sample, removal, attachment, unable-to-measure and evidence-binding receipts. The separate header, PIC and ordinary NG detail endpoints have their own receipt types. A later transport adapter must handle individual receipt recovery and mixed replay. |

The source for the audit is `WorkContextContracts.cs`, `WorkContextEndpoints.cs`, `WorkContextService.cs`, `SharedDraftContracts.cs`, `SharedDraftEndpoints.cs`, `SharedDraftService.cs` and `DraftCanonical.cs`. A03-01 adds no Retrofit methods or endpoint invocations for this surface.

## Local storage and authority

The A02 `patrol-task-cache.db` Room v1 schema and exported schema are sufficient. The existing `cache_chunk(slot, part, ciphertext)` table stores both formats, so a populated v1 database opens directly. The existing encrypted `workspace-v1` record supplies the same installation UUID to both. Workspace lookup and creation are transactional. The shared key continues to use `ipqc.shared-task-cache.v1`. Application origin, environment, existing installation ID, real TaskId, LineId, workflow and record kind feed the opaque work slot. Each separate source, catalog, operation and batch record is sealed with AES-256-GCM, slot-bound AAD and a fresh IV before it reaches SQLite. The existing chunking and Room transactions also cover journal files. The catalog holds local edit progress and durable operation and batch IDs. Operation envelopes hold typed business inputs, original provenance, server-compatible canonical bytes and SHA-256. Batch records hold immutable membership, dependency frontier, request fingerprint, state and authoritative receipt or conflict evidence.

The work key can decrypt the workspace after the personal vault and offline admission keys are removed. Existing no-backup and secure-screen protections still apply. A missing or corrupt work marker, key or ciphertext fails closed on access to an existing record without resetting the database. The installation UUID is a storage namespace. The server supplies business identity and authority.

The repository sits inside the trusted app coordinator boundary. Each access requires a live verified session stamp from `SessionRepository`. The stamp expires with its bearer or family and fails after rotation, denied authority, or logout. Trusted task evidence must identify the authenticated environment, task, line, workflow, DraftVersion and current attempts. A new operation uses the current actor's own stored issuing context and an authorized item/attempt. When B returns, current task authorization and matched source-preflight evidence are required to inspect eligible unaccepted A operation payloads. Current task authorization is sufficient to reconcile matching accepted facts. The internal preflight callback can borrow a sealed proof for a queued operation in the authorized task. Ordinary projections carry no proof. A03-01 tests provide the task and source evidence synthetically. The live adapter calls and their proof lifetime are a later slice.

## Durable operation and batch contract

- V1 supports typed `CaptureSample`, `CorrectSample` and `RemoveSample` envelopes. The backend's canonical field order, ASCII/Unicode escaping, invariant exact Decimal formatting, explicit nulls and ordered dependency references are pinned by test vectors. Proofs, issuing session and installation IDs, and B's credentials remain outside the server business hash. The encrypted envelope preserves A's original session and context separately.
- Local capture commits the operation and its next `OriginLocalEditVersion` together. A retry with the same operation ID and canonical content returns the original commit. Changed content or provenance under that ID fails with an identity conflict. Dependency references bind ID and hash to the same task/item/attempt/sample. A local commit leaves DraftVersion and CurrentRevisionNo untouched.
- Batch preparation commits the record and all operation links together. It freezes BatchId, expected DraftVersion, exact membership and accepted dependency frontier. Reordering a membership set produces the same fingerprint. Moving an operation to another batch or changing members under an existing BatchId fails. An accepted dependency retains its original server receipt.
- A batch starts Queued. A future authorized adapter can persist InFlight immediately before attempted transport. Reopening an interrupted task moves InFlight to OutcomeUnknown. A missing receipt or later source rejection leaves OutcomeUnknown. Beginning another send from that state is rejected. An unresolved attempt prevents another attempt for the same local task until the coordinator resolves it. This guard has installation scope and server CAS remains authoritative across tablets.
- A matching authenticated batch receipt can transition InFlight or OutcomeUnknown to Accepted. Matching includes task, batch, fingerprint, versions, exact member and reference set, operation hash, attempt, sample identity, work context, original contributor and actual uploader. Acknowledgement affects only that sealed batch. The original receipt can be replayed idempotently. This slice handles a batch receipt whose new operations were accepted by that batch and whose accepted references match previously stored receipts. Individual and mixed-known-operation recovery requires the later adapter and leaves unresolved state intact here.
- Definitive version/scope conflict or source rejection records Conflict or Quarantined and preserves the original bytes, expected versions and provenance. A quarantined queued operation cannot become sendable by assigning another actor or batch. Dependencies on unaccepted conflicted or quarantined work cannot form a sendable batch. Future operator reconciliation must obtain current server state and use explicit reapplication with new operation identities for changed work.

Room rollback covers a partial local operation or batch write. A stable-ID retry after commit can recover the original result after repository/database reopen. Keys survive normal logout and offline receipt expiry. This protection relies on the trusted Android app and OS. It offers no independent proof of physical authorship or defense against undetectable whole-device rollback or lost Keystore material.

## Verification

Windows JDK 17, SDK 36 and build-tools 35.0.0 ran the app's pinned Gradle wrapper using a dedicated offline cache.

```powershell
.\gradlew.bat --console=plain --max-workers=1 --no-daemon --no-watch-fs --offline '-PIPQC_SERVER_URL=' :app:testDebugUnitTest :app:lintDebug :app:assembleDebug :app:assembleDebugAndroidTest
adb -s <emulator-serial> shell am instrument -w -r --no-window-animation com.jaxpower.ipqc.patrol.test/androidx.test.runner.AndroidJUnitRunner
git diff --check
```

- The local Gradle command exited 0. JVM tests passed 61/61 with zero failures or skips, including the 52 existing A01/A02 cases and nine A03-01 cases. Lint reported zero errors and the existing manifest and KAPT advisories. Both APKs assembled and reported minSdk 30 and targetSdk 36. Generated debug `SERVER_URL` was blank.
- The same app and instrumentation APKs passed 16/16 each on Android 11/API 30 and Android 15/API 35 emulators. Both instrumentation commands exited 0 with a successful runner result. Physical Samsung UAT and plant HTTPS remain pending.
- Device coverage includes opening populated A02 v1 storage, encryption of operation and proof data, rollback after actual Room writes, stable replay after re-opening Room, receipt expiry, vault removal, B login and rotation, source access boundaries, and corrupted ciphertext or missing key. Crash boundaries use injected transaction interruptions and database/repository reopen.

## Deferred and production gates

Future slices cover live source issuance/preflight, task grants from authenticated responses, business capture, batch/operation receipt reconciliation over the network, WorkManager, inspections, photos and formal submission. Physical Samsung and plant-network UAT remain production gates. A02's receipt validator still requires the exact earlier of `VerifiedAtUtc + 72 hours` and absolute family expiry. Rev3.2 permits a shorter server-issued deadline, and aligning the validator with such a contract change remains separately scoped.
