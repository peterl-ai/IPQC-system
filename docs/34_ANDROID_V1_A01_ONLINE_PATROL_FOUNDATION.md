
# Android V1 A01 online patrol foundation

A01 introduces the first Android Patrol app under the approved Rev3.2 architecture. It runs one English-language APK on API 30 and higher with a two-column layout where screen width permits. Operators sign in personally, choose a configured line, and browse that line's tasks and frozen definitions while online.

## Verified baseline

PR #33 merged at `c71029c570c1616d4e37523c25a9cfe34ac5981e`. Its merged-main Push run passed before A01 implementation began. Backend passed 1,186 tests with 766 opt-in PostgreSQL skips and zero failures. The separate PostgreSQL 17.12 job passed 766 tests with zero skips or failures. Web passed 166 tests with lint, typecheck and production build successful. Backend Release builds reported zero warnings and errors. [PR #33](https://github.com/peterl-ai/IPQC-system/pull/33) · [Merged-main CI](https://github.com/peterl-ai/IPQC-system/actions/runs/44543806429)

## Project and toolchain

| Path under `android/patrol-app` | Responsibility |
| --- | --- |
| `app/src/main/java/com/jaxpower/ipqc/patrol/MainActivity.kt` | Application composition root, Activity, connectivity |
| `app/src/main/java/com/jaxpower/ipqc/patrol/api/` | Retrofit endpoint and wire definitions, HTTPS and response handling |
| `app/src/main/java/com/jaxpower/ipqc/patrol/auth/` | Session repository and Android Keystore refresh vault |
| `app/src/main/java/com/jaxpower/ipqc/patrol/tasks/` | Line directory and read-only task repository |
| `app/src/main/java/com/jaxpower/ipqc/patrol/ui/` | Compose screens, Navigation and StateFlow ViewModel |
| `app/src/test/`, `app/src/androidTest/` | TLS HTTP contract and state tests, device instrumentation |
| `gradle/wrapper/`, `app/build.gradle.kts` | Pinned build and dependencies |

