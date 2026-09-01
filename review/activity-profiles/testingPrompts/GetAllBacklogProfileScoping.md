# TEST-1 — `GET /api/activity-backlog-profile` must return only the caller's rows

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

Pins **SEC-1 🔴** (`review/activity-profiles/02-findings.md`). This is the highest-value test in the
backlog: the endpoint currently returns **every user's** backlog profiles, and nothing anywhere
notices.

## The endpoint

- Route: `GET /api/activity-backlog-profile` (no route parameter — the kebab-cased entity name).
- File: `AdhdTimeOrganizer.ActivityProfiles/application/endpoint/backlog/query/GetAllActivityBacklogProfileEndpoint.cs`
  — the whole class body is empty; it inherits
  `BaseGetAllEndpoint<ActivityBacklogProfile, ActivityBacklogProfileResponse>`.
- Base: `framework/Sydowwe.Framework/application/endpoint/base/read/BaseGetAllEndpoint.cs` — `:53`
  queries the unfiltered `DbSet`, `:74` defines `Filter` as the identity. **There is no
  `ApplyUserScoping` hook on this base at all** (unlike `BaseGridEndpoint:39`).
- Roles: none declared → the global configurator in `Program.cs:573-577` gives it
  `User, Admin, Root`.

## Response shape

`List<ActivityBacklogProfileResponse>` — `ActivityBacklogProfileResponse.Projection`
(`application/dto/response/ActivityBacklogProfileResponse.cs:49-66`), i.e. the **plain** projection:

```json
[{ "id": 1, "activityId": 7, "activityName": "Gaming Session", "locationTypeId": 3,
   "weatherDependencyId": 4, "energyLevel": "Low", "effortType": "Mental", "minParticipants": 1,
   "maxParticipants": null, "expectedCostTierId": 2, "durationMinutes": 120, "isRepeatable": true,
   "isAnchored": false, "memoryAnchorId": null }]
```

`isAnchored` / `memoryAnchorId` are **always** `false`/`null` on this route by design (the plain
projection cannot reach the anchors) — that is a second, separate expectation worth pinning while you
are here, because nothing records it today.

## Scenarios

Put them in a new `GetAllActivityBacklogProfileTests.cs`, or extend `ActivityProfileGridTests` if you
promote the seed helpers first.

1. **Scoping (the point).** Seed a backlog profile for the default test user and one for a second
   user created through `UserManager`. `GET` as the default user. The returned `id` set must contain
   the caller's profile and **must not** contain the second user's. Assert on the id set, not on the
   count — a count assertion passes when the fixture happens to be empty.
2. **The reverse direction.** Using `CreateFactory(roles: ["User"], userId: secondUserId)`, the same
   `GET` must return the second user's row and not the first's. Without this, a scoping fix that
   accidentally hard-codes one user id still passes.
3. **Completion fields stay at their defaults.** Seed a `MemoryAnchor` against the caller's own
   non-repeatable profile's activity — the grid (`POST /activity-backlog-profile/grid`) reports
   `isAnchored: true` for it, and this route must still report `false`. Reference
   `BucketList_Grid_ReportsAnchoredAndTheLatestAnchorId` (`ActivityProfileGridTests:494`) for the
   grid half of the comparison. Give the test a `DisplayName` saying this is deliberate, and cite
   `docs/domain-map.md:97`.
4. **Auth.** `CreateUnauthenticatedClient()` → 401. `CreateUserRoleClient()` → 200 (the `User` role
   is allowed here; this is not an admin route).

## Fixing it (do this, then write the tests green)

Two changes, both wanted:

```csharp
// GetAllActivityBacklogProfileEndpoint.cs
protected override IQueryable<ActivityBacklogProfile> Filter(IQueryable<ActivityBacklogProfile> query) =>
    query.Where(p => p.Activity.UserId == User.GetId());
```

and, in `AdhdTimeOrganizer/infrastructure/persistence/AppDbContext.cs`, the structural mirror — copy
the `RoutinePeriodCompletion` block at `:321-323` **exactly**, including the `ScopeUserId == null ||`
escape, for all three profiles, under a **new named** constant in `AppQueryFilters` (an unnamed
filter would replace the `ActivityArchived` one already on those entities — see the warning comment
at `:292-295`). If you add the filter, also extend `Infrastructure/NamedQueryFilterTests.cs` with the
new names and re-run `ActivityProfileGridTests` and `ActivityMergeTests` — the merge seam and the
seeders read profiles with no ambient user and depend on the `ScopeUserId == null` escape.

If you are only writing tests, use `[Trait("Status","KnownGap")]` with `SEC-1:` in the `DisplayName`.
