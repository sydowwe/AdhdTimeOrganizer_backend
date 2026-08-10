# TEST-5 — Tests for Activity and the three `Activity*Profile` grids

> **Highest a-priori IDOR risk in the portal.** Read the "Why this one first" section before writing
> anything.

## Context you need (do not rediscover)

**Repo:** `C:/Users/jakub/RiderProjects/AdhdTimeOrganizer`

**Endpoints:** `AdhdTimeOrganizer/application/endpoint/activity/` — subfolders `activity/`,
`role/`, `category/`, `lookup/{expectedCostTier,experienceType,locationType,weatherDependency}/`,
`memoryAnchor/`, and `profile/{backlog,bucketList,project}/`, each split `command/` + `query/`.

**Entities:** `domain/model/entity/activity/` — `Activity`, `ActivityCategory`, `ActivityRole`,
`lookup/*` (4), `memoryAnchor/MemoryAnchor`, `profile/Activity{Backlog,BucketList,Project}Profile`.

**Test project:** `AdhdTimeOrganizer.IntegrationTests`, new file `Endpoints/ActivityEndpointTests.cs`
(and a separate `Endpoints/ActivityProfileGridTests.cs` for the profiles — they have different risk
profiles and different setup).

Read first: `Infrastructure/AppDbContextFixture.cs`, `Infrastructure/AuthTestBase.cs`, and
`Endpoints/ExtensionActivityTrackingTests.cs` as the closest existing HTTP-level example.
xunit v3, FluentAssertions, `[Collection("Postgres")]`, real `Program` over Testcontainers.

Clients: `CreateUserRoleClient()`, `CreateAdminRoleClient()`, `CreateRootRoleClient()`,
`CreateUnauthenticatedClient()`, and `CreateFactory(roles, userId)` for a **second distinct user**
(caller disposes). `CreateDbContext()` to seed and assert; override `SeedAsync(db)`.

**Discover the request/response shapes yourself** — I have not read these endpoints. DTOs live under
`application/dto/request/activity/` and `application/dto/response/activity/`. Routes come from each
endpoint's `Configure()` or the framework base it derives from.

## Why this one first

Per `docs/domain-map.md` → Invariants → Ownership:

> The three `Activity*Profile` entities are **not** `IEntityWithUser` and have **no filter**; they
> are reachable only through their `Activity`, and their grids scope by hand.

Everything else in the portal is protected by `AppDbContext`'s global query filter on
`IEntityWithUser`. These three are not. Their scoping is hand-written inside `ApplyCustomFiltering`
as `p.Activity.UserId == userId`. Meanwhile `ApplyUserScoping` on the Grid/Filter/Sort bases is a
**no-op virtual** — it protects nothing.

So: one forgotten `.Where` in one `ApplyCustomFiltering` override leaks every user's backlog,
project or bucket-list data to any signed-in user, with no second line of defense and no test
covering it. **Also note no portal endpoint currently subclasses any of the 13 abstract framework
test bases in `Sydowwe.Framework.Testing/baseTests/`** — so there is no auth matrix anywhere.

## Scenarios to write

### A. IDOR on the three profile grids — write these first

For **each** of `backlog`, `bucketList`, `project`:

1. Seed user A with an `Activity` + its profile; seed user B likewise.
2. As user B, call the grid/filter endpoint with no filter.
3. Assert **only** user B's profile rows come back. Assert user A's row is absent by id.
4. As user B, call the by-id read for **user A's profile id**. Assert 404 (or 403) — never 200.
5. As user B, attempt update and delete against user A's profile id. Assert non-2xx and assert from
   a fresh `CreateDbContext()` that user A's row is **unchanged**.

Step 5 matters most: a read leak is bad, a cross-user *write* is worse, and the profile entities have
no filter to stop either.

### B. IDOR on the rest of the activity area

Same five-step pattern for `Activity`, `ActivityRole`, `ActivityCategory`, `MemoryAnchor` and each of
the four lookups. These *are* `IEntityWithUser` so the global filter should protect them — the tests
exist to prove the filter is actually applied on every route, including any endpoint that uses
`IgnoreQueryFilters()` or raw SQL.

Prefer subclassing the framework test bases here rather than hand-writing:
`BaseGetByIdEndpointTests`, `BaseGridEndpointTests`, `BaseCreateEndpointTests`,
`BaseUpdateEndpointTests`, `BaseDeleteEndpointTests`, `BaseBatchDeleteEndpointTests`,
`BaseGetSelectOptionsEndpointTests` (in `framework/Sydowwe.Framework.Testing/baseTests/`). They ship
the auth matrix and 404 paths. Being the first in the portal to subclass them, expect some friction —
if a base does not fit, say so explicitly rather than silently hand-rolling.

### C. Auth matrix

For every route: unauthenticated → 401; `User` role → allowed (per CLAUDE.md the default is
User + Admin + Root, because every account in this app is a plain `User`). Flag any activity route
that requires admin — that is almost certainly a bug given the default.

Also: these are **not** `[AllowExtensionClients]` surface, so an extension-client token must be
denied by the `DenyExtensionClients` policy. Add one test proving that.

### D. Uniqueness invariants (`docs/domain-map.md` → Uniqueness)

All per-user: `Activity(UserId, Name)`, `ActivityRole(UserId, Name)`, `ActivityCategory(UserId, Name)`.
Each `Activity*Profile.ActivityId` is unique — 0..1 profile of each kind per activity.

- Creating a duplicate name for the **same** user → clean 409/400, not a 500 from a raw 23505.
- Creating the same name for a **different** user → succeeds. This proves the index is per-user.
- Creating a second profile of the same kind for one activity → rejected.

### E. Delete behavior (`docs/domain-map.md` → Delete behaviour)

- `Activity` → `PlannerTask` is **`Restrict`**: deleting an activity still referenced by a planner
  task must fail cleanly (409), not 500 and not cascade.
- `Activity` → its profiles is **`Cascade`**: deleting an activity removes its profiles. Assert from
  a fresh context that no orphan profile rows survive — they have no user filter, so an orphan is
  both a correctness and a privacy problem.
- `ActivityRole` is **required** on `Activity`; `ActivityCategory` is optional. Assert create fails
  without a role.

### F. `CQ-17` — `CloneActivityEndpoint` and `MemberwiseClone`

`Activity.Clone()` uses `MemberwiseClone`, which copies navigation-collection **references**. It is
safe today only because `CloneActivityEndpoint` fetches via `FindAsync` with no `Include`.

1. Clone an activity that has to-do items, history rows and planner tasks.
2. Assert the clone is a new row with its own id, and that the **source** activity's collections are
   unchanged in the DB.
3. Assert the clone did not acquire the source's children.

This pins the current safe behavior so a future `Include` added to that endpoint fails loudly.

### G. `QuickEditActivityEndpoint`

Assert it cannot be used to change `UserId` (mass-assignment), and that it is subject to the same
IDOR checks as (B).

## Conventions

- AAA, one behavior per test. Fresh `CreateDbContext()` for post-request assertions.
- Two distinct seeded users in the fixture — most of this file is about the boundary between them.
- Assert on ids and counts, never on emails or names.
- If any IDOR test in **A** fails, that is a **🔴 security finding**, not a test to adjust. Stop and
  report it rather than weakening the assertion.
