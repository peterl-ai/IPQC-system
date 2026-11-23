# Android V1 A03-03 authenticated receipt recovery

A03-03 adds explicit internal foreground entry points on the A03-02 coordinator for reading and reconciling receipts for previously attempted sample-operation batches. Ordinary Patrol browsing continues through A01 and A02. The production UI remains read-only with canExecute=false, canSubmit=false and acceptsFormalInspection=false.

## Verified baseline

PR #38 merged as c0f2bba38a9b72526876d99aab2b2a47752582db. Its tree 208932c5a0381ad57f82b39373332e5a943e5a22 matches the independently reviewed head 1b0ae14c6c38ffcaeedef57072bb50ce91dade3f. Before implementation, the actual logs for the exact merged-main Push run showed Android 82 JVM tests, lint and both APK assemblies, Backend Release with 1,186 passed and 766 opt-in skips, PostgreSQL 17.12 with 766 passed, and Web with 166 tests in 23 files plus lint, typecheck and production build. [Merged-main Push CI](https://github.com/peterl-ai/IPQC-system/actions/runs/45193105679)

## Backend contracts and query scope

Both reads use the current authenticated IPQA authority under PersonalAccess.RequireCurrent and WorkContextStore. SharedDraftService applies the server task read policy and looks up the receipt within the task.

| Route under /api/line-patrol/tasks/{taskId} | Response and local use |
| --- | --- |
| GET /operation-batches/{batchId}/receipt | DraftBatchReceipt contains TaskId, BatchId, RequestFingerprint, first accepting uploader and personal session, AcceptedAtUtc, PreviousDraftVersion, DraftVersion and the receipt list. The sample batch receipt includes fresh sample operations and previously accepted dependencies. |
| GET /operations/{operationId}/receipt | A sample DraftOperationReceipt contains task, operation and batch IDs, canonicalization version and hash, operation kind, item/attempt/sample IDs, source WorkContextId and contributor, first accepting uploader and personal session, environment/line/workflow, AcceptedAtUtc and DraftVersion, value, removal flag and ValueOperationId. The route can also return other domain shapes. |

The implementation follows SharedDraftEndpoints.cs, SharedDraftContracts.cs, SharedDraftService.cs, WorkContextStore.cs and the sample acceptance/CAS and canonicalization code. The parser accepts a sample-only receipt shape with the server's exact JSON types and required fields. It bounds successful response bytes, depth and membership count, rejects duplicate or unknown fields and unsupported domains, and converts numeric JSON decimals without floating-point rounding. The server materializes quantitative judgment from the frozen definition. The client checks the quantitative type, exact decimal and barcode, and a valid server judgment. Qualitative receipts must match the original judgment and have null numeric value and barcode. Removals have a null value and retain the previous ValueOperationId.

One current grant binds the online task detail and draft read to the exact session stamp. Before either GET, the coordinator resolves the identifier against the protected local ledger for the bound installation, origin, environment, TaskId, server LineId and WorkflowVersion. It requires a sealed InFlight, OutcomeUnknown or Accepted attempt. It recomputes the canonical operation bytes and hashes, exact dependency frontier and batch fingerprint. The selected line is a UI filter. The scope comes from the authenticated task and protected envelope.

The same authority is checked when sending, after receiving and again inside the Room transaction. A local task/line selection change supersedes an in-flight response using the grant epoch. Receipt publication and selection changes serialize during the Room transaction. Session rotation, logout or 401/403 disables the old authority through SessionRepository. Queries use the current actor's bearer. The stored historical proof and credentials never travel on these GETs.

## Durable evidence and OutcomeUnknown

A batch acceptance must match the protected manifest and fingerprint, previous draft version and exactly its next committed version. Every fresh receipt must match its operation ID, hash, scope, source, contributor, sample identities and result. Uploader and timestamp must agree with the enclosing receipt. Earlier accepted references must match their stored receipts including the earlier actors, batch, version and time. Correction and removal must point to the exact original predecessor by ID and hash, within the same item, attempt and sample. Duplicate or missing operations, aliased samples, and inconsistent predecessor or actor evidence produce a recovery conflict.

