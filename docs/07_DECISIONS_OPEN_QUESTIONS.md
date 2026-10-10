# 07 — Decisions and Open Questions

## Confirmed decisions
- Product name: JAX Power In-Process Quality Control (IPQC) System.
- No GQMS historical data migration.
- No dashboard in current scope.
- Material Verification workflow is deferred to the MES vendor.
- Future Android home should reserve a Material Verification icon/entry only.
- Web is bilingual English/Chinese.
- Android is English-only.
- Current tablet target includes Galaxy Tab A7 SM-T500 on Android 11.
- PQE rejection requires reason and returns task to IPQA for correction/reinspection/resubmission.
- Report preview is HTML/modal.
- Report download is Excel.
- Admin manages users; PQE does not.
- IPQA web access is Completed Patrol Records only.
- New IPQC system owns a separate database; MES DB is not the Line Patrol database.
- Images/files live outside the relational DB; DB stores metadata/storage keys.

## Recommended decisions (can be changed before backend phase)
- Production DB: PostgreSQL.
- Local/test DB: SQLite.
- Download format: `.xlsx`, not legacy `.xls`.
- File access: authenticated HTTPS/API path rather than exposing FTP credentials/direct `ftp://` links.

## Still open before later backend/production phases
1. Exact production DB host/operations ownership.
2. Exact file server technology:
   - existing FTP-backed storage
   - SMB/network share behind API
   - S3-compatible/MinIO
3. File retention policy and maximum image size/count.
4. Resolved for Phase 1E: all submitted item and sample results receive immutable normalized revision rows.
5. Whether Excel download must exactly match legacy GQMS layout or only preserve required report content.

## Phase 1G identity decision
- IPQC v1 uses local ApplicationUser accounts for both Web and future Android. No Entra/O365/LDAP in v1.
- Passwords are hashed, access uses short-lived JWTs with database-backed user validation, and rotating refresh sessions store token hashes only. Web uses an HttpOnly refresh cookie.
- Admin manages persistent users without hard delete. New/reset accounts require password change; failed logins lock after five attempts for 15 minutes.
- Plan assignees and Task owners use User IDs. Audit actor fields use the authenticated username.
- Production credentials and JWT signing key come from deployment secrets, never repository configuration.

## Phase 1D decisions and remaining questions
- PQE uses a business-friendly recurrence form (Every N Hours, Daily, Weekly); shift, holiday, and monthly scheduling are deferred.
- The Jacksonville plant timezone is `America/New_York`. Scheduling uses local wall-clock time and UTC persistence; spring gaps are skipped and fall overlaps choose the earlier UTC instant.
- The scheduler catches up at most 24 hours by default and does not backfill disabled intervals or reinterpret past time after plan edits.
- Exactly one active IPQA execution assignee per enabled plan; rotation and batch reassignment are deferred.
- Generated tasks retain their selected standard ID and assignee. Phase 1E freezes the Plan/Standard header and ordered Standard Items at generation; submissions freeze item/sample results separately by revision.
- Plan No. and Task No. formats are technical GUID-based conventions until a business numbering requirement is approved.
