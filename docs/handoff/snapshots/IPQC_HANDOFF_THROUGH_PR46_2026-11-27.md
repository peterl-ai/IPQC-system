# IPQC handoff through the PR #46 merge

**2026-11-27 · A04-04 merged and verified · A04-05A is the next authorized working scope**

This snapshot records the verified state at the start of A04-05A. See the live Project Memory for later Draft revisions, checks and independent review. Peter retains final merge, release and deployment authority.

## Exact continuation

Live main and fetched origin/main were `1a30fe61a744b9282a20d18f92a521d6b59b9207` with Tree `b4268accd7f35d515513b305c4037e439ee29d38`. Ordered parents were `8120ace2ce5b4f458745dbf601f211960a2af4ca` and `5b6622c936bcca42bcdb5730166b394cac31d8e1`. The approved PR CI synthetic merge was `1db2cd1d48ed87816a292efc1a9dc68d4f49cfcd` with the same parents and tree. There were no open overlapping PRs at preflight. The clean isolated `codex/android-v1-a04-05a-basic-information-foundation` worktree started at the merge commit. Its active Draft and exact subsequent pins are recorded in the review handoff. [Draft by branch](https://github.com/peterl-ai/IPQC-system/pulls?q=is%3Apr+head%3Acodex%2Fandroid-v1-a04-05a-basic-information-foundation) · [PR #46](https://github.com/peterl-ai/IPQC-system/pull/46) · [Merged-main CI, attempt 1](https://github.com/peterl-ai/IPQC-system/actions/runs/46026722209/attempts/1)

| Completed PR | Verified merge and reviewed Tree | Final independent disposition |
| --- | --- | --- |
| [#44](https://github.com/peterl-ai/IPQC-system/pull/44) | `cddea323146ec3685b91a941fbc31fa87becdeb2` · `eeaf2bea0f903136e3c4a8b95ce423feb96ea559` | Project Memory integration, PASS, R44-P3-01 VERIFIED CLOSED. |
| [#45](https://github.com/peterl-ai/IPQC-system/pull/45) | `8120ace2ce5b4f458745dbf601f211960a2af4ca` · `7c473c37b5436079f7557314688e35420f3bddcd` | Foreground RemoveItem transport, PASS after the initial evidence-verification HOLD. |
| [#46](https://github.com/peterl-ai/IPQC-system/pull/46) | `1a30fe61a744b9282a20d18f92a521d6b59b9207` · `b4268accd7f35d515513b305c4037e439ee29d38` | Gated Item Removal Workbench, PASS after the focused repair, R46-P2-01 VERIFIED CLOSED. |

R43-P2-02 remains verified closed. PR #46 re-review found P1=0, P2=0, P3=0 in the focused scope. The reviewer verified exact GitHub revisions, scope, code and regression implementations, all eight actual final CI logs, Project Memory and four live archive digests. The local ZIP/TXT, screenshots, APK inner binaries, original reproduction logs and protected checkout were unavailable to independent review. Emulator and other local rerun evidence is developer evidence. [Review history](../REVIEW_HISTORY.md)

| Merged-main job | Result |
| --- | --- |
| [Android 140599904085](https://github.com/peterl-ai/IPQC-system/actions/runs/46026722209/job/140599904085) | JVM 139 passed, Lint passed, Debug and instrumentation APK assemblies passed. The matching PR #46 Tree's local Lint report has 0 errors and 2 existing warnings. |
| [Backend 140599904277](https://github.com/peterl-ai/IPQC-system/actions/runs/46026722209/job/140599904277) | Release build passed, 1,187 passed, 767 opt-in PostgreSQL skips. |
| [PostgreSQL 140599904438](https://github.com/peterl-ai/IPQC-system/actions/runs/46026722209/job/140599904438) | PostgreSQL 17.12, Release build passed, 767 passed, zero skipped or failed. |
| [Web 140599904229](https://github.com/peterl-ai/IPQC-system/actions/runs/46026722209/job/140599904229) | Lint, 166 tests, typecheck and production build passed. |

These merged-main results are from the actual merge checkout. PR #46 API 30/35 developer instrumentation had 60 passes apiece. Public Debug remains feature disabled and server URL blank. The original protected Windows checkout, Git index and all 22 saved file hashes matched before implementation.

## Approved direction and release limits

- Web/API v1 and Rev3.2 shared foundations are merged. The Android pilot uses personally authenticated, server-selected lines, supported API 30 and API 35 tablets, protected offline operations and explicit foreground transport. F1 source and uploader identities remain distinct. R43 task-version barriers and PR #46 removal projection/recovery protections apply. [Approved Rev3.2](../../architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md) · [Supersession](../../architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md)
- F2 exact Submit intent, query-only handoff, verified receipt/CAS and later PQE/reinspection remain required. F3 protected evidence references, retention, deletion orchestration and restore gates remain required. Production activation, both physical tablet UAT, plant HTTPS, production backups, restore and deployment are open. [Retained sign-off](../../architecture/IPQC_ANDROID_ARCHITECTURE_V1_IMPLEMENTATION_HANDOFF_2026-11-06.md) · [Open issues](../OPEN_ISSUES.md)
- The previously masked PR #46 CI matches were traced to the disposable test PostgreSQL fixture and the pinned setup-java action's environment-variable-name input. This check established no actual credential exposure in those four matches.

## Immediate authorized scope

A04-05A is the Android Basic Information foundation. Read optional Customer, Production and Product MO through the existing scoped GET. Preserve strict wire identity, V1 canonical operation/batch compatibility, original protected local capture/correction, shared version fences and restart replay. Header transport, ReceiptVersion 6 settlement and editable Workbench UI are subsequent scope. The Draft is separate from merged capability. [Backend contract](../../29_R3205_BASIC_INFORMATION_DRAFT.md) · [Roadmap](../PROJECT_ROADMAP.md)

Start with [AGENTS.md](../../../AGENTS.md), [root README](../../../README.md) and the [Project Memory index](../README.md). Follow the exact Draft review workflow, preserve historical snapshots, and verify this branch and its CI before relying on implementation status.
