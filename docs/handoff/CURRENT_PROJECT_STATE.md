# Current IPQC project state

**Verified 2026-11-27 · A04-03 merged · A04-04 Open Draft under independent HOLD**

PR #46 remains an Open Draft. Independent ChatGPT issued **HOLD — P2 REPAIR REQUIRED**. Codex recorded R46-P2-01 as **FIXED — AWAITING INDEPENDENT RE-REVIEW**. [PR #46](https://github.com/peterl-ai/IPQC-system/pull/46) · [Review history](REVIEW_HISTORY.md#2026-11-27-initial-independent-review-of-pr-46)

Peter merged PR #45 after the final independent **PASS — APPROVED FOR MERGE**. Codex verified that the merge contains the exact reviewed HEAD and tree. The currently authorized scope is a development-pilot Workbench UI for the existing per-item removal, foreground transport and receipt workflow. A04-04 is the working label introduced by this authorization. Peter owns manual merge and deployment. [PR #45](https://github.com/peterl-ai/IPQC-system/pull/45) · [Final independent review](REVIEW_HISTORY.md#2026-11-25-final-independent-review-of-pr-45)

## Verified continuation

| Field | Verified value |
| --- | --- |
| Live merged main, local origin/main at preflight, A04-04 Base | `8120ace2ce5b4f458745dbf601f211960a2af4ca` |
| Reviewed and merged tree | `7c473c37b5436079f7557314688e35420f3bddcd` |
| Ordered parents | `cddea323146ec3685b91a941fbc31fa87becdeb2`, then reviewed HEAD `c6bb7dcf3103059f2d5d49069acf6d3ee2b632d0` |
| Active implementation | `codex/android-v1-a04-04-item-removal-workbench-ui` · [Draft review status by branch](https://github.com/peterl-ai/IPQC-system/pulls?q=is%3Apr+head%3Acodex%2Fandroid-v1-a04-04-item-removal-workbench-ui) |
| Prior verified findings | R43-P2-02 and R44-P3-01 remain verified closed. |
| Project Memory integration | Complete at PR #44 merge. [PR history](PR_HISTORY.md) |

Codex inspected all four actual logs at the exact merge checkout. PR #45's earlier API 30/35 instrumentation passed 48 tests apiece and remains prior developer evidence. [Merged-main CI 45663299370, attempt 1](https://github.com/peterl-ai/IPQC-system/actions/runs/45663299370/attempts/1)

| Merged-main job | Actual result |
| --- | --- |
| [Android 139378607987](https://github.com/peterl-ai/IPQC-system/actions/runs/45663299370/job/139378607987) | 138 JVM passes, Lint with zero errors, both Debug APKs built. |
| [Backend 139378608403](https://github.com/peterl-ai/IPQC-system/actions/runs/45663299370/job/139378608403) | Release build, 1,187 passed, 767 opt-in PostgreSQL skips. |
| [PostgreSQL 139378608314](https://github.com/peterl-ai/IPQC-system/actions/runs/45663299370/job/139378608314) | PostgreSQL 17.12, 767 passed, zero failed or skipped. |
| [Web 139378608523](https://github.com/peterl-ai/IPQC-system/actions/runs/45663299370/job/139378608523) | Lint, 166 tests, typecheck and production build. |

## Scope and release gates

- IPQC replaces the Line Patrol portion of legacy GQMS for new operations. Web/API v1 and separate LineShared planning, personal authorization and shared-draft foundations are merged. Android has read-only public navigation, protected offline work, a development-pilot sample Workbench, and internal one-batch item-removal transport.
- The current UI authorization uses the original protected ledger, existing foreground transport and authenticated receipt lookup. Default Debug remains read-only with the Workbench disabled and an empty server URL. Release has the Workbench disabled. Production activation, automatic/background sending and Formal Submit are outside the slice.
- Approved Rev3.2, supersession, and retained F2/F3 govern future scope. Physical Samsung SM-T500 and SM-X210 UAT, plant HTTPS, production data/storage, backup and restore and deployment acceptance remain pending. [Approved architecture](../architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md) · [Supersession](../architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md) · [Open issues](OPEN_ISSUES.md) · [Project roadmap](PROJECT_ROADMAP.md)
