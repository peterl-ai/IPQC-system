# Android Patrol development and testing on Windows

Open `android/patrol-app` as a standalone Android Studio project. This guide covers the debug build and the two emulator API levels used to check [A01](34_ANDROID_V1_A01_ONLINE_PATROL_FOUNDATION.md).

## Install the Windows toolchain

1. Install a stable Android Studio version that supports Android Gradle Plugin 8.13.2. Run the Windows installer and complete the setup wizard. [Android Studio installation](https://developer.android.com/studio/install) · [AGP compatibility](https://developer.android.com/build/releases/agp-8-13-0-release-notes)
2. Install a JDK 17 distribution or download JDK 17 from the Gradle JDK picker in Android Studio. The checked-in wrapper uses Gradle 8.13.
3. Open **Tools > SDK Manager**. Under **SDK Platforms**, install **Android 16 (API 36)**. Under **SDK Tools**, enable **Show Package Details** and select **Android SDK Build-Tools 35.0.0**, **Android SDK Platform-Tools**, and **Android Emulator**. Install **Android SDK Command-line Tools** if you will use `sdkmanager` from PowerShell.
4. Download Android 11/API 30 and Android 15/API 35 system images using **Device Manager** or the SDK Manager. Choose a tablet profile and an image compatible with the host CPU. Google APIs x86_64 images suit an x64 Windows host.

For PowerShell, point `JAVA_HOME` and `ANDROID_HOME` at the same JDK and SDK installations selected in Studio. Replace the JDK path and use the SDK location displayed by SDK Manager if it differs from the default.

```powershell
$env:JAVA_HOME = 'C:\Path\To\jdk-17'
$env:ANDROID_HOME = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
$adb = Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe'
& "$env:JAVA_HOME\bin\java.exe" -version
& $adb version
```

## Open the project and sync

1. In Android Studio, choose **Open** and select `android/patrol-app` inside the checkout. This is the directory containing `settings.gradle.kts`.
2. Open **File > Settings > Build, Execution, Deployment > Build Tools > Gradle**. Select **JDK 17** as the Gradle JDK. Use the project’s Gradle Wrapper.
3. Confirm the Android SDK path when prompted and choose **File > Sync Project with Gradle Files**. Wait for the `app` module and its tasks to load. Studio may create `local.properties` for its SDK path. Keep that file local. [Gradle JDK configuration](https://developer.android.com/build/jdks)

## Build and check both APKs

Open PowerShell at the repository root. Run the four tasks from `android/patrol-app` and check that each exits successfully.

```powershell
Set-Location .\android\patrol-app
.\gradlew.bat --version
.\gradlew.bat :app:testDebugUnitTest
.\gradlew.bat :app:lintDebug
.\gradlew.bat :app:assembleDebug
.\gradlew.bat :app:assembleDebugAndroidTest
```

| Output | Path relative to the repository root |
| --- | --- |
| App APK | `android/patrol-app/app/build/outputs/apk/debug/app-debug.apk` |
| Instrumentation test APK | `android/patrol-app/app/build/outputs/apk/androidTest/debug/app-debug-androidTest.apk` |
| JVM test report | `android/patrol-app/app/build/reports/tests/testDebugUnitTest/index.html` |
| Debug lint report | `android/patrol-app/app/build/reports/lint-results-debug.html` |

Use SDK tooling to confirm application ID `com.jaxpower.ipqc.patrol`, `minSdk 30`, and `targetSdk 36`. Run this from `android/patrol-app` after assembling the APK.

```powershell
$aapt = Join-Path $env:ANDROID_HOME 'build-tools\35.0.0\aapt.exe'
$apk = '.\app\build\outputs\apk\debug\app-debug.apk'
& $aapt dump badging $apk | Select-String '^(package:|sdkVersion:|targetSdkVersion:)'
```

## Configure an approved development HTTPS origin

`IPQC_SERVER_URL` is a Gradle project property that is compiled into the Android build. For a one-off developer build, run this from `android/patrol-app`. Replace the reserved example hostname and port with the approved development origin.

```powershell
.\gradlew.bat '-PIPQC_SERVER_URL=https://ipqc-dev.example.invalid:8443/' :app:assembleDebug
```

For repeated Studio runs, set `IPQC_SERVER_URL` in `gradle.properties` under the local Gradle user home, typically `%USERPROFILE%\.gradle`. For a project-specific shell, set `ORG_GRADLE_PROJECT_IPQC_SERVER_URL` before invoking the wrapper. Rebuild and reinstall after changing the setting. Keep endpoint settings local and use authorized test accounts. A valid origin has HTTPS, a hostname, an optional port and an optional trailing slash. Use the origin without application paths, query parameters, fragments or credentials.

Check DNS and network access from the emulator as well as from Windows. Inside an Android Studio emulator, `localhost` is the emulator and `10.0.2.2` reaches the Windows host loopback. The development URL must still match its certificate and use a chain trusted by the app on Android. If the dev host lives on Windows, use an approved reachable name and network route. A workstation can trust certificates that a device does not. Keep certificate and hostname checks enabled. [Emulator networking](https://developer.android.com/studio/run/emulator-networking-address) · [Android certificate trust](https://developer.android.com/privacy-and-security/security-config)

CI builds with an explicitly empty origin. The downloaded app can be installed and launched and displays **A valid IPQC HTTPS server must be configured.** Use a locally rebuilt APK with an approved HTTPS origin to exercise online sign-in and read-only patrol. The application has no runtime URL setting.

## Create and test the two tablet emulators

| AVD | System image | Compatibility target |
| --- | --- | --- |
| IPQC Tablet API 30 | Android 11, API 30 | Galaxy Tab A7 SM-T500 |
| IPQC Tablet API 35 | Android 15, API 35 | Galaxy Tab A9+ SM-X210 |

In **View > Tool Windows > Device Manager**, select **Create Virtual Device**, choose a tablet profile such as **Pixel C** if available, and select the corresponding image. Name both AVDs, finish setup, and launch one. The Android system images exercise the target API levels. Hardware, Samsung firmware and One UI require separate physical-tablet UAT. [Create an AVD](https://developer.android.com/studio/run/managing-avds)

In Studio, select the booted AVD and the `app` run configuration, then choose **Run**. Cycle through portrait and landscape with the emulator rotate buttons. Check that controls and content remain reachable. With an approved development build and test account, exercise sign-in, line selection, task list and read-only detail. Repeat on the second AVD.

To install artifacts manually, target the serial from `adb devices -l`. Replace the sample serial before running the install. The commands below assume PowerShell is in `android/patrol-app` for local builds.

```powershell
& $adb devices -l
$serial = 'emulator-5554'
& $adb -s $serial shell getprop ro.build.version.sdk
& $adb -s $serial install -r .\app\build\outputs\apk\debug\app-debug.apk
& $adb -s $serial shell am start -n com.jaxpower.ipqc.patrol/.MainActivity
```

For a ZIP downloaded from CI, use the extracted path instead. Use the app APK and instrumentation APK from the **same run** when running tests. Windows and separate CI jobs can use different debug certificates. If you see `INSTALL_FAILED_UPDATE_INCOMPATIBLE`, use a fresh test AVD or the build signed with the already-installed test app’s key. Retain installed data where needed.

To let Gradle install the APKs and run instrumentation, connect only the intended AVD, then repeat with the other AVD.

```powershell
.\gradlew.bat :app:connectedDebugAndroidTest
```

To run the downloaded CI pair, install both extracted APKs and invoke the test runner by serial.

```powershell
& $adb -s $serial install -r .\app-debug.apk
& $adb -s $serial install -r .\app-debug-androidTest.apk
& $adb -s $serial shell am instrument -w -e package com.jaxpower.ipqc.patrol com.jaxpower.ipqc.patrol.test/androidx.test.runner.AndroidJUnitRunner
```

## Debug and troubleshoot

| Symptom | What to check |
| --- | --- |
| Gradle Sync fails or complains about Java | Check JDK 17 in **Gradle JDK** and `.\gradlew.bat --version` in PowerShell. Use the repository wrapper, check network access to Gradle and dependency repositories, and retry sync. |
| Required SDK package missing | Confirm Studio and PowerShell use the same SDK. In SDK Manager install API 36 and Build-Tools 35.0.0 with **Show Package Details**. |
| Windows fails to move a Gradle transform workspace | Stop the wrapper’s Gradle daemons with `.\gradlew.bat --stop`. Retry the same tasks with `--max-workers=1 --no-watch-fs`. [Gradle workspace issue](https://github.com/gradle/gradle/issues/31438) |
| Emulator will not boot | Check Windows virtualization and supported hypervisor availability. Invoke `emulator.exe -accel-check` from the SDK emulator folder. In Device Manager, try a **Cold Boot** and confirm the system image ABI matches the workstation. [Emulator acceleration](https://developer.android.com/studio/run/emulator-acceleration) |
| Install, runner or device is missing | Run `adb devices -l` using the SDK Platform-Tools copy. Confirm the selected serial is online, the OS reports API 30 or higher, and both APKs are from the same build. |
| App reports a configuration, connection or TLS error | Check the configured origin and reinstall the rebuilt APK. Verify the emulator can resolve and reach the HTTPS host and that the device trusts the certificate chain. Inspect sanitized status codes and timings in approved development API logs. |

In Studio, set a Kotlin breakpoint in `MainActivity.kt` or near the relevant UI-state transition, select the **debug** variant and choose **Debug** for the `app` run configuration. Open **View > Tool Windows > Logcat**, select the target device and filter with `package:com.jaxpower.ipqc.patrol`. Use synthetic data and inspect only the relevant errors. Keep passwords, access and refresh tokens, authorization headers, and request and response bodies out of log output. [Android Studio debugger](https://developer.android.com/studio/debug) · [Logcat](https://developer.android.com/studio/debug/logcat)

## Optional LDPlayer smoke test

Use the instance’s documented ADB setting if your LDPlayer edition exposes one. With a single attached device, check:

```powershell
adb devices -l
adb shell getprop ro.build.version.sdk
```

For several devices, pass `-s <serial>` to target the intended instance. Install the application APK only on an authorized instance reporting API **30 or higher**. Android 9/API 28 editions, including the documented Android 9 build of LDPlayer 9, cannot install this app. Editions vary in their Android base and ADB setup. Use the API 30/API 35 Android Studio emulator matrix as the primary test environment. [LDPlayer 9 Android base](https://www.ldplayer.net/support/ldplayer9-faster-higher-smother-android-emulator.html)

## Download Debug APKs from GitHub Actions

1. Open the repository’s **Actions** tab and select **CI**.
2. Open a successful run for the commit under test. Verify the event, branch or PR, and full commit SHA. In a PR run, use the Android job checkout log to identify the tested GitHub merge ref if it differs from the PR branch HEAD.
3. In the run summary’s **Artifacts** section, download `ipqc-android-debug-apk`. For instrumentation, also download `ipqc-android-instrumentation-apk` from that same run.
4. Extract the ZIPs. The first contains `app-debug.apk` and the second contains `app-debug-androidTest.apk`. Use the ADB steps above for the approved emulator or test tablet.

The run page and artifact links identify the exact workflow run. Availability depends on repository read access and artifact retention. Check the Android job and artifact list when an expected file is missing. The APKs use ephemeral CI debug signing and are for testing. [GitHub artifact download](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts)
