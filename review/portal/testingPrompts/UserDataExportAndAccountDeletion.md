# TEST-9 — User data export / account deletion

## Context

Same infra as the other portal integration tests: `PostgresTestBase`, `CreateUserRoleClient()`. Two
host-level endpoints, both in `AdhdTimeOrganizer/application/endpoint/user/`:
`read/GetUserDataExportEndpoint.cs` and `command/settings/DeleteUserAccountEndpoint.cs` (the latter
extends `framework/Sydowwe.Framework/application/endpoint/user/command/settings/
BaseDeleteUserAccountEndpoint.cs` — read the base first, most of the behavior likely lives there).

This is GDPR-relevant surface (export = data portability, deletion = right to erasure) with **zero**
test files today per the prior review pass — treat it as the highest-severity gap in this backlog.

## What to write

Add e.g. `Endpoints/UserDataExportAndDeletionTests.cs`:
- **Export**: authenticated user gets back their own data only; response contains no other user's
  rows; unauthenticated is rejected; confirm the export actually walks every slice's user-scoped data
  (Activity, TodoLists, History, Planning, Routines, Tracking, ActivityProfiles) rather than only the
  host's own tables — this is the kind of cross-slice completeness gap a route-only test would miss.
- **Deletion**: deleting the account actually removes (or the codebase's stated retention policy
  correctly anonymizes/retains, whichever `BaseDeleteUserAccountEndpoint` implements — read it, don't
  assume) the user's rows across every slice with `IEntityWithUser` data, not just the host's `User`
  row. Check for orphaned FK rows left behind in slice tables after deletion — a missed cascade here is
  exactly the kind of thing that compiles fine and fails silently per this project's own "Silent
  failures" doctrine in `CLAUDE.md`.
- **Auth**: deletion requires the caller's own session (can't delete another user's account by id);
  confirm whether it requires re-authentication/password confirmation and test that path if so.
- **Post-deletion**: confirm the deleted user's existing JWT/refresh tokens stop working (session
  revocation), and that re-registering with the same email works if the deletion actually purges rather
  than soft-deletes.

## Out of scope

Don't test the generic auth/session machinery (`RefreshTokenServiceTests.cs`, `AuthFunctionalTests`
already own that) beyond confirming tokens are revoked post-deletion.
