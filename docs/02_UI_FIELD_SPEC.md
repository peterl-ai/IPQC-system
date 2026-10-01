# 02 — UI and Field Specification

## Global web behavior
- Desktop-first internal application.
- English / Simplified Chinese selector in header.
- Consistent filter row, toolbar, table, pagination, empty/loading/error states.
- Horizontal table scroll is acceptable.
- Do not use Jinko proprietary logos/assets.

# A. Patrol Standards Management

## Filters
- Patrol Standard Name
- Factory
- Line

## Toolbar
First-level buttons:
- New
- Save
- Copy
- Export
- Import
- Download Template

Do not include:
- Submit
- Approve
- Upgrade

## Header fields
| Field | Required |
|---|---|
| Factory Code | No |
| Factory Name | No |
| Workshop Code | No |
| Line Code | No |
| Line Name | **Yes** |
| Patrol Standard Name | **Yes** |
| Material Code | No |

Removed legacy fields:
- Base
- Business Unit

## Detail-item fields
Based on the provided GQMS Excel template:
- Process Code
- Process Name
- Inspection Item Category
- Inspection Item
- Inspection Content
- Upper Limit Operator
- Upper Limit Value
- Lower Limit Operator
- Lower Limit Value
- Inspection Type
- Sampling Plan
- Sample Count
- Photo Requirement
- Defect Level

## List columns
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

# B. Patrol Plans

## Purpose
A Patrol Plan references a Patrol Standard and defines when server-side scheduling should generate Patrol Tasks for assigned IPQA inspectors.

## Core fields
- Enabled / Disabled
- Effective Start
- Effective End
- Schedule / Frequency
- Plan No.
- Plan Name
- Patrol Standard
- Factory
- Line
- Assigned IPQA

No Base or Business Unit fields.

# C. Patrol Tasks

## PQE-facing list
Suggested columns:
- Status
- Inspection Result
- Generated Time
- Task No.
- Plan Name
- Patrol Standard Name
- Factory
- Line
- Inspector
- Submitted Time
- Review Status
- Actions

## Approval actions
- Approve
- Reject

Reject requires a reason. Rejected task returns to IPQA Android client for correction/re-inspection and resubmission.

# D. Completed Patrol Records

This page is the historical record page. No dashboard is required.

## Filters
At minimum:
- Task No.
- Completion Time range
- Patrol Standard Name
- Factory
- Line
- Inspector

## Column order
1. Task No.
2. Completion Time
3. Inspection Result
4. Inspector
5. Patrol Standard Name
6. Patrol Plan Name
7. Factory
8. Line
9. Patrol Plan No.
10. Approver
11. Approval Time
12. Actions

Avoid carrying blank legacy GQMS fields unless a real JAX requirement appears.

## Actions
- Details
- Preview Report
- Download Report

## Details page
### Basic Information
- Task No.
- Inspector
- Shift
- Patrol Plan
- Patrol Standard
- Factory
- Line
- Created Time
- Completed Time
- Approver
- Approval Time

### Inspection details
- Inspection Item
- Process Information
- Reference Photo
- Inspection Photo
- Inspection Type
- Upper Limit
- Lower Limit
- Sampling Plan
- Sample Count
- Inspection Date
- Inspection Time
- Inspection Result
- Reinspection Result
- Abnormal Code
- Abnormal Type
- Abnormal Reason
- Abnormal Description
- Log / audit trail

# E. Report
- Preview: HTML page or modal/popup in browser.
- Download: Excel; `.xlsx` preferred for the new system.
- Report should be generated from persisted inspection/review data, not from UI state.

# F. User Management
Admin-only management screen.

Suggested columns:
- Employee ID / Username
- Display Name
- Role
- Active
- Created Time
- Updated Time
- Actions