Use JDK 17, Gradle 8.13, Android Gradle Plugin 8.13.2, Kotlin 2.2.21, Android SDK 36, build tools 35.0.0, `minSdk=30` and `targetSdk=36`. The Gradle distribution checksum is pinned in the wrapper properties. CI independently verifies the checked-in wrapper JAR checksum. CI invokes `sdkmanager` from the runner SDK directory. The initial Ubuntu 26.04 Push and PR jobs returned exit 127 because the SDK tool is installed outside `PATH`. [Android Gradle Plugin compatibility](https://developer.android.com/build/releases/agp-8-13-0-release-notes) · [Gradle release checksums](https://gradle.org/release-checksums/)

Compose BOM 2025.10.00 controls Material 3 and Compose UI/test versions. Activity Compose is 1.11.0, Lifecycle 2.9.4, Navigation 2.9.5, Coroutines 1.10.2, Kotlin serialization JSON 1.9.0, Retrofit/converter 3.0.0 and OkHttp 4.12.0. Tests use JUnit 4.13.2, MockWebServer and OkHttp TLS 4.12.0, AndroidX Test JUnit 1.3.0 and Runner 1.7.0.

## API and local authority

| Request | A01 contract |
| --- | --- |
| `POST /api/auth/login` | Sends username, password and `clientType: "Native"`. Checks the active IPQA role and read permission before verification. |
| `POST /api/auth/change-password` | Forced-change users only. Sends bearer and current/new passwords to existing server policy. HTTP 204 clears the old session and returns to online sign-in. |
| `POST /api/auth/refresh` | Sends the current native refresh token once. Checks user, environment, personal family and expiry before storing the rotated token. |
| `POST /api/auth/logout` | Clears local authority and the vault first. Sends the previous bearer and refresh token when available. A failed, unreachable or unattempted request is shown as unconfirmed remote revocation. |
| `POST /api/line-patrol/access/verify` | Checks version, user, family, environment and expiry against the authenticated session before admission. Receipt is used in memory for online validation. |
| `GET /api/line-patrol/lines` | Requires two valid configured lines. Displays server IDs internally and server names, codes and order in the UI. |
| `GET /api/line-patrol/tasks` | Sends the chosen server `lineId`, page and page size. Uses `total` to page and verifies current session, line and LineSharedV1 identity on returned rows. |
| `GET /api/line-patrol/tasks/{taskId}` | Sends `expectedTaskLineId` and validates the summary and frozen items. Maps line mismatch and unsupported snapshots to separate errors. |

The server definitions are in `AuthEndpoints.cs`, `AuthContracts.cs`, `PersonalAccess.cs`, `LinePatrolEndpoints.cs`, `LinePatrolContracts.cs` and `LinePatrolReadService.cs`. PostgreSQL persists timestamps to microseconds. Verification accepts exactly that precision loss from the initial .NET timestamp and retains the authoritative verified expiry. [Npgsql date and time handling](https://www.npgsql.org/doc/types/datetime.html)

Passwords, access tokens and new online receipts remain in memory. The vault stores the refresh token and its origin, user and family metadata as AES-256-GCM ciphertext in the application's no-backup directory. The encryption key is held by Android Keystore. App data is excluded from cloud backup and device transfer. A mutex serializes refresh. The old vault entry is destroyed before sending rotation, preventing replay after an ambiguous result or process death. A restarted process rotates and verifies online before opening Home. A fresh login after an unknown result is an intentional availability trade-off. Requests use standard HTTPS with system trust, without cookies, redirects or connection retries. Authentication POST bodies are one-shot.

## Screens and behavior

Sign-in or verified resume leads to Home. A forced password change stays in its own online flow. Home opens Patrol or displays the inert Material Verification entry. Patrol enters Line Selection, the selected line's paged task list, then read-only Detail. Detail shows source text for photo requirement and sample count and plant-local Eastern time. Switching lines obtains a fresh directory and starts on page 1 of the new filter. Back and manual refresh issue fresh reads where applicable. Loading, empty results, expired sessions, configuration failures, unavailable service and conflicts have distinct states. Loss of the default network clears visible tasks and requires an online resume or sign-in.

## Developer verification

Open `android/patrol-app` as a standalone Android Studio project. Select JDK 17 and install Android SDK Platform 36 and Build Tools 35.0.0. Set `IPQC_SERVER_URL` as a Gradle property for an authorized development HTTPS origin with a certificate trusted by the device. Leave the property unset for test and CI builds. The app reports a configuration error until a valid HTTPS origin is supplied.

From `android/patrol-app` use these commands on Windows. On Linux use `./gradlew` instead of `.\gradlew.bat`.

```powershell
.\gradlew.bat :app:testDebugUnitTest :app:lintDebug :app:assembleDebug :app:assembleDebugAndroidTest
.\gradlew.bat "-PIPQC_SERVER_URL=https://dev.example.invalid/" :app:assembleDebug
adb devices -l
adb install -r app/build/outputs/apk/debug/app-debug.apk
.\gradlew.bat :app:connectedDebugAndroidTest
```

Replace the reserved example origin before installing on an authorized test tablet. Confirm the device serial before targeting installation or connected tests. Debug signing is for development.

For emulator verification, both APKs were installed through ADB and run with `adb -s <emulator-serial> shell am instrument -w -r -e package com.jaxpower.ipqc.patrol com.jaxpower.ipqc.patrol.test/androidx.test.runner.AndroidJUnitRunner`.

The Windows run used `--console=plain --max-workers=1 --no-watch-fs`. The initial cold build hit a Gradle transform-cache rename failure. Retrying with serial workers completed the gates. [Gradle issue #31438](https://github.com/gradle/gradle/issues/31438)

| Verification | Result |
| --- | --- |
| Windows Android gates | Exit 0 for JVM tests, lint, debug APK and instrumentation APK. JVM 25 passed, 0 skipped, 0 failed. Lint 0 errors and 16 warnings about available dependency updates and the Android 13 locale attribute. APK manifest confirms minimum API 30, target API 36, and ARM64, ARMv7, x86 and x86-64 ABIs in the same APK. The v2 debug signature verifies with `apksigner verify --verbose --min-sdk-version 30`. |
| Local backend restore and Release build | `dotnet restore api/JaxPower.Ipqc.slnx` and `dotnet build api/JaxPower.Ipqc.slnx --configuration Release --no-restore` exit 0. Release had 0 warnings and 0 errors. |
| Local backend complete test gate | `dotnet test api/JaxPower.Ipqc.slnx --configuration Release --no-build`. The first complete Windows run exited 1 with 1,175 pass, 768 skip and 9 failures. Isolated targeted reruns passed all 46 cases with exit 0. The complete isolated rerun exited 0 with 1,184 pass, 768 skip and 0 fail. The skips comprise 766 PostgreSQL opt-in cases and 2 Linux-only cases. |
| Local Web `npm ci`, lint, test, typecheck and build | All exit 0. 166 tests in 23 files. |
| Android runtime instrumentation | Exit 0 on Android 11/API 30 and Android 15/API 35 tablet emulators, 5 passed on each. Covered Keystore corruption and deletion, Activity recreation during login, portrait/landscape widths, offline sign-in and the inert Material Verification card. |
| Samsung Galaxy Tab A7 SM-T500, Android 11/API 30 | Physical UAT NOT RUN. No connected unit. |
| Samsung Galaxy Tab A9+ SM-X210, Android 15/API 35 | Physical UAT NOT RUN. No connected unit. |

The first complete Windows run recorded seven photo fixture directory cleanup exceptions, one unavailable enrollment journal, and one CAS case with HTTP 500 where the test expected 409. The follow-up used dedicated `TEMP` and `TMP` paths and reran the affected cases. The initial Windows failure causes were not established. The backend source and test suite match the PR base.

## Deferred and production gates

A01 contains no inspection editing, shared drafts, WorkContext, offline task browsing, offline receipt enforcement, samples, photos, PIC, Material Verification integration, Formal Submit, PQE or reinspection. The existing server execution and submission limits remain false. Historical enrollment probe files stay outside the app. Live plant HTTPS acceptance, physical orientation and Keystore tests, and IT's assessment of the Android 11 tablet's OS patch level remain prerequisites for production use. Remote logout cannot be confirmed during network or ambiguous response failures.
