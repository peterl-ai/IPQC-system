# Android V1 A03-02 online WorkContext and shared-draft coordinator

A03-02 adds explicit internal foreground entry points for opening a shared-draft scope, issuing or reusing the current actor's WorkContext and preflighting a queued historical source. The application composition shares the existing Retrofit client, personal session repository and no-backup Room database. Ordinary Patrol screens continue to use A01/A02 browsing. The coordinator has no automatic call from a screen, background worker or inspection execution path.

## Verified entry baseline

PR #37 merged as 23c87d701b89f0b08b90dcd0521dae16305e7c64. Its tree 17a45b1418ce00d6e46852b09070d27bee276d66 matches reviewed HEAD 2246ab01f671bccc9783bcf415cba05bc388f3ef. Before implementation, all four actual merged-main job logs were verified at the merge SHA. Android passed 68 JVM tests, lint and both APK assemblies. Backend Release passed 1,186 tests and skipped the 766 opt-in PostgreSQL cases. The separate PostgreSQL 17.12 job passed all 766. Web passed 166 tests in 23 files, lint, typecheck and production build. [Main workflow](https://github.com/peterl-ai/IPQC-system/actions/runs/44814599288)

## Current server surface

All paths in this table are under /api/line-patrol/tasks/{taskId}. The current business-personal authorization path requires an active IPQA, the read permission, no forced password change and an eligible current personal-session family.

| Route | Actual contract and coordinator use |
| --- | --- |
| GET task detail with expectedTaskLineId | Reuses the A01 task and frozen definition validation. Matches the returned ValidatedTaskAccess to the current authenticated user, family, environment, TaskId, LineId and workflow. The optional coordinator path reads without replacing A02 snapshots. |
| GET /shared-draft | Reads SharedDraftOutput, its persisted scope, signed 64-bit DraftVersion, current item and attempt pairs, dispositions and typed sample projections. Environment comes from the authenticated task detail and stamp. The response is read-only with CanExecute=false and Completeness=NotEvaluatedForFormalSubmission. |
| POST /work-contexts | Sends ExpectedTaskLineId, ExpectedWorkflowVersion and ExpectedEnvironmentId. The authenticated endpoint returns HTTP 201 with WorkContextMetadata, a one-time Base64url-encoded 32-byte proof, an OfflineAccessReceipt and CanExecute=false. The server generates a fresh ContextId. |
| GET /work-contexts/{contextId} | Reads proof-free metadata, including immutable source fields, revision and optional security revocation. Used to refresh evidence before reusing a protected current-family source. |
| POST /work-contexts/validate-sources | Sends expected scope and the source ContextId, original contributor assertion and protected proof. The server accepts a list of distinct sources. This coordinator requests one existing queued source per call and matches the reply to that source. A valid response has Purpose=SourcePreflightOnly and AcceptsWork=false. A matched 409 SourceSecurityRevoked identifies the affected sourceContextIds. |

Source references are WorkContextEndpoints.cs, WorkContextService.cs, WorkContextStore.cs, WorkContextContracts.cs, PersonalAccess.cs, SharedDraftEndpoints.cs, SharedDraftService.cs, SharedDraftContracts.cs, InspectionAttemptService.cs and LinePatrolReadService.cs. The task-scoped authenticated batch and operation receipt GETs remain available through the backend. A03-02 uses neither operation receipt transport nor operation mutation.

## Authority and draft grants

The coordinator pins each authorized exchange to one bearer stamp from SessionRepository. The session mutex checks the exact authority before sending and after receiving. Rotation invalidates the old stamp. HTTP 401 revokes the matching A02 admission and allows the next explicit exchange to rotate. HTTP 403 ends that authority. Observed denial uses the existing non-cancellable revocation path. Coordinator requests perform one HTTP attempt with that stamp and use the existing HTTPS client with one-shot POST bodies and redirects and connection retry disabled. Tokens stay within the session repository and HTTP authorization header.

Selecting a task invalidates the previous selection's grants immediately. The coordinator serializes exchanges, checks that the selection is current before publishing a grant and rejects an observed regression in DraftVersion for the same Task scope during the coordinator lifetime. Grants are tied to the authenticated session and current selection. The coordinator accepts only its own current grant for network operations. UI has no caller supplying arbitrary source metadata or using the internal WorkAccess fixture constructor. An optional read failure leaves A02 cache bytes available.

The coordinator matches SharedDraft to the authenticated task detail, validates the real definition set, rejects malformed or duplicate identifiers and attempt pairs, and checks minimum counts against frozen definitions for initialized tasks. It recognizes the actual backend availability categories. EligibleCleanFoundationOnly has no current attempts. AlreadyInitialized and SharedDraftInitialized require the current attempt set for the frozen items. Unsupported or historical categories can carry read status without granting WorkContext use. The coordinator keeps CanExecute=false and never synthesizes attempts from sample history.

