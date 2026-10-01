# 03 — Role-Based Access Control

## Roles

### Admin
- Patrol Standards: read/write
- Patrol Plans: read/write
- Patrol Tasks: read/review/approve/reject
- Completed Records: read/export/report
- User Management: read/write

### PQE
- Patrol Standards: read/write
- Patrol Plans: read/write
- Patrol Tasks: read/review/approve/reject
- Completed Records: read/export/report
- User Management: no access

### IPQA
Web:
- Completed Patrol Records: read only
- No Standards / Plans / Task approval / User Management

Android (future):
- View assigned tasks
- Start/continue inspection
- Enter inspection results
- Capture/upload photos
- Submit
- Receive rejected tasks
- Correct/re-inspect
- Resubmit

## Enforcement
Phase 1A may use a mock role switcher for UI testing.

Once the API exists:
- route/menu visibility is UX only
- API authorization is mandatory and authoritative
- server endpoints must enforce role/permission checks
