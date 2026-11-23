# Android V1 A03-05 — Offline Sample Capture Coordinator

## Approved baseline

PR #40 merged approved head `4360f3646c05d4626ed0d91727dbea90f230e13a` into main at
`a22563bc71fedf92c8a3237c0d51832755dd8fe3`. The merged tree is
`bf99ecf3ec7a4f416657db19cc03b0a1c62483ea`, identical to the independently approved source tree.
The four actual job logs were checked at that merge SHA before implementation.

| Gate | Attempt and log | Result |
| --- | --- | --- |
| Backend | [1, job 137936404260](https://github.com/peterl-ai/IPQC-system/actions/runs/45244151498/job/137936404260) | Release build, 1,186 passed and 766 opt-in PostgreSQL tests skipped |
| PostgreSQL | [1, job 137936404027](https://github.com/peterl-ai/IPQC-system/actions/runs/45244151498/job/137936404027) | PostgreSQL 17.12, Release build, 766 passed |
| Web | [1, job 137936404536](https://github.com/peterl-ai/IPQC-system/actions/runs/45244151498/job/137936404536) | Lint, 166 tests in 23 files, typecheck, production build |
| Android | [1, job 137936403611](https://github.com/peterl-ai/IPQC-system/actions/runs/45244151498/job/137936403611) | Maven Central and Gradle Plugin Portal HTTP 429 during Gradle configuration |
| Android retry | [2, job 137955937230](https://github.com/peterl-ai/IPQC-system/actions/runs/45244151498/job/137955937230) | 110 JVM tests passed, Lint passed, debug and instrumentation APK assemblies passed |

GitHub **Re-run failed jobs** ran Android alone. The [completed workflow](https://github.com/peterl-ai/IPQC-system/actions/runs/45244151498/attempts/2) carries forward the other three successful jobs. Attempt 2 produced the [debug APK, artifact 14856786164](https://github.com/peterl-ai/IPQC-system/actions/runs/45244151498/artifacts/14856786164) and [instrumentation APK, artifact 14855541361](https://github.com/peterl-ai/IPQC-system/actions/runs/45244151498/artifacts/14855541361). The downloaded APKs declare min SDK 30 and target SDK 36. The debug DEX declares an empty `BuildConfig.SERVER_URL`. The original Windows checkout matched all 22 protected paths and SHA-256 values.

## Offline admission

An internal caller first opens the exact task online, explicitly issues or confirms the operator's WorkContext, and arms local capture with a fresh authenticated task detail and shared draft. The session needs `LinePatrol.ContributeDraft`, an eligible A02 receipt, and a current contribution grant. The protected record binds the task, environment, line, workflow, owner, personal session, source, effective capture cap, server version, frozen definitions, and server sample observation. An ordinary A02 download cannot arm capture. A02 receipts from older client versions remain readable. A fresh online verification establishes permission for local editing. A known loss of contribution permission survives restart and leaves ordinary A02 browsing available under its own rules.

The local coordinator resolves identity from the protected A02 receipt. It can run after access-token expiry or process recreation while the exact verified family and WorkContext remain eligible. It verifies the original encrypted source and issuance state, known revocation, and protected time. The final transaction check re-observes A02 and requires time strictly before the effective original source cap. A normal reboot uses the approved A02 time evidence. A02 expiry, detected clock inconsistency, logout, user change, known revocation and missing or corrupt state fail closed for the affected operations. Receipt renewal and line switching retain the original source cap. A later authenticated server observation may invalidate the prior basis. Equal-version disagreements disable new edits until a newer consistent observation.

## Local operations and projection

Capture uses independently generated operation and client sample IDs. Correction and removal each take a new operation ID and the exact immediate predecessor ID and content hash for the same client sample and server-issued item and attempt. A removable or correctable predecessor is an eligible unbatched local edit or a matching full-batch accepted receipt for the current server sample. A remote value without that proof can be viewed. A sealed batch or an unresolved result keeps its dependency boundary. Removal records a tombstone and retains the previous value and source in history.

An applicable frozen definition selects qualitative OK/NG or exact .NET-compatible decimal text, with an optional quantitative barcode of up to 200 non-control characters. Local quantitative operations leave judgment to the server. The server-validated frozen SampleCount accepts 1–50 and defaults to one for a blank definition. The local count combines the observed server board and applicable local operations, excludes removed samples, and limits each attempt to 50 active sample identities. Missing samples yield incomplete counts.

The projection returns the latest authenticated server DraftVersion and a separate local edit sequence. It presents the confirmed server snapshot, the applicable effective local board, and operation history with Queued, InFlight, OutcomeUnknown, Accepted, Conflict and Quarantined states. History also carries current-attempt and version applicability. A verified individual receipt is available as evidence. Full-batch acceptance still controls the batch barrier and accepted predecessor authority. A verified receipt ahead of the server snapshot requires a fresh read before a new edit or batch can be prepared. Historical and ineligible operations remain durable.

## Room, recovery, and transfer

The encrypted profile and local capture evidence use the existing work storage namespace, Android Keystore, and Room schema. The capture evidence records the apparent time and checked cap without changing the A03-01 canonical bytes. The transaction serializes admission, global OperationId claim, local sequence, immutable envelope and catalog update. The session and protected clock are rechecked before commit. A storage or authorization failure rolls back the operation. After a lost acknowledgement, look up the exact OperationId or repeat the same typed command. An identical saved command returns the same source, bytes, hash and local version while its owning local admission remains valid, including after the WorkContext capture deadline.

Preparing a batch validates its selected operations and their order against the observed version, current attempts, sample limit and exact predecessor rules before the existing A03-01 seal. The task-scoped BatchId replays its exact membership and manifest. InFlight or OutcomeUnknown blocks new preparation for the task. A03-04 remains the explicit online transport with its current bearer, contribution permission, fresh task read, source preflight and guarded POST. B uses B's WorkContext for new edits. When B is allowed to reconcile A's sealed work, the operation preserves A's contributor, source and personal session, while the server receipt identifies B as uploader.

## Verification boundaries

These changes expose internal application-layer calls only. Production inspection editing, background sync, photo and ordinary NG entry, task disposition workflows, and Formal Submit remain deferred. The offline clock inherits the approved Rev3.2 limitation across reboot. Undetected post-reboot UTC manipulation can affect expiry. The coordinator continues to rely on the trusted Android app and OS. Offline capture uses the last authenticated task and revocation evidence. A03-04 applies the server's current state when explicitly invoked.

### Local execution

Windows JDK 17 and Android SDK 36, from `android/patrol-app`:

```powershell
.\gradlew.bat --console=plain --max-workers=1 --no-daemon --no-watch-fs --offline '-PIPQC_SERVER_URL=' :app:testDebugUnitTest :app:lintDebug :app:assembleDebug :app:assembleDebugAndroidTest
```

Exit 0. The JVM XML reports 121 passed, zero failed, zero errors, and zero skipped. Lint completed with zero errors and the existing `UnusedAttribute` and `KaptUsageInsteadOfKsp` warnings. Both APK assemblies completed. APK badging shows min SDK 30 and target SDK 36.

The exact Debug APK pair ran on API 30 (Android 11) and API 35 (Android 15) with
`adb -s <serial> shell am instrument -w -r --no-window-animation com.jaxpower.ipqc.patrol.test/androidx.test.runner.AndroidJUnitRunner`.
The runner returned 28/28 on each AVD, including four new actual Room/Keystore tests. API 30 took 35.764 seconds and API 35 took 149.339 seconds. Both had runner code `-1`.
The debug APK SHA-256 is `9eeebde6aec343822cdc4d32d3774b61857ba9f330a94f21b5026bbbe9a81c69` and the instrumentation APK SHA-256 is `b70ecf2cd74710aa6ece9497f763cc29fd53820ecf610023bdc558ebb4af1703`.

The test fixtures exercise process recreation, reboot clock reconciliation, exact save retry, commit rollback, lost commit acknowledgement, expiry and operator changes during a write, and encrypted persistence. Physical Samsung hardware and factory HTTPS integration are separate validation.
