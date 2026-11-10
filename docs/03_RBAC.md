# 03 — Role-Based Access Control

## Rev3.2 personal read foundation

`LinePatrol.Read` is granted only to eligible IPQA accounts. New `/api/line-patrol/*` routes also require a current signed personal family/environment matched to a live unrevoked refresh row and current DB user/AuthVersion. Admin/PQE review permissions are unchanged and do not grant IPQA execution. Old Web navigation and LegacyPersonalV1 APIs keep existing policies and owner semantics. The new directory/read DTOs grant no task write permission. Owner-only `/api/auth/personal-sessions/{id}/revoke` requires current personal-family auth, permits forced-change users to close their own prior family and never closes another user's family. See [contract](16_R3202_PERSONAL_ACCESS.md).

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
Phase 1G uses persistent local ApplicationUser accounts. Admin, PQE, and IPQA authenticate with the same backend used by future Android clients. No Entra, O365, or LDAP integration is planned for v1.

- Route/menu visibility follows the permissions returned by `/api/me`; API permission policies remain authoritative.
- Each bearer request reloads the active user and current role from the database and checks the auth version.
- A user flagged `MustChangePassword` can access `/api/me`, refresh, logout, and change password, but has no business permission claims.
- Admin alone manages users. The last active Admin cannot be deactivated or demoted, and an Admin cannot deactivate or demote themselves.
- An IPQA assigned to an enabled Plan or unfinished Task cannot be deactivated or moved to another role. Completed history alone does not block a change.
