# Current IPQC project state

**Verified 2026-11-27 · A04-05A merged · A04-05B foreground transport and Workbench editor authorized**

Peter merged PR #47 after independent ChatGPT issued **PASS — APPROVED FOR MERGE**. The merged Android foundation covers authenticated Basic Information observations, canonical V1 identity and exact protected local operations and replay. R43-P2-02, R44-P3-01 and R46-P2-01 remain verified closed. [PR #47](https://github.com/peterl-ai/IPQC-system/pull/47) · [Independent review and limits](REVIEW_HISTORY.md#2026-11-27-final-independent-review-of-pr-47)

| Git field | Verified value |
| --- | --- |
| Latest merged main, local origin/main and A04-05B Base at preflight | `2c85ca164815999bd2ea0aeee4f9d6779af85474` |
| Reviewed and merged Tree | `db4f7ab494fbadc45ddd04a2464fa451c2135e4c` |
| Ordered merge parents | `1a30fe61a744b9282a20d18f92a521d6b59b9207`, then reviewed HEAD `0f7f5d611f57ce8170a5c90174bd337d4b41f2c0` |
| Active implementation branch | `codex/android-v1-a04-05b-basic-information-transport-workbench` · [Draft PR by branch](https://github.com/peterl-ai/IPQC-system/pulls?q=is%3Apr+head%3Acodex%2Fandroid-v1-a04-05b-basic-information-transport-workbench) |

Codex inspected all four actual logs at the exact merged-main commit. PR #47's API 30 and API 35 runs of 66 passes each remain preceding developer evidence. [Merged-main CI, attempt 1](https://github.com/peterl-ai/IPQC-system/actions/runs/46040662582/attempts/1)

| Merged-main job | Result |
| --- | --- |
| [Android 140647263833](https://github.com/peterl-ai/IPQC-system/actions/runs/46040662582/job/140647263833) | JVM 145 passed, Lint passed, Debug and instrumentation APK assemblies passed. The local report for the matching reviewed Tree records 0 errors and 2 existing warnings. |
| [Backend 140647263225](https://github.com/peterl-ai/IPQC-system/actions/runs/46040662582/job/140647263225) | Release build passed, 1,187 passed, 767 opt-in PostgreSQL skips. |
| [PostgreSQL 140647268623](https://github.com/peterl-ai/IPQC-system/actions/runs/46040662582/job/140647268623) | PostgreSQL 17.12, Release build passed, 767 passed, zero skipped or failed. |
| [Frontend 140647263992](https://github.com/peterl-ai/IPQC-system/actions/runs/46040662582/job/140647263992) | Lint, 166 tests, typecheck and production build passed. |

## Scope and release gates

- IPQC replaces legacy GQMS Line Patrol for new operations. Web/API v1 and Rev3.2 personal authorization, LineShared planning and draft foundations are merged. Android has public read-only navigation, protected offline work and gated Sample and Item Removal Workbench functionality. Public Debug keeps Workbench disabled and an empty server URL. Release keeps Workbench disabled.
- Peter authorized the A04-05B Draft after merged-main preflight. Scope covers the optional-field editor, immutable protected batches, explicit foreground POST under shared task-wide admission, complete V6 settlement and explicit exact batch or operation receipt recovery. Android ordinary NG, PIC, UnableToMeasureNG, Photos, Formal Submit and production activation remain separate. [Task Basic Information contract](../29_R3205_BASIC_INFORMATION_DRAFT.md)
- Approved Rev3.2 and retained F2/F3 govern remaining work. Physical Samsung SM-T500/SM-X210 UAT, plant HTTPS, production activation, backup/restore and deployment acceptance remain pending. [Approved architecture](../architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md) · [Supersession](../architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md) · [Open issues](OPEN_ISSUES.md) · [Roadmap](PROJECT_ROADMAP.md) · [Historical handoff through PR #46](snapshots/IPQC_HANDOFF_THROUGH_PR46_2026-11-27.md)
