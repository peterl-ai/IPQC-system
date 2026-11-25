# IPQC Project Memory

This directory carries project context across ChatGPT, Codex, Claude and other engineering sessions. Peter owns business decisions, architecture changes, merge and deployment authorization. The implementation engineer maintains this record and attaches evidence. The independent reviewer supplies their own verdict. When a reviewer cannot write to the repository, they provide exact Markdown edits for the implementation engineer to record faithfully.

## Start a session

1. Read [AGENTS.md](../../AGENTS.md).
2. Read the [root README](../../README.md).
3. Read this Project Memory index.
4. Read the [current project state](CURRENT_PROJECT_STATE.md) and identify the approved continuation point.
5. Read relevant [business decisions](BUSINESS_DECISION_LOG.md), [architecture decisions](ARCHITECTURE_DECISION_LOG.md), [PR history](PR_HISTORY.md), [review history](REVIEW_HISTORY.md) and [open issues](OPEN_ISSUES.md).
6. Read the applicable approved requirements and their source and tests.
7. Verify current GitHub and Git state, including the relevant Base, HEAD, Tree, PR state and actual CI evidence.

Use these sources to establish the approved scope and report any evidence gaps that affect it.

## Authority and status labels

| Question | Authoritative evidence |
| --- | --- |
| Intended behavior | Explicit Peter-approved requirements, architecture and subsequent approved amendments. Preserve the original sign-off and cite the amendment. |
| Implemented behavior | Source, schema and tests at a verified Git revision. Identify merged main versus open PRs. |
| Verified result | CI and test artifacts tied to the exact revision, including applicable checkout, environment and results. Cite a test that was actually run. |
| Independent finding or verdict | The attributable review of the exact revision. An author report is evidence of work and self-review. |
| Unavailable or conflicting evidence | Record precisely what conflicts or is missing, which revision/date it affects and who can reconcile it. |

Use **APPROVED** for explicit authorization, **PROPOSED** for a candidate decision, **IMPLEMENTED** for pinned source, **VERIFIED** for pinned tests, and **DEFERRED** for named future scope. Use **UNKNOWN / UNVERIFIED** when a date, approval, result or rationale is absent. In review history use **OPEN**, **FIXED — AWAITING INDEPENDENT RE-REVIEW**, **VERIFIED CLOSED**, **ACCEPTED LIMITATION**, **DEFERRED WITH APPROVAL** and **UNKNOWN / HISTORICAL EVIDENCE UNAVAILABLE**. Record changes incrementally and retain earlier review verdicts with their revisions.

## Update the record

Update the affected entries when a PR is created, implementation completes, a review or re-review completes, a finding is repaired, a PR is approved or merged, a business or architecture decision is approved or superseded, the CI/test baseline materially changes, a blocker appears or resolves, or a milestone ends. Record missing evidence as **UNKNOWN** or **UNVERIFIED**. Carry this maintenance in normal authorized PRs. Persist final review and merge status in the next authorized branch or a separately authorized documentation update. Preserve a reviewed application HEAD and keep snapshots historical.

- [CURRENT_PROJECT_STATE.md](CURRENT_PROJECT_STATE.md) carries today's action, gate and exact refs.
- [PR_HISTORY.md](PR_HISTORY.md) records scope, source and merge identity, outcomes and evidence.
- [BUSINESS_DECISION_LOG.md](BUSINESS_DECISION_LOG.md) records approved product behavior and unresolved business policy.
- [ARCHITECTURE_DECISION_LOG.md](ARCHITECTURE_DECISION_LOG.md) records approved engineering decisions and implementation boundaries.
- [REVIEW_HISTORY.md](REVIEW_HISTORY.md) preserves findings, repairs and independent dispositions.
- [OPEN_ISSUES.md](OPEN_ISSUES.md) carries active dependencies, deferred work and evidence gaps.
- [snapshots/](snapshots/) contains dated, standalone historical handoffs. Add a dated snapshot for a new state. Preserve existing snapshots.

