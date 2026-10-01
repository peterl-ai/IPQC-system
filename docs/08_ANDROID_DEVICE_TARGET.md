# 08 — Android Device Target

## Current reference tablet
Observed from plant device screenshots:
- Device family: Samsung Galaxy Tab A7
- Model number: SM-T500
- Android version: 11
- One UI Core: 3.1

## Compatibility requirement
The future IPQA Android application must run correctly on Android 11 and must not depend exclusively on newer Android APIs.

Recommended development constraint when Android work begins:
- choose a `minSdk` compatible with Android 11 (API 30) or lower
- compile/target with the then-current supported SDK while retaining Android 11 runtime compatibility
- test on the physical SM-T500 tablet before release

## App language
English only.

## Planned Line Patrol capabilities
- assigned task list
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
