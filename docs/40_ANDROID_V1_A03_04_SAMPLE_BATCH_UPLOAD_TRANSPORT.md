# Android V1 A03-04 guarded foreground sample batch upload

A03-04 adds an explicit internal foreground entry point for sending an already sealed sample batch. It uses the existing task-scoped Backend CAS and A03-03 receipt verifier. Patrol screens remain read-only with canExecute=false, canSubmit=false and acceptsFormalInspection=false. The upload has no production UI or background caller.

## Verified baseline

PR #39 merged at 11a43f6a6d8d17c1ccaea564333fc839a9f08925. Its tree e85fed5744a3f2e18103c07db36443e1838b187b exactly matches approved HEAD 1c2fa64c092cdae73dbd72733b8cb2d78a33df75. The actual merged-main Push CI logs checked out the merge SHA. Android passed 92 JVM tests, lint and both APK assemblies. Backend Release passed 1,186 tests with 766 opt-in PostgreSQL skips and zero failures. PostgreSQL 17.12 passed 766 without skips or failures. Web passed 166 tests across 23 files, lint, typecheck and production build. The original checkout's 22 protected file hashes matched their manifest before implementation. [Merged-main CI](https://github.com/peterl-ai/IPQC-system/actions/runs/45216824896)

## Request and response contract

The upload uses POST /api/line-patrol/tasks/{taskId}/operation-batches and the server's DraftBatchInput. The same current session performs the task detail read, shared-draft read and read-only POST to /work-contexts/validate-sources before upload. Backend authorization uses the current active IPQA personal family, LinePatrol.ContributeDraft and the task scope.

| Contract | Handling |
| --- | --- |
| Batch scope | Frozen BatchId, canonicalization version, persisted Task.LineId, WorkflowVersion 2, EnvironmentId and ExpectedDraftVersion. |
| Operations | Stable OperationId, Kind, item/attempt/sample IDs, BaseDraftVersion, OriginLocalEditVersion and exact original source/contributor assertion. The persisted V1 bytes and content hashes are recomputed and checked before encoding. |
| Data | Quantitative values are exact canonical JSON number tokens validated by the decimal rules before encoding. This avoids the Kotlin JSON number encoder's floating-point conversion. Qualitative judgments and explicit nulls are preserved. A removal has null Data and the original predecessor reference. |
| Dependencies | The manifest must contain the exact accepted-reference frontier. Its immutable receipts must resolve to the same protected task and applicable attempt with valid hashes and versions. |
| SourceProofs | The business request supplies only WorkContextId and ProofSecret for each distinct source in its attempted members. Preflight includes the contributor assertion as prescribed by WorkContextPreflightInput. Accepted references require no old proof or preflight. The server derives the uploader from the current bearer. |
| DraftBatchResult | A successful response contains BatchReceipt, Operations, ReplayOnly, CanExecute=false and AcceptsFormalInspection=false. With a full receipt the outer operation list must match the receipt list, including earlier accepted references. The A03-03 verifier validates the complete receipt against the sealed ledger. |

SharedDraftContracts.cs, SharedDraftEndpoints.cs, SharedDraftService.cs, DraftCanonical.cs, WorkContextService.cs and WorkContextStore.cs define the server behavior. The request is bounded to 50 sample members, 100 accepted references, the distinct original proofs and 131,072 encoded bytes. Successful responses are limited to 512 KiB and strict sample types, fields and JSON structure. Tagged errors are limited to 16 KiB before Retrofit can buffer them. Unknown and duplicate fields in successful response objects fail validation.

## Admission and durable state

1. An explicit upload requires the active coordinator grant and the current session's contribution permission. A task-scoped lease in this Android process serializes initiation and distinguishes a live attempt from abandoned InFlight records. Before new upload activity, the coordinator recovers abandoned InFlight as OutcomeUnknown.
2. The task detail and shared draft are fetched again under the same session and selection. Both must match the original scope and the queued batch's expected version, valid planning, initialized draft and applicable items and attempts. Room checks the per-task unresolved-attempt barrier and exact sealed manifest.
3. Fresh preflight under the current uploader checks the exact distinct source contexts, contributor and capture bounds of the still-pending members. A matching SourceSecurityRevoked observation can quarantine a queued batch while preserving the originals. Historical sources retained after normal logout or expiry remain eligible for server checks. Accepted dependencies remain historical facts even if their source is later revoked.
4. Room rereads and verifies the sealed manifest and protected sources, validates the preflight binding and encodes the complete request. It commits InFlight before returning the body to the coordinator. Failure or a lost acknowledgement at this boundary cannot send a business POST. An abandoned committed attempt remains available for separate receipt recovery.
5. The current Retrofit/OkHttp HTTPS path supplies a one-shot request body and disables connection retry and redirects. A final network-interceptor check validates the current session and selection before the request writes headers. The server checks current account, family, line, attempt, provenance and DraftVersion in its acceptance transaction.
6. Receipt application checks the same grant and session under the selection and Room publication boundary. Acceptance and its immutable evidence commit atomically. Settlement of a still-InFlight record changes only the exact attempted batch to OutcomeUnknown, including after cancellation or loss of authority. A failed settlement leaves InFlight recoverable.