## Draft PR and independent review workflow

Peter owns business and material architecture decisions, final manual merge and deployment. Codex implements, tests, self-reviews, prepares evidence and maintains this record. ChatGPT is the independent architect and PR reviewer. Claude and other designated reviewers follow the same independence and exact-revision evidence rules.

1. **Codex implementation.** Implement the authorized scope, run the required gates and complete a strict self-review. Open one Draft PR with auto-merge disabled. Provide the PR URL, exact Base, HEAD and Tree, complete pinned binary-capable Base-to-HEAD diff, separate final-HEAD Push and pull_request CI links and actual job evidence, scope, risks and verification limits. Include a focused repair diff for re-review.
2. **Independent ChatGPT review.** Review the exact revision, GitHub source, full diff and actual CI logs. Record scope, findings, severity, evidence, verification limits and verdict. When changes are required, return a focused repair prompt and exact Project Memory updates. Repair within the same Draft PR and obtain re-review of the new revision.
3. **Independent review PASS.** In the same response, provide the final verdict, exact approved Base, HEAD and Tree, the direct GitHub PR link for Peter's manual merge, a complete implementation prompt for the next approved or clearly scoped PR, and exact Project Memory updates. State any scope that still needs authorization. If the sources establish no next scope, record the gap instead of assigning a milestone or granting implementation authority.
4. **Owner manual merge.** Peter makes the final GitHub merge and deployment decisions. ChatGPT and Codex do not merge or enable auto-merge on Peter's behalf. Codex keeps its PR Draft during independent review and does not mark its own work Ready.
5. **Codex merged-main preflight.** Start the next implementation prompt by verifying the preceding PR's actual merge, exact reviewed HEAD ancestry and merged tree, live main, local origin/main and successful merged-main CI. Confirm the intended clean base and check for overlaps.

   If the preceding PR is merged and CI for its exact merged-main SHA is queued, pending or in progress, monitor the existing run under a 45-minute deadline from the start of monitoring. Use `gh run watch <run-id> --exit-status` with an enforced deadline or equivalent bounded polling. Keep monitoring while CI is nonterminal. After terminal success, inspect all required actual job logs and proceed directly within existing authorization once every preflight gate passes.

   If local `origin/main` is behind verified remote main, fetch only the remote main into the local remote-tracking ref without forcing an update. Revalidate the fetched SHA, ancestry and tree against live GitHub. Preserve the protected original working tree without resetting, cleaning, switching or editing it.

   Stop only for a concrete failed gate, unsuccessful terminal CI, revision or tree drift, unexpected repository state, inaccessible required evidence or monitoring timeout. Report the exact blocker. A separate owner merge-confirmation message is unnecessary.

This workflow does not authorize unscoped implementation, business changes or deployment. Preserve the reviewer's exact disposition. A reviewer without repository write access provides exact edits for current state, review history, open issues and any relevant PR or decision records.

## Handoffs

Generate a self-contained dated handoff after every **3–5 completed PRs**, at milestone completion, after substantial approved business or architecture changes, before switching agents or review sessions, when Peter requests one and before conversation context exhaustion. Use repository memory, verified Git state and relevant evidence to restore context.

Include current project state, latest merged milestone, active PR and exact revision, approved decisions and architecture, independent review history and outstanding findings, verified test baseline, known evidence gaps, open issues, immediate next authorized action and required references. Keep prior snapshots historical. [Consolidated handoff through PR #43](snapshots/IPQC_HANDOFF_THROUGH_PR43_2026-11-24.md) · [Historical PR #43 pre-review snapshot](snapshots/2026-11-24_PR43_A04-02_RE_REVIEW.md)

## Evidence to keep

Record concise durable evidence links, file hashes and Git revisions. Keep passwords, tokens, private keys and certificates, production connection strings, raw employee or production inspection data, personal or unrelated conversation history, full transcripts, large raw CI logs, APKs and validation ZIPs out of repository memory.
