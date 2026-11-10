# 08 — Android Device Target

## Current plant tablet inventory

Reported on-site inventory, corrected by Peter and incorporated into approved Rev3.2:

| Line | Model | OS | Reported patch |
| --- | --- | --- | --- |
| 1 | Samsung Galaxy Tab A9+ / SM-X210 | Android 15 / API35, One UI 7.0 | 2025-07-01 |
| 2 | Samsung Galaxy Tab A7 / SM-T500 | Android 11 / API30, One UI Core 3.1 | 2022-04-01 |

IT must assess updates/security before production use, especially the older Line 2 patch. IP/MAC/SSID/model are not authenticated identity. Endpoint, TLS trust and actual reachability remain PENDING external verification. See [approved Rev3.2](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md).

## Compatibility requirement
The future IPQA Android application must run correctly on Android 11 and must not depend exclusively on newer Android APIs.

Recommended development constraint when Android work begins:
- retain `minSdk 30` in one codebase/APK for both models
- compile/target with the then-current supported SDK while retaining Android 11 runtime compatibility
- test on both physical tablets independently before release; OS versions need not match

## App language
English only.

## Planned Line Patrol capabilities
- personal login, server-backed line selection and task list (Rev3.2; future implementation)
- task details
- inspection form
- numeric/qualitative values
- photo capture/upload
- save draft / continue
- submit to PQE
- rejected-task notification/state
- rejection reason display
- correction/reinspection
- resubmit

## Material Verification
Reserve an Android home-screen icon/entry for future Material Verification. Do not implement its workflow in the current project scope.
