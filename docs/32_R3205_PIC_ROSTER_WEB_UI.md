# R32-05 Admin PIC roster Web management

The Web page at `/pic-roster` exposes the existing global roster under System
Management. It follows the [roster API contract](30_R3205_PIC_ROSTER.md) and the
[NG selection boundary](31_R3205_NG_PIC_SELECTIONS.md). The implementation
baseline is merged main `9ce06aabcd28c20ba2b2db7a0a912ea526c5d8db` and
[merged-main CI](https://github.com/peterl-ai/IPQC-system/actions/runs/44349927762).

## Integration audit and behavior

- `PicRosterEndpoints`, `PicRosterContracts`, `PicRosterService` and
  `IpqcPermissions` provide current-family Admin policies, bounded `hasMore`
  pages and immutable RequestId receipts. List order is by server PicId; history
  order is by descending revision. In a roster of duplicate names, the page
  displays each stable PIC ID. History shows the stored actor, original fields,
  original RequestId and New York timestamp for each revision.
- Unlike User Management pagination, the roster has no total. The page sends
  the active filter to the server, pages using `hasMore`, and distinguishes
  an empty successful response from a failed read. Create sets active status
  explicitly, edit sends only Name and Department, and lifecycle changes
  require confirmation. Text follows the backend's raw UTF-16 and Unicode
  scalar bounds. The interface is available in English and Simplified Chinese.
- The menu and route check current Admin authority and `PicRoster.Read`.
  Writes also check `PicRoster.Write`. The roster opts into an actor check in
  `authFetch` immediately before transport dispatch, including after a read's
  session refresh. Replacement tokens are published after `/api/me` resolves
  their user. Mutations never resend on an automatic authentication retry.
  Server policy and current-family validation remain authoritative.

## Request recovery

One pending command per Admin per browser tab is retained in `sessionStorage`
before dispatch. It contains only the original RequestId, Admin UserId, action,
target, expected revision and expected values. The UI verifies persistence
before a fresh send or deliberate retry; unreadable or inconsistent storage
blocks writes. Reopening the page in the same tab restores the pending
command. Tab closure or clearing browser storage ends that recovery window.

Only a matching original receipt counts as acceptance. The UI displays that
receipt separately, refreshes the current PIC and roster, then clears pending
storage. A failed refresh leaves the verified receipt and the recovery command
available. On timeout, disconnection, a malformed response or a transient
server error, an explicit retry uses the same request body and RequestId.
A receipt lookup returning 404 leaves an unresolved request in place.

A 409 triggers a current-record read where a target exists and a RequestId
lookup. For an existing PIC, the original operation may be retired after that
lookup returns 404 and the current record proves the frozen revision is already
past or the frozen action is a no-op at that revision. A replacement edit
requires an explicit review of current data and a separate save. A replacement
lifecycle action requires a new confirmation. In ambiguous cases the original
request remains available for receipt lookup or exact retry.

## Verification

The Web tests cover the C# response shapes, payloads, permissions and direct
route navigation, duplicate PICs, current state versus audit history,
lost-response recovery, late acceptance after a receipt 404, and stale-revision
review. They exercise account switching, duplicate clicks, unreadable storage
and HTTP failure paths. The local Web suite passed 166 tests across 23 files.
A Chrome smoke check using intercepted synthetic responses verified the roster
table, populated Edit PIC dialog and revision history with the original actor,
RequestId and New York time.

Local `npm ci`, `npm run lint`, `npm run test`, `npm run typecheck` and
`npm run build` exited 0. The existing Vite large-chunk warning also appeared.
On Windows, backend restore and Release build exited 0 with no build warnings
or errors. The complete backend test command exited 0 with 1,159 passed,
745 skipped and zero failed. The 745 skips comprise 743 PostgreSQL opt-in
cases and two Linux-specific photo-storage cases. This machine lacked an
available PostgreSQL provider, so real PostgreSQL verification runs in CI.

The scope is Web Admin reference-data management. Shared execution and formal
submission remain disabled (`canExecute=false`, `canSubmit=false`). The
historical intermittent SQLite HTTP 500, trace `0HNQ4T7TVDUH5`, remains
unexplained.
