# Current IPQC project state

**Verified 2026-11-27 · A04-04 merged · A04-05A Basic Information foundation authorized**

Peter merged PR #46 after independent ChatGPT issued **PASS — APPROVED FOR MERGE** and **R46-P2-01 VERIFIED CLOSED**. The merged feature-gated Workbench preserves the removal projection and exact recovery fixes. R43-P2-02 and R44-P3-01 remain verified closed. [PR #46](https://github.com/peterl-ai/IPQC-system/pull/46) · [Merged-main CI, attempt 1](https://github.com/peterl-ai/IPQC-system/actions/runs/46026722209/attempts/1) · [Independent re-review and limits](REVIEW_HISTORY.md#2026-11-27-final-independent-re-review-of-pr-46)

| Git field | Verified value |
| --- | --- |
| Latest merged main, local origin/main and A04-05A Base at preflight | `1a30fe61a744b9282a20d18f92a521d6b59b9207` |
| Reviewed and merged Tree | `b4268accd7f35d515513b305c4037e439ee29d38` |
| Ordered merge parents | `8120ace2ce5b4f458745dbf601f211960a2af4ca`, then reviewed HEAD `5b6622c936bcca42bcdb5730166b394cac31d8e1` |
| Active implementation branch | `codex/android-v1-a04-05a-basic-information-foundation` · [Draft PR by branch](https://github.com/peterl-ai/IPQC-system/pulls?q=is%3Apr+head%3Acodex%2Fandroid-v1-a04-05a-basic-information-foundation) |

Codex inspected all four actual logs on the exact merged-main checkout. PR #46's API 30/35 runs of 60 passes each are prior developer evidence.

| Merged-main job | Result |
| --- | --- |
| [Android 140599904085](https://github.com/peterl-ai/IPQC-system/actions/runs/46026722209/job/140599904085) | JVM 139 passed, Lint passed, Debug and instrumentation APK assemblies passed. The matching PR #46 Tree's local Lint report has 0 errors and 2 existing warnings. |
| [Backend 140599904277](https://github.com/peterl-ai/IPQC-system/actions/runs/46026722209/job/140599904277) | Release build passed, 1,187 passed, 767 opt-in PostgreSQL skips. |
| [PostgreSQL 140599904438](https://github.com/peterl-ai/IPQC-system/actions/runs/46026722209/job/140599904438) | PostgreSQL 17.12, Release build passed, 767 passed, zero skipped or failed. |
| [Web 140599904229](https://github.com/peterl-ai/IPQC-system/actions/runs/46026722209/job/140599904229) | Lint, 166 tests, typecheck and production build passed. |

## Scope and release gates

- IPQC replaces legacy GQMS Line Patrol for new operations. Web/API v1 and Rev3.2 personal authorization, LineShared planning and draft foundations are merged. Android has public read-only navigation, protected offline work, and gated sample and item-removal Workbench functionality. Public Debug keeps Workbench disabled and an empty server URL. Release keeps Workbench disabled.
- Peter authorized A04-05A after merged-main preflight. The Draft scope is authenticated Basic Information reads, V1 canonical identity, and protected local task-level capture, correction, replay and projection. Header POST, ReceiptVersion 6 settlement and editable Workbench UI are a subsequent slice. [Task Basic Information contract](../29_R3205_BASIC_INFORMATION_DRAFT.md)
- Approved Rev3.2 and retained F2/F3 govern remaining work. Physical Samsung SM-T500/SM-X210 UAT, plant HTTPS, production activation, backup/restore and deployment acceptance remain pending. [Approved architecture](../architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md) · [Supersession](../architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md) · [Open issues](OPEN_ISSUES.md) · [Roadmap](PROJECT_ROADMAP.md) · [Consolidated handoff](snapshots/IPQC_HANDOFF_THROUGH_PR46_2026-11-27.md)
