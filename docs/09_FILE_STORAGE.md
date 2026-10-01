# 09 — Inspection Image / File Storage

## Recommendation
Do **not** save inspection images as large BLOBs in the main relational database.

Use two layers:
1. Relational DB stores attachment metadata and a stable storage key.
2. File storage server stores the actual bytes.

## Existing FTP server
An existing FTP server can be used as a storage backend if required, but clients should not receive FTP credentials and should not depend on `ftp://` URLs.

Preferred flow:

```text
Android/Web
   ↓ HTTPS multipart upload
ASP.NET Core API
   ↓ storage adapter
FTP / file server

DB stores:
attachment id
storage key
filename
content type
size/hash
uploader/time
```

For viewing:

```text
Browser/Android
   ↓ authenticated HTTPS
GET /api/attachments/{id}
   ↓
API authorization
   ↓
file storage
```

Alternative: expose the same file storage through an internal HTTPS web server/reverse proxy and let the backend issue an authorized URL. Avoid hard-coding the server host into database rows.

## Why store a `StorageKey` instead of full URL
A stable key such as:

```text
patrol/2026/10/XJ20261001000123/item-005/550e8400.jpg
```

lets the storage host/path change without rewriting historical DB rows.

## Suggested upload rules (final values TBD)
- allow JPG/JPEG/PNG initially
- validate MIME type and actual file signature
- randomize stored filename / use attachment ID
- keep original filename only as metadata
- calculate SHA-256
- set configurable image size and count limits
- do not expose arbitrary filesystem paths