## Outcome and attribution

| Outcome | Durable behavior |
| --- | --- |
| Valid full receipt with ReplayOnly=false | The actual uploader and personal session must match the current pinned authority. The validated receipt and Accepted state commit atomically. |
| Valid full receipt with ReplayOnly=true | Keep the original server uploader, session, timestamp and version even if a different operator queries or observes the replay. Apply the same complete verification. An already Accepted local batch uses explicit GET for re-observation. |
| ReplayOnly=true and BatchReceipt=null | Preserve the attempt as OutcomeUnknown. The operation list cannot prove batch completion. Explicit A03-03 receipt GETs are available. |
| Definitive application rejection | The client recognizes specific Backend 400/409 problem documents with matching HTTP and JSON status, code and title. Invalid typed input records a conflict. A matched DraftVersionConflict or recognized scope conflict records the corresponding reason. Source quarantine also requires the exact source and affected operation IDs, including dependent members, and both preserve flags. |
| Unknown completion | Transport loss, malformed or unsupported responses, unknown errors, throttling and transient server errors preserve OutcomeUnknown. Session denials invoke the existing 401/403 revocation behavior. A stale response cannot apply acceptance. |
| 404 during subsequent GET | The query only shows that a receipt was absent at that instant. It preserves the original IDs and the unresolved-attempt barrier. |

When A captures and B later sends, A's original source, contributor, issuing PersonalSessionId and canonical bytes remain in the protected ledger. B's own bearer transports the proof. The authenticated receipt identifies B as the new accepting actor, or C if C was the actual first accepter and the later exchange observes that earlier acceptance. Exact server replay can occur when an already accepted batch is reached by this attempt. The fresh local version and source gates may prevent a late replay POST, in which case the operator reconciles existing attempted work by GET. Pure replay supplies no proof of a complete local batch.

The Backend CAS remains authoritative across tablets. The client never automatically resends or replaces an attempted identity. A definitive rejected batch remains blocked. Rebuilding or retrying a rejected batch needs a separately reviewed policy. The A02 reboot clock limitation is unchanged. Fully offline ordinary Android cannot prove elapsed wall time if UTC manipulation leaves no detectable inconsistency. Physical authorship and immediate offline knowledge of source revocation remain outside the approved Rev3.2 guarantee.

## Verification and scope

~~~powershell
.\gradlew.bat --console=plain --max-workers=1 --no-daemon --no-watch-fs --offline '-PIPQC_SERVER_URL=' :app:testDebugUnitTest :app:lintDebug :app:assembleDebug :app:assembleDebugAndroidTest
adb -s <emulator-serial> shell am instrument -w -r --no-window-animation com.jaxpower.ipqc.patrol.test/androidx.test.runner.AndroidJUnitRunner
git diff --check
~~~

- Windows Gradle exited 0. All 110 JVM tests passed with zero failures or skips. Lint returned zero errors and the existing UnusedAttribute and KaptUsageInsteadOfKsp warnings. Both APKs assembled with minSdk 30 and targetSdk 36, and the generated debug SERVER_URL was empty.
- The same local APK pair passed all 24 instrumentation tests on both API 30 and API 35, with command exit 0 and runner code -1. The original checkout's 22 protected SHA-256 hashes matched before and after implementation.

MockWebServer exercises authenticated HTTPS, exact original identities and decimal fidelity, valid acceptance and replay, pure replay, scope/version/fingerprint/hash mismatches, missing or revoked proof, foreign and malformed dependencies, correction/removal results, concurrent initiators, existing InFlight/OutcomeUnknown and the 404 barrier. It covers 400/401/403/409/429/5xx, redirect prevention, an after-delivery disconnect, transport timeout, malformed/duplicate/unsupported and oversized results, rotation before actual send, cancellation, logout, replacement user and task/line selection changes during response. Real Room v1 and Android Keystore tests inject failures before and after InFlight and receipt commits, inspect encrypted records, reopen and reconcile by GET, and check A02 snapshots. The existing SQLite/PostgreSQL server concurrency tests exercise the competing batch CAS.

Physical Samsung Galaxy Tab A7 and factory HTTPS testing remain separate deployment gates. Emulator tests use synthetic identities and fixture responses. Deferred work includes inspection execution UI, editors, automatic or background synchronization, photos, header/PIC/NG editing, other transport domains, Material Verification, reinspection, PQE approval, F3 deletion and Formal Submit.
