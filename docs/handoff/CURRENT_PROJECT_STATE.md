# Current IPQC project state

**Verified 2026-11-27 · A04-05A merged · A04-05B Open Draft awaiting focused independent re-review**

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

[PR #48](https://github.com/peterl-ai/IPQC-system/pull/48) is Open Draft with auto-merge disabled. Initial independent review returned **HOLD — FOCUSED REPAIR REQUIRED** at HEAD `b1222b66cf4f8177c02e3b2cfd38e779fcb1974e`, Tree `1598b72744c9692b1de2816b401ccde8e4629b37`. Codex completed the focused application repair in `6861c2fa7d450b89e0f559a44c34a2581c02d44b` with Tree `111db6ef0b352178260ee4641b1f09aef02a5d64`. The exact final Draft and CI pins are in the re-review handoff. The initial independent HOLD remains in effect pending independent re-review. [Independent review](REVIEW_HISTORY.md#2026-11-27-initial-independent-review-of-pr-48)

## Scope and release gates

- IPQC replaces legacy GQMS Line Patrol for new operations. Web/API v1 and Rev3.2 personal authorization, LineShared planning and draft foundations are merged. Android has public read-only navigation, protected offline work and gated Sample and Item Removal Workbench functionality. Public Debug keeps Workbench disabled and an empty server URL. Release keeps Workbench disabled.
- Peter authorized the A04-05B Draft after merged-main preflight. Scope covers the optional-field editor, immutable protected batches, explicit foreground POST under shared task-wide admission, complete V6 settlement and explicit exact batch or operation receipt recovery. Android ordinary NG, PIC, UnableToMeasureNG, Photos, Formal Submit and production activation remain separate. [Task Basic Information contract](../29_R3205_BASIC_INFORMATION_DRAFT.md)
- Approved Rev3.2 and retained F2/F3 govern remaining work. Physical Samsung SM-T500/SM-X210 UAT, plant HTTPS, production activation, backup/restore and deployment acceptance remain pending. [Approved architecture](../architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md) · [Supersession](../architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md) · [Open issues](OPEN_ISSUES.md) · [Roadmap](PROJECT_ROADMAP.md) · [Historical handoff through PR #46](snapshots/IPQC_HANDOFF_THROUGH_PR46_2026-11-27.md)
