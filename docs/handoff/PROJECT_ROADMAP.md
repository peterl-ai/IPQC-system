# IPQC V1 project roadmap

Updated 2026-11-27 from verified Git, approved Rev3.2 and the retained F2/F3 sign-off. The roadmap is a planning record. Peter owns scope, final merge, release and deployment. [Current state](CURRENT_PROJECT_STATE.md) · [Approved Rev3.2](../architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md) · [Rev3.2 supersession](../architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md) · [F2/F3 handoff](../architecture/IPQC_ANDROID_ARCHITECTURE_V1_IMPLEMENTATION_HANDOFF_2026-11-06.md)

- **Latest verified merge.** PR #48, A04-05B Basic Information foreground transport and gated Workbench UI, `054d57711c8936f05306aca18497e334f931ab8f` · Tree `063f12899900fc2ffda102a3aca030c5d005e0e7`. [PR #48](https://github.com/peterl-ai/IPQC-system/pull/48) · [Merged-main CI, attempt 1](https://github.com/peterl-ai/IPQC-system/actions/runs/46066793596/attempts/1)
- **Active Draft scope.** A04-06A Android Ordinary NG Details read, canonical V1 identity and protected local Set/Correct foundation. A04-06A is the newly authorized working label for this split. [Current state](CURRENT_PROJECT_STATE.md)

| V1 capability | Evidence or next work |
| --- | --- |
| Web/API v1 Line Patrol | Merged legacy-compatible Standards, Plans, personal authentication, task review and Completed Records reporting. Preserve role, historical fields and outcome behavior. |
| Rev3.2 shared foundations | Merged server-owned line/task/access, initial identity, draft CAS, sample and disposition operations, scoped provenance, typed evidence, draft headers/PIC/ordinary NG and Submit preflight. Android public read-only sign-in/task/offline access and gated sample Workbench are merged. |
| Android inspection | RemoveItem and Basic Information foundations, internal foreground transport, exact recovery and gated Workbench UI are merged. Ordinary NG read and protected local preparation are active. Ordinary NG transport/UI, PIC, UnableToMeasureNG and additional draft synchronization remain subsequent scopes. Production activation remains gated. |
| Photo and reference evidence | Backend Ready/association and draft evidence foundations merged. Android camera/album, protected persistent copy, explicit transfer and recovery, current-attempt evidence binding, reference image cache/zoom, and tablet/QA evidence remain. |
| Formal Submit and F2 | Submit preflight exists. Implement explicit durable Submit intent, immutable snapshot and attachments, server transaction and receipts, query-only recovery and signed-off A/B takeover. All-Removed uses distinct NotPerformed workflow. |
| PQE and reinspection | New LineShared exact revision approval/rejection, reason and targets, separate NotPerformed handling, immediate new NG revision visibility, fresh targeted attempts and inherited minimum rules remain. |
| Reporting and F3 | Preserve existing reporting. Add version-aware LineShared history, HTML preview/XLSX and distinct final outcomes. Complete formal references, attachment locking, five-year retention, audited deletion and restore reconciliation. |
| UAT and production readiness | Physical tablet and factory HTTPS tests, permission and network checks, production-like migrations, coordinated backup/restore, storage readiness, one-time retention enablement and Peter's release/activation gates remain. [Open issues](OPEN_ISSUES.md) |
| Deferred outside this V1 denominator | Material Verification, MES integration, legacy history migration and retired device registration/device-PKI/mTLS. |

## Estimation and maintenance

Track each capability as scoped, foundation merged, integration pending, integration merged, or release verified. Count a slice as merged only after the exact merge and its CI are verified. Keep production readiness separate and require actual evidence for facilities, devices, HTTPS and data lifecycle gates. Split remaining work by one independently reviewable contract plus acceptance path per PR. Use a range only when the low/high end states which coupled pieces can land together and which depend on separate tests, decisions or infrastructure. Reassess affected rows after verified merges and external gate changes. An active Draft, approved design, emulator and synthetic data establish different evidence.

The provisional management estimate after PR #44 was approximately 65% for V1 business development, 55% for overall production readiness, and 18–26 remaining PRs. Those were planning estimates with no measured per-capability denominator. This record carries them as historical context and does not extrapolate a new percentage from the merge count.
