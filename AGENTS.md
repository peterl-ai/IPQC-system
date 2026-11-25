# AGENTS.md — Coding Agent Contract

## Mission
Build the JAX Power IPQC system according to the repository documentation. Treat `docs/` as product requirements. Do not invent business rules when the docs are silent.

## Mandatory working rules
1. Read `README.md` and the relevant `docs/` files before changing code.
2. Work only on the requested phase / PR scope.
3. Do not implement Material Verification workflow. It is intentionally deferred to a future MES-vendor integration phase.
4. Do not implement Android business workflow unless explicitly requested. Future Android compatibility must still be considered in API/domain design.
5. Web must support English + Simplified Chinese through i18n. Do not hard-code visible display strings in components.
6. Android is English-only and must remain compatible with Android 11 / Samsung Galaxy Tab A7 SM-T500.
7. Do not add legacy GQMS Base or Business Unit fields.
8. Patrol Standards do not have legacy Submit / Approve / Upgrade actions.
9. PQE rejection of a submitted patrol task requires a reason and returns the task to IPQA for correction/re-inspection and resubmission.
10. Completed Patrol Records is a history module, not a dashboard.
11. Report preview is web HTML/modal. Report download is Excel (`.xlsx` preferred).
12. Do not migrate legacy GQMS history into the new DB.
13. Do not use MES DB as the application database. Future MES integration should be isolated behind an integration/API layer.
14. Do not store image binaries in the relational database. Persist attachment metadata/storage keys instead.
15. Add/update tests for every implemented business rule.
16. Keep changes reviewable and avoid unrelated refactors.

## Project Memory and handoffs

Before planning, implementing or reviewing a new task, read in order:

1. This contract.
2. The root [README.md](README.md).
3. The [Project Memory index](docs/handoff/README.md).
4. The [current project state](docs/handoff/CURRENT_PROJECT_STATE.md).
5. Relevant business and architecture decisions, PR history, review history and open issues.
6. Applicable approved requirements and their current source and tests.
7. Actual GitHub and Git state, including the relevant branch, PR and CI revisions.

Use source-backed memory to establish the continuation point. Record missing evidence as **UNKNOWN** or **UNVERIFIED**. Keep approved decisions, proposals, implemented behavior, validation and independent verdicts distinct. Escalate material business or architecture ambiguity to Peter.

Peter owns business and material architecture decisions, final merge and deployment. Codex implements, tests, self-reviews, prepares evidence and maintains Project Memory. ChatGPT is the independent architect and PR reviewer. Claude and other designated reviewers follow the same independence and exact-revision evidence rules. Codex records its own self-review separately from the independent verdict. Preserve the independent reviewer's disposition. A reviewer without repository write access must provide exact Project Memory updates for the implementation agent.

Update the affected records incrementally at PR creation and implementation completion, independent review or re-review, repair or verified closure, actual merge, material CI or test changes, approved decisions, blocker discovery or resolution and milestone completion. Maintain memory in normal authorized work. Record final review or merge status in the next authorized branch or a separately authorized documentation update. Preserve an already-reviewed application HEAD and historical snapshots.

Generate a self-contained, dated handoff after every 3–5 completed PRs, at milestone completion, after substantial approved changes, before changing agents or review sessions, on Peter's request and before context exhaustion. Use repository memory, verified Git state and relevant evidence. Include current state, latest merge, active PR and exact revision, approved decisions and architecture, review history and findings, test baseline, evidence gaps, open issues, immediate authorized action and receiving references.

Keep secrets, private keys or certificates, production connection strings, raw employee or inspection data, full transcripts, unrelated conversations, large raw CI logs, APKs and validation ZIPs out of repository memory. Store concise evidence references, hashes and exact Git pins.

### Draft PR and independent review workflow

1. **Codex implementation.** Implement the authorized scope, run the required gates and complete a strict self-review. Open one Draft PR with auto-merge disabled. Provide the PR URL, exact Base, HEAD and Tree, complete pinned binary-capable Base-to-HEAD diff, separate final-HEAD Push and pull_request CI links and actual job evidence, scope, risks and verification limits. Include a focused repair diff for re-review.
2. **Independent ChatGPT review.** Review the exact revision, GitHub source, full diff and actual CI logs. Record scope, findings, severity, evidence, verification limits and verdict. When changes are required, return a focused repair prompt and exact Project Memory updates. Repair within the same Draft PR and obtain re-review of the new revision.
3. **Independent review PASS.** In the same response, provide the final verdict, exact approved Base, HEAD and Tree, the direct GitHub PR link for Peter's manual merge, a complete implementation prompt for the next approved or clearly scoped PR, and exact Project Memory updates. State any scope that still needs authorization. If the sources establish no next scope, record the gap instead of assigning a milestone or granting implementation authority.
4. **Owner manual merge.** Peter makes the final GitHub merge and deployment decisions. ChatGPT and Codex do not merge or enable auto-merge on Peter's behalf. Codex keeps its PR Draft during independent review and does not mark its own work Ready.
5. **Codex merged-main preflight.** Start the next implementation prompt by verifying the preceding PR's actual merge, exact reviewed HEAD ancestry and merged tree, live main, local origin/main and successful merged-main CI using actual job logs. Confirm the intended clean base and check for overlaps.

   If the preceding PR is merged and CI for its exact merged-main SHA is queued, pending or in progress, monitor the existing run under a 45-minute deadline from the start of monitoring. Use `gh run watch <run-id> --exit-status` with an enforced deadline or equivalent bounded polling. Keep monitoring while CI is nonterminal. After terminal success, inspect all required actual job logs and proceed directly within existing authorization once every preflight gate passes.

   If local `origin/main` is behind verified remote main, fetch only the remote main into the local remote-tracking ref without forcing an update. Revalidate the fetched SHA, ancestry and tree against live GitHub. Preserve the protected original working tree without resetting, cleaning, switching or editing it.

   Stop only for a concrete failed gate, unsuccessful terminal CI, revision or tree drift, unexpected repository state, inaccessible required evidence or monitoring timeout. Report the exact blocker. A separate owner merge-confirmation message is unnecessary.

This workflow does not authorize unscoped implementation, business changes or deployment.

## Quality gates
- TypeScript strict mode.
- Lint, tests, and production build must pass for touched projects.
- No secrets, internal passwords, FTP credentials, tokens, or production connection strings committed.
- No raw production/customer/employee data in fixtures.
- Use stable IDs rather than display names for relationships.
- Server-side authorization must remain the source of truth once the API exists; UI hiding alone is insufficient.
- Scheduled task creation must be server-side and idempotent.

## Completion report
At completion, report:
- branch name
- files changed
- implemented behavior
- tests/build commands and exact results
- assumptions/open questions
- intentionally deferred work
