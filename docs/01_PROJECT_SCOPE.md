# 01 — Project Scope

## Product objective
Build a new JAX Power **In-Process Quality Control (IPQC)** system for new Line Patrol operations without carrying forward unnecessary GQMS complexity.

The legacy Jinko GQMS remains available for historical lookup; therefore this project starts with a clean application database and no GQMS data migration.

## Phase 1 — Web application

### In scope
1. Patrol Standards Management
2. Patrol Plans
3. Patrol Tasks / PQE Approval
4. Completed Patrol Records
5. Report Preview and Excel Download
6. User Management
7. Role-based access control
8. English / Simplified Chinese web language switching

### Out of scope
- Dashboard / BI page
- GQMS history migration
- Material Verification business workflow
- MES BOM/work-order integration
- Android implementation in Phase 1
- GQMS Submit / Approve / Upgrade standard workflow

## Future Android application
The Android client will be English-only and used by IPQA to:
- receive assigned patrol tasks
- execute inspections
- enter inspection values/results
- capture/upload photos
- submit work to PQE
- receive rejected tasks
- correct/re-inspect and resubmit

A future Material Verification icon should be reserved on the Android home screen, but its workflow is deferred to the MES vendor.

## Core role ownership
- **Admin**: platform administration + all business functions
- **PQE**: Line Patrol business owner; standards/plans/tasks/approval/records/reports
- **IPQA**: inspection executor on Android; web access limited to Completed Patrol Records

## Data ownership boundary
The IPQC system owns its own operational database. MES integration is future scope and must not make MES DB the storage layer for Line Patrol.
