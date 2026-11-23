# Android V1 A04-01 — Feature-Gated Offline Sample Workbench

## Verified base

PR #41 merged approved head `39a0b4819b20ffef5aaf261272e3406dba0676db` at main
`e71177b70451c95e824b639ed7ba164691a24126`. The merge tree
`6db3060329c166df26980a0b5293f8b789bc2fcd` equals the independently approved source tree.
All four actual logs in [merged-main run 45265139545, attempt 1](https://github.com/peterl-ai/IPQC-system/actions/runs/45265139545/attempts/1) were checked before implementation.

| Gate | Actual result |
| --- | --- |
| [Android 138009963726](https://github.com/peterl-ai/IPQC-system/actions/runs/45265139545/job/138009963726) | 121 JVM tests passed, Lint and both APK assemblies passed |
| [Backend 138009962838](https://github.com/peterl-ai/IPQC-system/actions/runs/45265139545/job/138009962838) | Release build, 1,186 passed and 766 opt-in PostgreSQL tests skipped |
| [PostgreSQL 138009963396](https://github.com/peterl-ai/IPQC-system/actions/runs/45265139545/job/138009963396) | PostgreSQL 17.12 and 766 tests passed |
| [Web 138009963558](https://github.com/peterl-ai/IPQC-system/actions/runs/45265139545/job/138009963558) | Lint, 166 tests across 23 files, typecheck and production build passed |

Baseline APK artifact IDs are `14861349187` (debug) and `14863280861` (instrumentation).
The 22 protected files in the original Windows checkout matched the SHA-256 manifest.

## Run the development pilot

Set the Gradle property `IPQC_ENABLE_SAMPLE_WORKBENCH=true` for a local Debug build only.
From PowerShell in `android/patrol-app`, substitute your approved HTTPS origin:

```powershell
.\gradlew.bat '-PIPQC_ENABLE_SAMPLE_WORKBENCH=true' '-PIPQC_SERVER_URL=https://your-approved-test-origin/' :app:assembleDebug
```

In Android Studio, open `android/patrol-app`, select the **debug** variant, and supply the same two properties in a local Gradle run configuration. Run with JDK 17 and Android SDK 36. Use a valid plant/test account and the existing HTTPS trust policy. The release variant and GitHub Actions debug builds always set the pilot flag to false. CI explicitly passes a blank `SERVER_URL`. The flag defaults to false, has no settings toggle, and leaves A01/A02 read-only navigation in place. The normal app window remains protected by `FLAG_SECURE`.

## Navigation and controls

The pilot opens from the currently loaded task detail. The selected task, configured line, environment, workflow and personal session come from the authorized task/detail response. On wide screens the item and progress list sits beside the selected item's frozen definition, editor, current samples and history. A narrow screen uses a horizontal item selector and a vertically scrolling detail pane. While the keyboard is visible, the task header uses less space. Status and selection have text or accessibility semantics.

Opening or explicitly reloading online obtains a current authenticated task and shared draft. Before activation, confirmed server values and counts are read-only. **Activate local capture** is an explicit online action that obtains or verifies the current operator's WorkContext and arms the approved protected A03-05 profile. An existing eligible profile can be reopened offline from its authorized cached task. Loss of network keeps the current workbench only when the protected offline receipt matches the same operator and personal session. Reconnect uses explicit online verification before new online activation.

The frozen attempt determines whether the operator enters OK/NG or an exact decimal with an optional keyboard-wedge barcode. Quantitative judgment comes from a server-accepted value when available. The editor displays frozen limits for reference. Each current attempt shows its real applicable and required count. `Count met` describes the measured sample count. The separate observed server DraftVersion and local edit sequence show which snapshot and local operations the tablet has.

## Local saves and recovery

The workbench sends the current task/line and expected personal session to the existing A03-05 coordinator. The coordinator independently resolves protected identity, applicability, source, capture deadline and final Room admission. It uses the same canonical sample format and durable ledger. A new saved operation receives its original ID once. **Saved locally** appears after durable success or a read of an exact matching stored command. The draft then refreshes locally. An ambiguous storage acknowledgement offers **Check local save** and **Retry same save** with the original command. The typed input and unacknowledged command stay in the current in-memory view model. A fresh process reopens any durable operation from the protected ledger.

A correction requires the exact current eligible predecessor. **Remove sample** requires confirmation and records a tombstone, preserving the earlier operation and lowering the applicable count. It is distinct from the task-item Removed disposition. History labels pending sync, in flight, outcome unknown, accepted, conflict, quarantine and historical/stale attempts without promoting pending values to server acceptance.

The workbench locks new input when its WorkContext expires while eligible local history remains visible. An expired, missing or inconsistent A02 admission, logout, or a different personal session also removes protected data from the view. The same underlying Room ledger retains A's work. B sees only authorized server data and local work allowed by the current B scope, and B's new measurements require a B WorkContext. Switching lines preserves each task's original scope.

## Scope and limits

This pilot is limited to sample capture, correction, removal and read-only inspection of local status. Background sync, automatic upload, Formal Submit, PQE actions, reinspection, photo workflows, Material Verification, NG Type/Reason/Remarks, PIC and task-level dispositions are deferred. The accepted Rev3.2 limitation remains when post-reboot UTC manipulation leaves no detectable inconsistency. Physical Samsung SM-T500/SM-X210 UAT and factory HTTPS E2E are separate validation.

## Emulator screenshots

The API 35 opt-in test captured the scrollable workbench after saving synthetic measurements. The actual production Activity retains secure window protection.

- [Landscape two-column workbench](verification/a04-01/workbench-landscape.jpg)
- [Portrait workbench with horizontal item selection](verification/a04-01/workbench-portrait.jpg)


## Local verification

Windows JDK 17 and Android SDK 36, from `android/patrol-app`:

```powershell
.\gradlew.bat --console=plain --max-workers=1 --no-daemon --no-watch-fs --offline '-PIPQC_SERVER_URL=' :app:testDebugUnitTest :app:lintDebug :app:assembleDebug :app:assembleDebugAndroidTest
.\gradlew.bat --console=plain --max-workers=1 --no-daemon --no-watch-fs --offline '-PIPQC_SERVER_URL=' '-PIPQC_ENABLE_SAMPLE_WORKBENCH=true' :app:assembleDebug :app:assembleDebugAndroidTest :app:assembleRelease
```

Both commands exited 0. The default build passed 121 JVM tests with zero errors, failures or skips. Lint reported zero errors and the existing `UnusedAttribute` and `KaptUsageInsteadOfKsp` warnings. Both APKs assembled. Actual APK metadata showed min SDK 30, target SDK 36 and empty `SERVER_URL`. The default Debug DEX flag was false. The second invocation generated an opt-in Debug DEX flag of true and a Release DEX flag of false.

The same default APK pair passed all 37 instrumentation tests on API 30 (124.136 seconds) and API 35 (214.212 seconds), after running
`adb -s <serial> shell am instrument -w -r --no-window-animation -e ipqcWorkbenchEnabled false com.jaxpower.ipqc.patrol.test/androidx.test.runner.AndroidJUnitRunner`.
The app APK SHA-256 was `c29291c813a502bf1374c2b3cfa4407fb66164e0ec83d6ef1c9800e0e6137ccf`, and the test APK hash was `d076e89cd067e731d96e6dd98cb3c2c4d96031b57f1bc5cb460f54690e3d402a`.
Both device runs exited 0 with runner code `-1`.

The final opt-in APK pair passed all nine pilot tests on API 35 (222.537 seconds) with
`-e class com.jaxpower.ipqc.patrol.SampleWorkbenchTest -e ipqcWorkbenchEnabled true -e ipqcWorkbenchScreenshots true`.
The opt-in app APK SHA-256 was `f2b3fc16339408318412fa563eea0877ac1b1478b337f815e532b5645383d5f4`, and the test APK hash was `1a9463c8697243c845d649675d584a84b4e9d921ea886b21f2b4076a0a05b9dc`.
Release SHA-256 was `0c217d3d44a7b2355dff00ba692cbbec12ace88c391b6865538d90df082e82d8`.

The nine new instrumentation cases exercise actual Compose, the A03 coordinator, Room and Android Keystore using synthetic authorization, task and draft responses. The tests exercise typed values, no production route in the disabled UI, exact scoped recovery, work and receipt expiry, clock faults, operator transition, two lines and persisted batch statuses. The external review package pins both final-HEAD Actions runs, the eight actual job links and the flag evidence from the public APKs.