## Issuance, reuse and recovery

An issue reply must match the current actor, personal-session family, environment and Task/Line/Workflow. The coordinator validates source version, original issuing auth version and revision, UTC bounds, the issued receipt's matching actor and deadline, and the current family's exact absolute expiry. The source proof must decode to exactly 32 bytes and round trip through canonical unpadded Base64url. It is sealed before it reaches SQLite. Source proof fields use redacted object representations and are excluded from ordinary grant and operation projections.

Room v1 stores the source and issuance selection in opaque task/installation-scoped slots with the existing shared-work key and slot-bound AES-GCM. Issuance has no server idempotency key. Before POST, the coordinator commits a protected reservation for that origin, task and user across session families. It serializes overlapping requests in memory and compares reservations atomically in Room, including across coordinator instances. A validated success saves the source and replaces the exact reservation in the same transaction. A definitive negative HTTP response or a request that never reached the HTTP call may release only that reservation. A timeout, lost or malformed reply, cancellation while the POST may have run, or failure persisting the proof leaves the reservation unresolved. A restarted coordinator shows uncertainty on the next explicit issue request and sends no duplicate POST.

The server has no request-key lookup, context-list recovery route for this caller or proof re-delivery. If the response is lost, an authorized Admin can consult server issuance/audit evidence when the context can be identified. An authenticated metadata GET alone cannot recover a missing proof or establish non-issuance. This client conservatively holds the affected user's task issuance. A future reviewed workflow is required to reconcile or release the local reservation. Other users and scopes remain independent, and earlier operations and proofs stay intact.

Reuse requires the same current user and family, an unexpired local protected source and a matching current authenticated metadata read. A source past its capture deadline or tied to a different family remains historical. A revoked source is recorded separately and is no longer usable for capture or preflight. A missing or corrupt protected proof fails closed. Metadata reads tolerate only PostgreSQL's one-way sub-microsecond truncation of server ticks. The effective capture deadline can only move earlier when persisted server precision requires it. A03-01 still checks protected server-anchored time immediately before writing and before Room commits any new edit. Exact immutable historical replay remains available through its prior authorization path. A02's approved reboot policy and shorter server-deadline advisory remain as documented in docs/36_ANDROID_V1_A02_OFFLINE_READ_FOUNDATION.md and docs/37_ANDROID_V1_A03_01_SHARED_WORK_OUTBOX_FOUNDATION.md.

## B preflight and protected history

Preflight reads a proof solely from a matching queued operation in the protected Task scope. It sends A's exact stored context, proof and contributor assertion with B's current online bearer. The authenticated reply must match the source, contributor and capture bound and explicitly indicate source preflight only. This grants scoped visibility to eligible historical work. It performs no acceptance transition. An authenticated matching security-revocation response records sticky status under the source and disables new capture, preflight and future send admission. Already accepted facts keep their original attribution. Ambiguous outcomes, a 404 or ordinary network failure cannot prove an operation was never accepted. The ledger's Unknown and receipt recovery semantics remain in effect.

Rev3.2 protects original provenance under the trusted app and Android OS. Proof possession establishes a scoped server source capability. Physical authorship and undetectable UTC manipulation after offline reboot remain outside the guarantee. Source status on a disconnected device can lag a server security change. The server remains authoritative for every later write.

## Verification and deferred work

Windows JDK 17 and Android SDK 36 used the pinned Gradle wrapper and build-tools 35.0.0.

~~~powershell
.\gradlew.bat --console=plain --max-workers=1 --no-daemon --no-watch-fs --offline '-PIPQC_SERVER_URL=' :app:testDebugUnitTest :app:lintDebug :app:assembleDebug :app:assembleDebugAndroidTest
adb -s <emulator-serial> shell am instrument -w -r --no-window-animation com.jaxpower.ipqc.patrol.test/androidx.test.runner.AndroidJUnitRunner
git diff --check
~~~

- The Windows command exited 0. JVM suites passed 82/82 tests with zero failures or skips, including 14 new coordinator regressions. Android lint had zero errors and the existing manifest and KAPT warnings. Both APKs assembled and reported minSdk 30 and targetSdk 36. The debug BuildConfig server URL was blank.
- The same final app and instrumentation APKs passed 20/20 on both API 30 and API 35 emulators. Both commands exited 0 with runner code -1. The two new device tests cover proof persistence and preflight across Room reopening and A/B login changes, SQLite sidecar inspection, rollback after real SQL writes and durable suppression of unknown issuance after reopen.
- The original checkout's 22 protected file hashes were verified before and after local implementation.

Physical Samsung and plant HTTPS acceptance remain future gates. MockWebServer verifies typed HTTPS semantics with synthetic identities. Device tests use actual Room and Android Keystore with a synthetic server adapter. Production inspection edits, active offline entry, operation receipt transport, real batch uploads, background synchronization, photos and Formal Submit remain deferred.
