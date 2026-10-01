# Codex Prompt — Phase 1A Web Foundation

You are the senior SDE implementing **Phase 1A** of the JAX Power In-Process Quality Control (IPQC) project.

## Before coding
Read completely:
- `README.md`
- `AGENTS.md`
- `docs/01_PROJECT_SCOPE.md`
- `docs/02_UI_FIELD_SPEC.md`
- `docs/03_RBAC.md`
- `docs/04_ARCHITECTURE.md`
- `docs/06_PHASE_PLAN.md`
- `docs/07_DECISIONS_OPEN_QUESTIONS.md`

Do not implement beyond Phase 1A.

## Goal
Create the initial **web frontend foundation** for JAX Power IPQC using mock/static data only.

Do not create the backend, database, scheduler, Android application, MES integration, or Material Verification workflow in this phase.

## Required stack
- Vue 3
- TypeScript strict mode
- Vite
- Ant Design Vue
- Vue Router
- vue-i18n
- Vitest
- Use Pinia only if shared state actually justifies it

## Application shell
Header:
- `JAX Power IPQC`
- current mock user display
- language selector: `中文` / `English`
- logout placeholder

Left navigation:
- Home
- IPQC
  - Line Patrol
    - Patrol Standards
    - Patrol Plans
    - Patrol Tasks
    - Completed Patrol Records
- System Management
  - User Management

Do **not** add Material Verification web workflow/page in Phase 1A. Its future Android icon is documented for a later Android/vendor phase.

## i18n
- All visible strings use translation keys.
- English and Simplified Chinese resources.
- Switching language updates menu, titles, filters, buttons, table headers, validation labels, empty states, etc. without reload.
- Persist selected language in localStorage.

## Mock roles / route permissions
Implement a development-only mock user/role selector or centralized mock current-user configuration.

### Admin
- all Line Patrol pages
- User Management

### PQE
- Patrol Standards
- Patrol Plans
- Patrol Tasks
- Completed Patrol Records
- no User Management

### IPQA
- Completed Patrol Records only on web

Requirements:
- hide unauthorized menu entries
- route guard direct URL access
- 403 / No Permission page
- centralized permission mapping

## Pages

### 1. Home
Simple JAX Power IPQC landing page focused on Line Patrol. Do not build a dashboard or analytics widgets.

### 2. Patrol Standards
Filters:
- Patrol Standard Name
- Factory
- Line

Toolbar:
- New
- Save
- Copy
- Export
- Import
- Download Template

Do not add Submit / Approve / Upgrade.

Mock columns:
- No.
- Patrol Standard Name
- Factory Code
- Factory Name
- Workshop Code
- Line Code
- Line Name
- Material Code
- Created By
- Created Time
- Updated By
- Updated Time
- Actions

Add a non-persistent modal/drawer editor shell with standard header fields and detail-grid skeleton.
Only `Line Name` and `Patrol Standard Name` visually indicate required fields.

Detail skeleton fields should follow `docs/02_UI_FIELD_SPEC.md`.

### 3. Patrol Plans
Mock fields/table:
- Enabled/status
- Effective start/end
- Schedule
- Plan No.
- Plan Name
- Patrol Standard
- Factory
- Line
- Assigned IPQA

Buttons/actions may be visual placeholders in Phase 1A.

### 4. Patrol Tasks
PQE-facing mock list:
- Status
- Inspection Result
- Generated Time
- Task No.
- Plan Name
- Standard Name
- Factory
- Line
- Inspector
- Submitted Time
- Actions

Include visual placeholder actions for `Approve` and `Reject`.
If a Reject mock modal is included, the Reason field must be visually required, but do not persist/mutate backend state yet.

### 5. Completed Patrol Records
This is a records/history page, **not a dashboard**.

Mock columns in this order:
- Task No.
- Completion Time
- Inspection Result
- Inspector
- Patrol Standard Name
- Patrol Plan Name
- Factory
- Line
- Patrol Plan No.
- Approver
- Approval Time
- Actions

Actions:
- Details
- Preview Report
- Download Report

Implement:
- a mock details page or drawer consistent with the documented Basic Information + Inspection Details structure
- a mock HTML/modal report preview shell
- Download Report can remain a non-functional placeholder in Phase 1A; later backend phase will produce Excel

### 6. User Management
Admin only.
Mock columns:
- Employee ID / Username
- Display Name
- Role
- Active
- Created Time
- Actions

No persistence.

## UX requirements
- desktop-first internal system
- horizontally scrollable tables where needed
- consistent filter/toolbars/pagination/loading/empty states
- no Base or Business Unit fields
- no Dashboard
- no proprietary Jinko logo/assets
- neutral JAX Power branding/text only

## Suggested organization
```text
web/src/
  app/
  components/
  features/
    patrol-standards/
    patrol-plans/
    patrol-tasks/
    completed-records/
    user-management/
  i18n/
  router/
  services/
  stores/
  types/
```

Do not build abstractions merely for hypothetical future needs.

## Tests
At minimum:
1. permission mapping for Admin/PQE/IPQA
2. route authorization helper/guard
3. language preference persistence/helper

## Completion criteria
- dependencies install successfully
- lint passes if configured
- unit tests pass
- production build passes
- no TypeScript errors

## Git / PR behavior
Work on branch:
`phase-1a-web-foundation`

At the end provide:
1. branch name
2. implementation summary
3. main files/folders created
4. exact test/build commands and results
5. screenshots or concise browser-preview description if supported
6. assumptions/open questions
7. intentionally deferred Phase 1B work

Do not start Phase 1B.
