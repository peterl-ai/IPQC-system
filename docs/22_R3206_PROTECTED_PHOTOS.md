# R32-06 — Protected photo upload and Ready foundation

Status: implementation for independent PR review under Peter's scoped authorization;
not production activation or permission to merge. Approved [Rev3.2](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_2026-11-10.md)
and [Supersession](architecture/IPQC_ANDROID_ARCHITECTURE_V1_REV3_2_IMPLEMENTATION_HANDOFF_SUPERSESSION_2026-11-10.md)
take precedence. This slice follows PR #20, merged main
`74e1da258d0acd2ae4e403fea9bb55736564a2b2`, whose [CI](https://github.com/peterl-ai/IPQC-system/actions/runs/43450875664)
passed backend, real PostgreSQL 17 and Web jobs. It does not approve any proposed future contract.

## Meaning and boundaries

An attachment reservation binds a real Task/Item/current InspectionAttempt to one
environment-wide ClientPhotoId, immutable content descriptor and eligible WorkContext
source A. It is neither an inspection result nor business operation acceptance. Source
proof possession never authenticates B, establishes physical authorship, proves capture
time, completes inspection work or grants Submit. Camera/Album is an unverified client
claim. Server times record actual reservation, durable verification and first Ready;
EXIF/client clock/workspace identity do not establish authority.

Ready means the server verified actual uploaded bytes, completed its documented filesystem
flush/promotion protocol and committed immutable acceptance plus availability/audit.
The stored encoding is JPEG/PNG; bytes are returned unchanged from what was uploaded.
They are not claimed to be camera originals if the client compressed them. No cross-ID
physical deduplication is performed. Historical ReadyAt remains immutable when current
availability later becomes MissingUnexpected/Corrupt. Every successful Ready/status/read
response rechecks actual bytes. There is no public file path or static media middleware.

## API contract v1

All routes are below `/api/line-patrol/tasks/{taskId}/inspection-attachments`.
All require current personally authenticated IPQA family, configured environment and
active line/task read scope. Writes also require `LinePatrol.ContributeDraft`.
No device, client certificate, mTLS, MES or PKI integration is used.

| Method / suffix | Contract |
| --- | --- |
| POST `/` | Strict bounded JSON `AttachmentInput`: ClientPhotoId, ContractVersion=1, real TaskItemId/InspectionAttemptId, ExpectedTaskLineId/WorkflowVersion=2/EnvironmentId, SourceWorkContextId, ContributorUserIdAssertion, ProofSecret, ByteCount, Sha256, ContentType, CaptureSource. Reserves identity only; returns safe metadata with IsReady=false. |
| PUT `/{attachmentId}/content` | Raw `application/octet-stream`; A proof in `X-WorkContext-Proof` header, never URL. Current B, actual editable attempt and A validated before body streaming. Final acceptance revalidates them under locks. Returns 200 only after committed Ready and actual-file probe. |
| GET `/{attachmentId}` | Safe metadata/availability, never StorageKey/path/proof. |
| GET `/by-client/{clientPhotoId}` | Recover durable identity after an unknown reservation/upload outcome; 404 is not proof an in-flight request will never commit. |
| POST `/{attachmentId}/recover` | No body needed; proof header for unaccepted work. Claims expired/free lease and reconciles actual stage/final bytes, rechecking current B/A/attempt. Never fabricates missing bytes. |
| GET `/{attachmentId}/content` | Current task-read scope; actual hash/size/image verification, bounded bytes, verified MIME, generated safe download filename, no-store/nosniff, no ranges. Accepted history does not require current A/proof. |

SHA-256 is normalized to uppercase. Same ClientPhotoId + identical scope/source/content
descriptor returns the same AttachmentId; changed bytes/source/task/item/attempt conflicts.
An accepted PUT retry validates the actual supplied bytes without overwriting a file or
changing first A/B/times. Accepted status/recovery needs current B read authority, not
renewed A eligibility. An unaccepted reservation retry does not grant upload authority;
PUT/recover independently validate A before any acceptance.

Limits: 5 MiB binary; manifest 128 KiB with duplicate property rejection; JPEG/PNG only,
full decoder success, one frame, edge <=6000 and pixels <=12,000,000 before allocation.
PNG chunks require valid lengths/CRC/IEND with no trailing data; JPEG requires SOI/EOI
and successful full decode. Unsupported/corrupt input is not re-encoded or made Ready.
SkiaSharp and matching Linux native assets are pinned at 4.153.3 (MIT); maintain their
security updates and verify native runtime availability for the chosen deployment OS.

Technical resource bounds are five reserved photo identities per item/current attempt
(including pending/failed), ten registered upload attempts per attachment, 20 requests
per minute/current user, four concurrent photo requests per process, two concurrent
decoders and a two-minute request deadline. A durable lease lasts ten minutes, covering
worker death; lease expiry alone never grants acceptance. Failed staging files remain
registered and count toward bounds. There is no automatic slot release or cleanup worker.
Monitor disk space and failed staging backlog before any production activation.

Safe failures include 401/403 current authority, resource 404, immutable identity/source/
attempt/lease conflict 409, request Content-Length over limit 413, binary media 415,
rate/concurrency 429 and storage unavailable/I/O 503. Codes contain no paths, storage keys
or credentials. Source errors are photo errors, not fabricated draft-operation receipts.

## Metadata and locking

Additive migration `20261113153316_R3206ProtectedPhotoReady` creates five tables only:

| Table | Meaning / mutation |
| --- | --- |
| InspectionAttachments | Immutable reservation, scope, source A, expected byte count/hash/type, capture claim, opaque random final key, CreatedAtUtc. Unique (EnvironmentId,ClientPhotoId) and StorageKey. |
| AttachmentUploads | Every randomly keyed stage registered before creating bytes, original streaming actor/family/time. StoredAtUtc can transition NULL to truthful verified/flush time once; other fields immutable. |
| AttachmentAcceptances | Immutable first Ready: matching upload, actual verified length/hash/MIME/encoding/dimensions, first accepting B/family, stored/Ready times. Unique AttachmentId; no business operation/reference is inserted. |
| AttachmentAvailabilities | Mutable state/version, active upload, lease/fence token/expiry, restrictive accepted FK. Ready requires matching accepted identity. |
| AttachmentEvents | Append-only safe state audit, actor/family/server time; atomic with metadata. No proof or storage path. |

Restrictive Task/Item/Attempt/WorkContext/User/Line FKs and native insert scope guards
prevent cross-scope identities. Acceptance native guard matches reservation content and
the upload's durable timestamp. ORM and SQLite/PostgreSQL native triggers protect immutable
facts. Existing tables/rows/keys/triggers are not rebuilt or rewritten. Down migration
is intentionally rejected: use reviewed forward repair or verified coordinated restore.

Every metadata transition uses the existing WorkContextStore protocol: SQLite writer
before reads; PostgreSQL ReadCommitted, sorted requester + A-owner account locks, current
B/family, sorted active-line locks, actual Task, source WorkContext, then availability row.
No planning gate is acquired after Task; no filesystem streaming/rename occurs inside a
DB transaction. Different task/account lock sets racing on the environment-wide client ID
resolve the unique-index winner after rollback under fresh authority. Lease tokens fence
stale writers; no one can relabel an already accepted upload. PR #17's issuance MVCC fence
and legacy Admin security hooks are unchanged.

A normal logout, expiry, refresh or self-password change does not blanket revoke eligible
historical A. Explicit source revoke, Admin password reset, deactivation or role loss
blocks new acceptance in either serialization order. Ready accepted before revoke remains
historical accepted storage and can be read/recovered under current task authorization.
Stored-but-unaccepted bytes remain unaccepted after revocation; no automatic promotion
by a background service can bypass the required B/A checks.

## Filesystem and durability contract

Configure an existing dedicated private **local** root via `InspectionPhotos:Root` /
`InspectionPhotos__Root`. No default, production path or server URL is shipped. Reject
relative/UNC/device/root-drive paths, roots inside or containing ContentRoot, unsupported
OS and symlink/reparse-point ancestors. Linux requires owner-only rwx (no group/other
permissions) on the root. Windows rejects applicable allow-ACEs for broad principals or
identities other than the current service, SYSTEM, Administrators and explicitly configured
`InspectionPhotos:AllowedStoragePrincipals` SID entries (e.g. an approved backup identity).
Broad Everyone/Users/AuthenticatedUsers/Guest/Anonymous grants are always rejected.
Restrict volume and all ancestors using OS ACLs;
API service and approved backup identities only. Host administrators with physical/DB
control are outside the application's authorization boundary. This is not an object-store,
FTP, SMB, public media or multi-writer network-filesystem adapter.

Only server-generated lowercase 32-hex keys become flat `.stage`/`.blob` leaves. Linux
pins directory descriptors with openat/O_DIRECTORY/O_NOFOLLOW; leaf opens use O_NOFOLLOW,
exclusive stage creation, bounded file reads and flock to reject active writers.
Windows pins each ancestor with OPEN_REPARSE_POINT/no delete sharing, rejects reparse
handles, uses CREATE_NEW for stages, and renames a verified handle with non-replacing
FILE_RENAME_INFO. Read verification does not follow a leaf symlink. Existing final objects
are verified, never replaced. No API deletes accepted or failed files.

Streaming flushes the stage using FileStream.Flush(true). Reconciliation flushes existing
verified files again before Stored. Linux fsyncs the file and directory and promotes with
renameat2(RENAME_NOREPLACE), then fsyncs the directory. Windows flushes file handles and
uses same-volume non-overwriting rename; it has no portable directory-fsync guarantee.
Filesystem API success is not a universal promise about controller caches, sudden power
loss, disk failure or future availability. Production OS/filesystem, power-loss and
coordinated restore qualification remain mandatory external gates. This PR does not claim
hardware power-cut durability testing. Keep root mapping stable across restarts/restores.

## Transitions and crash matrix

Reservation → AwaitingBytes → Receiving (committed stage identity + lease) → Stored
(verified actual bytes + flush, committed timestamp) → non-overwriting final promotion →
Ready (first acceptance + availability + audit commit). Errors leave Failed or durable
Receiving/Stored until current authorized reconciliation. Recovery never trusts state alone.

| Interruption | Durable facts and recovery |
| --- | --- |
| Before stage/partial stream | Upload/key already registered. No Ready. Missing/partial file fails validation; a new upload attempt gets a new key. |
| After stage flush, before Stored commit | Receiving/key survives. After free/expired lease, current B with eligible A verifies actual bytes and flushes again; StoredAt is recovery verification time, not invented historical time. |
| After Stored commit, before rename | Valid stage promotes; timestamp stays immutable. |
| After rename, before Ready transaction/commit | Valid final bytes reconcile, but current B/A/attempt must still pass. First accepting B is the actual recovery caller. |
| Ready commit succeeds, response lost | Status/by-client/exact retry returns the original immutable A/B/hash/times; no second acceptance or overwrite. |
| Worker dies with Receiving lease | No process-local success flag. Fresh host reads DB/volume; active lease conflicts, expired lease can be claimed. Stale worker cannot commit through a replaced fence. |
| Source security revoked before final acceptance | No Ready/acceptance, even if bytes are already stored/promoted. Physical bytes remain tracked and inaccessible as accepted content. |
| Missing/corrupt accepted bytes | Fail closed; availability becomes MissingUnexpected/Corrupt. Original accepted provenance/ReadyAt retained. No automatic overwrite/repair or invented retention reason. |
| Disk/permission/flush/promotion/DB/audit failure | Safe failure, no false success; database rollback does not roll back files. Registered file identity remains for recovery. |

DB and filesystem are **not atomic together**. There is no accepted-file delete path, F3
deletion intent, orphan sweeper, reference counter or automatic restore repair in this PR.

## Verification and independent review

Tests use real generated identities, issued proofs, valid encoded images, isolated private
files and disposable databases. Coverage includes byte-identical retrieval, source A vs
first B, immutable retries, changed content/scope, actual mismatched item/attempt pairs,
Removed during streaming, body/rate/slot limits, type/hash/size/dimension rejection,
expired-writer fencing, normal A lifecycle, both orders of four security actions, B logout
after streaming, native no-overwrite/path controls, fresh-host recovery, Ready commit lost
response, missing/corrupt files, metadata/audit/commit faults, populated migrations and
whole old-row/schema/FK/index fingerprints. Fault injection is not a physical disk-full
or hardware power-cut experiment. Fresh-host tests recreate services/file adapter against
the same DB/volume; they do not claim an OS reboot or production restore drill.

Linux symlink/openat tests are explicit platform SKIPs on Windows and execute in Ubuntu
backend CI. PostgreSQL opt-in SKIPs are never counted as executed; dedicated CI uses real
PostgreSQL 17 and includes `PhotoPostgresTests` alongside all existing security regressions.
Exact final-HEAD Push/PR run URLs, counts and adversarial review findings belong in the PR
delivery evidence. Full backend Release and Web lint/tests/typecheck/build are required.

Deferred: draft attachment references/source dependency manifests, NG/NG→OK completeness,
UnableToMeasureNG/current-attempt evidence, PQE reference purpose/permissions, F2 Submit,
formal revision freezing, five-year retention, F3 reference-aware deletion/recovery,
Android/offline/photo UI and production rollout. Shared `canExecute=false` remains true.
