# TEST-4 — mapping create accepts another user's `activityId` / `roleId` / `categoryId`

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today:** `TrackingMappingCrudAuthMatrixTests` covers create (stamps the caller's `UserId`),
update and delete of a foreign id, and grid exclusion — for both mapping tables. That matrix proves you
cannot **read or modify** another user's row. It does not prove you cannot **point at one**, and that is
the gap (`SEC-2`).

## The endpoints

| Route | Source | DTO |
|---|---|---|
| `POST /api/activity-tracking/desktop/settings/tracker-desktop-mapping-by-pattern` | `endpoint/desktop/command/CreateTrackerDesktopMappingEndpoint.cs` (`BaseCreateEndpoint`) | `CreateTrackerDesktopMappingRequest` |
| `POST /api/activity-tracking/android/settings/tracker-android-mapping-by-pattern` | `endpoint/android/command/CreateTrackerAndroidMappingEndpoint.cs` | `CreateTrackerAndroidMappingRequest` |

Validators: `CreateTrackerDesktopMappingValidator` / `CreateTrackerAndroidMappingValidator` — pattern
⇒ match-type, at least one pattern, exactly one target group, `isIgnored` only ever true.
**No ownership check on any FK.**

Request (desktop; android swaps the pattern fields for `packageName` / `appLabel`):

```jsonc
{
  "processName": "code.exe", "processNameMatchType": 0,  // PatternMatchType.Exact
  "isActive": true,
  "activityId": 123        // ← the hole; or roleId / categoryId
}
```

## Why the global query filter does not save this

The filter scopes **reads** of `IEntityWithUser` entities. This is a write of a raw `long` into an FK
column whose target row genuinely exists at the database level, so the FK constraint is satisfied and
nothing rejects it. `CreateTrackerDesktopMappingRequest.ToEntity:29-43` copies all four target members
straight through.

The consequence is worse here than in the TodoLists instance of the same hole (`SEC-1` in
`review/todo-lists/02-findings.md`), because a mapping is a **standing rule**: every later desktop
heartbeat that matches the pattern reaches `DesktopActivityHeartbeatEndpoint.cs:87-91` and calls
`attributionSink.AttributeAsync(dbContext, userId, activityId, …)`, writing an attribution row for the
caller against another user's activity and publishing `ActivityTimeRecordedEvent` with it — repeatedly,
indefinitely. Read-back is blocked (the response projection's `Activity` navigation is query-filtered),
so the victim sees nothing and the attacker sees a mapping that merely looks broken.

## Scenarios

Write these in `TrackingMappingCrudAuthMatrixTests.cs` beside the existing
`TrackerDesktopMappingCreateTests` / `TrackerAndroidMappingCreateTests`, or in a new
`Endpoints/TrackingMappingForeignKeyOwnershipTests.cs`.

1. `Create_WithAnotherUsersActivityId_IsRejected` (both tables)
   - Seed a second user (see `_SHARED-CONTEXT.md`), an `ActivityRole` and an `Activity` **owned by
     them**.
   - `POST` a mapping as the test user with that `activityId`.
   - **Expect 400/404.** *Fails today with 201* — assert the current behaviour under
     `[Trait("Status", "KnownGap")]` naming `SEC-2` if you are not fixing the code in the same pass.
   - Whichever way, also assert through a fresh `CreateDbContext()` that **no row exists** with
     `ActivityId == theirActivityId && UserId == UserId` — that row is the actual damage, and asserting
     on it rather than on the status code is what makes this test worth having.
2. `Create_WithAnotherUsersRoleId_IsRejected` and `Create_WithAnotherUsersCategoryId_IsRejected` —
   same shape; `ActivityRole` and `ActivityCategory` are separately owned and separately unchecked.
3. `Create_WithOwnActivityId_Succeeds` — the control. Without it, a fix that rejects everything passes
   cases 1–2.
4. `Update_CannotRepointToAnotherUsersActivity` — **note `CQ-10`**:
   `UpdateTrackerDesktopMappingRequest.UpdateEntity:20-29` writes only the pattern fields and
   `IsActive`, so the target cannot be changed at all through `PUT` today. Write this case as a
   `[Trait("Status", "KnownGap")]` documenting that update silently ignores any target member sent,
   and convert it into a real ownership case when `CQ-10` adds the fields. Do not skip it — this is
   exactly the seam where the ownership check will be forgotten a second time.
5. **The consequence test** — `Heartbeat_DoesNotAttributeToAnotherUsersActivity`. Seed the foreign
   mapping **directly in the database** (bypassing the endpoint, so it is independent of whether case 1
   is fixed), post a matching desktop heartbeat with an extension token, and assert no `ActivityHistory`
   row was written against the foreign activity. Copy the end-to-end shape from
   `ExtensionActivityTrackingTests.Heartbeat_MappedEntry_AttributesTimeAndCompletesPlannerTask:198`,
   which already seeds a role, an activity and an exact-match mapping.

## Fixtures / helpers to reuse

- `TrackingMappingCrudAuthMatrixTests:75` shows the seeding note that matters here —
  `BaseWithUserEntitySaveChangesAsync` only fills a `UserId` of 0, so **set `UserId` explicitly** when
  seeding rows for the other user.
- The activity/role seed block: `ExtensionActivityTrackingTests:206-222`.
- Respect the mapping check constraints when seeding: exactly one of `is_ignored = true` /
  `activity_id` / `role_id`-or-`category_id`, and a distinct pattern tuple per user
  (`NULLS NOT DISTINCT`).
