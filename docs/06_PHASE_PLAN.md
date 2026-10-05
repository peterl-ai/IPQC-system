# 06 — Phase Plan

## Phase 1A — Web Foundation
Goal: establish Vue web shell using **mock data only**.

- Vue 3 + TypeScript + Vite + Ant Design Vue
- Router
- English/Chinese i18n
- mock role switching and route guards
- Home
- Patrol Standards mock page
- Patrol Plans mock page
- Patrol Tasks mock page
- Completed Patrol Records mock page
- User Management mock page
- report preview placeholder/modal
- no backend/database
- no Material Verification workflow/page

## Phase 1B — Patrol Standards Web Behavior
- standard list/edit/create/copy
- validation
- detail item editor
- import/export/template UI behavior using local/mock adapters where appropriate
- still no production persistence unless explicitly included in the PR

## Phase 1C — Backend Foundation + Patrol Standards
- ASP.NET Core API
- EF Core
- PostgreSQL target; SQLite allowed for local automated testing
- migrations
- standard CRUD
- Excel import/export/template
- validation/error contracts

## Phase 1D — Patrol Plans + Scheduler
- plan CRUD
- assignee management
- schedule/effective period
- enable/disable
- server-side task generation
- idempotency/duplicate prevention

## Phase 1E — Patrol Task Execution Contract + PQE Approval
- task API/domain state machine and future Android-compatible execution contract
- task-time Plan/Standard and ordered item snapshots
- temporary draft save, Shift, N/A, qualitative and quantitative samples, server judgment
- immutable submission revisions and item/sample history
- PQE approve, batch approve, reasoned reject, and re-inspection/resubmission
- persistent PQE web Task list/detail and review actions

## Phase 1F — Completed Records + Reporting
- filters/pagination
- details page
- HTML/modal report preview
- Excel `.xlsx` download

Implemented as a read-only view of Completed tasks and their final approved immutable submissions. Photo display remains deferred with the attachment workflow; no Phase 1E image data exists to report.

## Phase 1G — User Management + Production RBAC
- persistent users/roles
- server authorization
- route/menu permission integration
- audit fields

## Phase 1H — UAT / Hardening
- bilingual review
- pagination/performance
- error handling
- security review
- scheduler recovery/idempotency tests
- deployment configuration
- backup/restore runbook

## Later Phase — Android IPQA App
- English-only
- Android 11 compatible
- Galaxy Tab A7 SM-T500 validation
- task receive/execute/photo/submit
- rejected-task correction/reinspection/resubmit
- reserve Material Verification icon only

## Later MES Vendor Phase — Material Verification
To be designed with MES integration for work order/BOM/material verification.
