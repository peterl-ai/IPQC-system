# Android V1 A04-02 protected offline inspection item removal

## Verified base

Peter merged [PR #42](https://github.com/peterl-ai/IPQC-system/pull/42) at
`4c74c1000c7c30997c4877dcfb693b354d80fba5`. The merge contains reviewed head
`7132d18571bd08f4475db332046f721ab9e88e22` and exactly matches reviewed tree
`3e0216487ae10280981f66a080a5917ac37271a9`. All four actual logs in
[merged-main run 45413041721, attempt 1](https://github.com/peterl-ai/IPQC-system/actions/runs/45413041721/attempts/1)
were checked before creating the clean implementation worktree. No overlapping Android PR
was open. The original checkout matched the 22 protected paths and SHA-256 values.

| Gate | Actual result |
| --- | --- |
| [Android 138511694428](https://github.com/peterl-ai/IPQC-system/actions/runs/45413041721/job/138511694428) | 121 JVM tests passed, Lint and both APK assemblies passed |
| [Backend 138511695156](https://github.com/peterl-ai/IPQC-system/actions/runs/45413041721/job/138511695156) | Release build passed, 1,186 tests passed, 766 opt-in PostgreSQL tests skipped |
| [PostgreSQL 138511695507](https://github.com/peterl-ai/IPQC-system/actions/runs/45413041721/job/138511695507) | Release build passed on PostgreSQL 17.12, 766 tests passed |
| [Web 138511695262](https://github.com/peterl-ai/IPQC-system/actions/runs/45413041721/job/138511695262) | Lint, 166 tests in 23 files, typecheck and production build passed |

Baseline APK artifact IDs are `14932175388` (debug) and `14931390530` (instrumentation).
The downloaded APKs both declare min SDK 30 and target SDK 36. The debug DEX declares the
sample workbench flag false and an empty `SERVER_URL`.

## Server contract

The [Rev3.2 architecture](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md)
and [R32-05 RemoveItem contract](21_R3205_ITEM_REMOVED.md) govern this slice. An authorized IPQA
records explicit Production or Equipment confirmation of absence or non-applicability for one
specified frozen item and its current attempt. The optional API `reason` permits 500 UTF-16 code
units and rejects control characters. Null and empty remain distinct. The app retains the
frozen item and minimum, original samples and contributor history.

Authenticated `GET /api/line-patrol/tasks/{taskId}/shared-draft` provides exact current attempt
identities, dispositions, accepted removal receipts, sample observations and DraftVersion.
The client verifies the matching task-detail scope and pairs each typed receipt to its current
item/attempt and minimum. `GET /shared-draft/item-dispositions` describes current states and
paginates UnableToMeasure history. It cannot list previously unseen historical RemoveItem
receipts. This client keeps every removal receipt it has observed for an attempt that later
becomes historical. Local command history and samples remain available under their existing
authorization. No missing history or acceptance is inferred.

The server `POST /item-removal-batches` contract takes one `RemoveItem` operation and its
exact task, line, environment, workflow, expected DraftVersion and original source proof.
The item V1 canonical format uses the existing .NET-compatible UTF-8 encoder and the
`item-removal` domain. It binds the operation and scope, contributor WorkContext and assertion,
base and origin local-edit versions, affirmative confirmation, source and nullable reason.
The `item-removal-batch` fingerprint binds the exact operation hash, batch ID and expected
version to that scope. There are no sample IDs or dependency lists in these formats. The
server uses one task-level CAS and records an accepted V2 batch receipt with an empty sample
operation list and one typed item-removal receipt.

## Local admission, persistence and progress

The existing A03-05 explicit online activation arms the protected profile. It requires a
validated task detail, current authenticated shared-draft snapshot, an eligible A02 receipt,
contribution permission and a verified task-scoped WorkContext. Creation freezes the command
ID, source, owner/family, scope and expected DraftVersion. Save rechecks those values, the
current applicable attempt, known source revocation, the original capture window and
conflicting local intent inside the protected transaction. The final precommit admission
rechecks the same A02 identity and deadline. A pending command cannot rebase silently.
A legacy profile can still serve sample work. Its item removal path needs a fresh typed,
matched online observation before it accepts a new command.

The original Room transaction stores the item row, advances the shared local edit sequence
and claims the OperationId across both typed domains and task scopes. Every row uses the
existing independent Keystore work key. The typed item ledger and the existing sample rows
have one catalog and share the same transaction, operation ID namespace and per-task batch
barriers. No Room schema change or second database is needed. Exact retries under the same
eligible A02 identity return the original command. A changed payload, contributor or scope
under that ID cannot replace the original. A lost acknowledgement or rolled-back write is
resolved by an exact retry, including after a process restart.

The item projection retains the last authorized server observation, all observed samples and
the local operation history. It exposes an explicit provisional removal and a separate local
sample requirement for an applicable attempt when a local command still matches its basis.
A matching authenticated observation is separately visible as server evidence. Newer
receipts require a server refresh before new preparation against an older snapshot. The
projection retains historical attempts, conflicts and known quarantines. A queued command
and an accepted server disposition stay separately visible even when their IDs match.
The provisional and server-observed all-Removed indicators both leave Formal Submit and PQE
confirmation to their own workflows.

## Batch preparation and A/B

An eligible creator can seal a queued command locally for the exact observed version. The
protected manifest points to the original proof row and binds a single immutable operation.
The creator may preserve and reconcile a saved operation after its capture window closes.
A current authorized B can use the explicit source preflight for the saved A command and can
then seal the exact operation against a fresh matched task observation. Source proof and
current uploader authorization serve different purposes. B's new operations require B's own
WorkContext. A's normal logout clears A's login material and leaves the original protected
operation, capture evidence and provenance. This phase adds no item-removal POST caller.

A task-wide in-flight or unknown sample or item batch blocks new preparation or a new
attempt. Exact retrieval of an already sealed manifest preserves its recorded status.
The foundation has explicit durable InFlight and OutcomeUnknown transitions for a
preflight-gated attempt. B's authenticated GET of a known batch or operation uses the
existing receipt routes. The client binds acceptance to the saved content hash, scope and
manifest. A single operation receipt supplies evidence. The exact full V2 batch receipt
settles the batch. A 404, mismatched receipt or failed request leaves uncertainty in place.

## Sample workbench navigation

While a sample save has an unresolved acknowledgement, the toolbar Back and Switch line
controls are disabled and system Back stays on the workbench. The view model enforces the
same check for delivered navigation callbacks. Check local save or Retry same save still
uses the original command. The uncommitted form remains in memory. Sign out continues to
end the personal session and protected committed data remains in Room.

## Limits

The accepted A02 ordinary reboot policy applies. Detectable clock rollback or inconsistent
progress locks access. Undetectable manipulation of post-reboot device UTC remains an
accepted limitation. A WorkContext establishes scoped provenance and the server records the
actual uploader. Exact offline capture time and independent physical authorship are outside
the WorkContext guarantee.

Production item-removal controls, UnableToMeasureNG editing, NG fields, PIC, photos,
background upload, Formal Submit, PQE, rollout and physical Samsung UAT remain deferred.

## Local verification

Windows JDK 17 and Android SDK 36, from `android/patrol-app`:

```powershell
.\gradlew.bat --console=plain --max-workers=1 --no-daemon --no-watch-fs --offline '-PIPQC_ENABLE_SAMPLE_WORKBENCH=false' '-PIPQC_SERVER_URL=' :app:testDebugUnitTest :app:lintDebug :app:assembleDebug :app:assembleDebugAndroidTest
```

The JVM XML reports record 129 passed, zero failures, zero errors and zero skips.
Lint has zero errors and the two existing `KaptUsageInsteadOfKsp` and `UnusedAttribute`
warnings. Both APK assemblies passed. The eight added JVM cases include byte comparison
against the checked-out backend C# canonicalizer's operation hash
`3DFADD7AC7E4A9FFC04B9EFACE971CB3C87701F3F72BE23499FF33905232A9C6` and batch fingerprint
`9FA15D163EFF40BAA6C5EA4E0819E95292AC76B63B205CD1121F6F79EFD74644`.

The same app APK `e2303c931f4b6a77b61a0680c50a02fbf74fca1193f6ce705a52f99bf8e9b805`
and instrumentation APK `2a530f29cfa9c7e88152a452a88222cf8d34206b493d7806df7be1cd348b03a5`
ran with each emulator's actual API verified:

```text
adb -s <serial> shell am instrument -w -r --no-window-animation -e ipqcWorkbenchEnabled false com.jaxpower.ipqc.patrol.test/androidx.test.runner.AndroidJUnitRunner
```

| Device | Result |
| --- | --- |
| API 30 emulator | 41 passed in 79.464 seconds, runner code -1 |
| API 35 emulator | 41 passed in 147.572 seconds, runner code -1 |

The new emulator cases use real Room, Android Keystore and Compose with synthetic server
responses. They cover source A through offline restart and B's explicit reconciliation, clock
expiry and rollback, interrupted transactions and lost acknowledgement, and the navigation
lock until exact local resolution. The JVM HTTP tests check B's current bearer on the receipt
GET and the distinction between a 404, individual evidence and full batch acceptance.
Factory HTTPS end-to-end and physical-device UAT were outside these runs.
