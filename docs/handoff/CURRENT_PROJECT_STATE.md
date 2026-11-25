# Current IPQC project state

**Verified 2026-11-25 · Project Memory integration merged · Foreground item-removal transport in progress**

PR #44 is merged. Independent ChatGPT issued **PASS — R44-P3-01 VERIFIED CLOSED** at the exact reviewed revision. The authorized Android slice is foreground transport for one prepared RemoveItem batch on `codex/android-v1-a04-03-item-removal-batch-transport`. A04-03 is the new working label for this authorization. The prior approved roadmap had no exact successor designation. Peter retains final merge and deployment authority. [Independent re-review and merge](REVIEW_HISTORY.md#2026-11-25-final-independent-re-review-of-pr-44)

## Product and milestone

JAX Power IPQC replaces the Line Patrol portion of legacy GQMS for new operations. Web/API v1 (#1–8) and the Rev3.2 personal authorization, LineShared scheduling and draft/evidence foundations (#9–33) are merged. Android A01 through A04-02 (#34–43) is merged. The public app remains read-only by default. A04-02 adds protected local RemoveItem preparation and receipt recovery. Production removal UI and mutation transport remain deferred.

| Verified Git / GitHub field | Value |
| --- | --- |
| Latest merge, live main at preflight and transport Base | `cddea323146ec3685b91a941fbc31fa87becdeb2` · [PR #44 Project Memory integration](https://github.com/peterl-ai/IPQC-system/pull/44) · merged 2026-11-25 |
| Merged-main Tree | `eeaf2bea0f903136e3c4a8b95ce423feb96ea559` |
| PR #44 independently reviewed HEAD and verdict | `273a0631b10652c5968e5f7adff8e5dbb458d3d3` · **PASS — R44-P3-01 VERIFIED CLOSED** |
| Android A04-02 merge | `cadd0973795aaecd4143c96de44ffd80f2ed356a` · [PR #43](https://github.com/peterl-ai/IPQC-system/pull/43) · merged 2026-11-24 |
| PR #43 approved Base | `4c74c1000c7c30997c4877dcfb693b354d80fba5` |
| PR #43 independently reviewed HEAD | `a0050619fb74226eb72ccac2d22da140a9fefbd2` |
| PR #43 reviewed Tree | `a8522ab82078837bc57604a9e7e84dd0bedfa85d` |
| PR #43 pre-merge CI checkout | `3a87c3c883accbbd102840b02a229143c98d5310` · synthetic merge with approved Base and HEAD as ordered parents |
| PR #43 independent verdict | 2026-11-24 · **PASS — R43-P2-02 VERIFIED CLOSED**. Earlier P2/P3 remain closed. No new actionable P1/P2/P3 in the inspected repair and adjacent scope. |
| Project Memory integration | Complete at the verified PR #44 merge. |
| Current application scope | A04-03 working label · explicit internal foreground upload, strict receipt settlement and recovery. |

The actual PR #43 merge has the approved Base and reviewed HEAD as its ordered parents and exactly matches the reviewed Tree. [Independent review disposition and verification limits](REVIEW_HISTORY.md#2026-11-24-final-independent-re-review)

## Verified evidence

Codex inspected all four actual job logs on the exact PR #44 merged-main checkout. The earlier 46 API 30 and 46 API 35 instrumentation passes were PR #43 developer evidence. [PR #44 merged-main CI 45642806832, attempt 1](https://github.com/peterl-ai/IPQC-system/actions/runs/45642806832/attempts/1)

| Merged-main job | Actual result |
| --- | --- |
| [Android 139305709689](https://github.com/peterl-ai/IPQC-system/actions/runs/45642806832/job/139305709689) | 129 JVM tests passed. Android Lint passed with zero errors and both APKs assembled. |
| [Backend 139305708550](https://github.com/peterl-ai/IPQC-system/actions/runs/45642806832/job/139305708550) | Release build passed. 1,187 passed, 767 opt-in PostgreSQL tests skipped. |
| [PostgreSQL 139305709067](https://github.com/peterl-ai/IPQC-system/actions/runs/45642806832/job/139305709067) | Release build passed with PostgreSQL 17.12. 767 passed, zero skipped. |
| [Web 139305709078](https://github.com/peterl-ai/IPQC-system/actions/runs/45642806832/job/139305709078) | Lint, 166 tests in 23 files, typecheck and production build passed. |

The prior exact-HEAD Push and PR CI are retained separately. Prior developer emulator evidence records 46 instrumentation passes each on API 30 and API 35 using the same repaired APK pair. The independent reviewer inspected archived results and live metadata. APK binary and protected original-checkout verification came from developer evidence. [Push CI](https://github.com/peterl-ai/IPQC-system/actions/runs/45473753327/attempts/1) · [PR CI](https://github.com/peterl-ai/IPQC-system/actions/runs/45473771848/attempts/1) · [A04-02 contract and developer evidence](https://github.com/peterl-ai/IPQC-system/blob/a0050619fb74226eb72ccac2d22da140a9fefbd2/docs/43_ANDROID_V1_A04_02_OFFLINE_ITEM_REMOVAL_FOUNDATION.md)

## Boundaries and action

- Preserve approved Rev3.2 personal HTTPS, server line selection, A02 ordinary read policy, WorkContext provenance and retained F2/F3 obligations. [Approved Rev3.2](../architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md) · [Rev3.2 supersession](../architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md)
- Physical Samsung SM-T500 and SM-X210 UAT, plant HTTPS, production infrastructure, backups, restore and deployment validation remain pending. Material Verification and broader MES integration stay deferred. [Open issues](OPEN_ISSUES.md)
- Peter has authorized the transport-only slice. Production RemoveItem UI, background upload, automatic retries and Formal Submit remain deferred. [Next Android implementation readiness](snapshots/IPQC_HANDOFF_THROUGH_PR43_2026-11-24.md#next-android-implementation-readiness)