| Observation | Local outcome |
| --- | --- |
| Exact full Batch Receipt | Save the immutable receipt and set the batch Accepted in one Room transaction. Only this transition releases the existing unresolved-attempt barrier. |
| Exact Operation Receipt | Save the individual historical fact or replay an existing matching fact. The batch state and sibling operations stay as they were. A dependent operation needs a verified predecessor receipt. |
| 404, or individual predecessor unavailable | Return unresolved. Preserve the sealed manifest, source, and attempt barrier. Each 404 is a point-in-time observation. |
| Typed mismatch or HTTP 409 | Record the bounded conflict reason under the lookup in protected storage. Retain the sealed manifest and any prior acceptance. |
| Authentication failure, transport failure, malformed response or unsupported domain | Return authentication or transient evidence for the explicit query. Keep the ledger available for later, newly authorized recovery. |

An individual receipt can name a different server BatchId if the exact operation was first accepted in another batch. It cannot fill in a missing local batch receipt or confirm sibling operations. A backend pure replay may supply operation receipts without creating a new batch receipt. The original local batch remains unresolved if its exact full receipt is unavailable. No GET observation permits requeue, resend, an unrelated draft revision change or Formal Submit. The backend retains the shared-draft CAS for competing tablets.

The existing Room v1 records use an installation-scoped work key, opaque namespace and slot-bound AES-GCM. A batch and its receipt share one encrypted record. Individual observations and conflict reasons use additional encrypted record kinds in the same database schema. Validation and persistence share one Room transaction. A failure before commit leaves the earlier state. A commit with a lost local acknowledgement replays idempotently. Later inconsistent evidence cannot replace a verified receipt. An unreadable record or missing work key fails closed and preserves rows. A02 cache data, earlier queued edits and source proofs are outside the receipt updates.

In an A-to-B handoff, A's envelope keeps A's contributor, issuing personal session, source WorkContextId and canonical bytes. B authenticates online, independently obtains task-read authority and looks up the original identities. If B was the server's first accepter the receipt retains B's uploader identifiers. If C had already accepted them the receipt retains C's uploader identifiers even when B queries. The acceptance timestamp and version are from the server.

## Verification and deferred work

~~~powershell
.gradlew.bat --console=plain --max-workers=1 --no-daemon --no-watch-fs --offline '-PIPQC_SERVER_URL=' :app:testDebugUnitTest :app:lintDebug :app:assembleDebug :app:assembleDebugAndroidTest
adb -s <emulator-serial> shell am instrument -w -r --no-window-animation com.jaxpower.ipqc.patrol.test/androidx.test.runner.AndroidJUnitRunner
git diff --check
~~~

- Windows Gradle exited 0 with 92 JVM tests passed, zero failed or skipped, Android lint with zero errors and the existing UnusedAttribute and KaptUsageInsteadOfKsp warnings, and both APK assemblies. The generated debug SERVER_URL is empty. Both APKs have minSdk 30 and targetSdk 36.
- The API 30 (Android 11) and API 35 (Android 15) emulators each passed 22 of 22 instrumentation tests with exit 0 and runner code -1. They used the same locally assembled Debug APK pair. The original checkout's 22 protected files matched their SHA-256 manifest.

MockWebServer covers the exact HTTPS GETs, scope and manifest validation, independent operation evidence, repeated misses, HTTP errors, session and selection races, and A/B/C attribution. The device suite exercises actual Room v1 and Android Keystore with controlled interruptions before and after commit, database and session reopening, corrupted evidence, missing key and A02 snapshot preservation. Existing backend concurrency tests retain their two-attempt, one-winner CAS checks, including the PostgreSQL provider.

Physical Samsung Galaxy Tab A7 and plant HTTPS validation remain deployment gates. The fixture and emulator runs use synthetic identities and data. The approved Rev3.2 A02 residual limitation remains as documented in docs/36_ANDROID_V1_A02_OFFLINE_READ_FOUNDATION.md. Completely offline reboot cannot prove true elapsed time when UTC manipulation leaves no detectable inconsistency. Rev3.2 provenance records the server-scoped source and authenticated uploader. Physical authorship and disconnected awareness of immediate server revocation remain outside that guarantee.

Batch upload transport, auto resend, editing, background synchronization, photos, ordinary NG and PIC editing, Material Verification, Formal Submit, PQE approval, reinspection and F3 retention/deletion remain deferred. A03-03 has no server mutation route.
