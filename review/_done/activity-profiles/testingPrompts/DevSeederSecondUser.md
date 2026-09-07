# TEST-8 — the dev profile seeders must seed every user, not just the first

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

Pins **CQ-3 🟠**.

> ✅ **Done 2026-09-04** — `AdhdTimeOrganizer.IntegrationTests/Seeding/DevProfileSeederTests.cs`, 12 tests,
> landed with the fix. All four scenarios below, plus a registration fact (`CQ-12`) and a
> `WithoutTheDefaultLookups_…` case for `CQ-4`. It lives in `Seeding/` next to
> `PerUserDefaultSeederTests` rather than in `Endpoints/`, since nothing about it goes over HTTP.
>
> **One thing this prompt could not have known:** `SEC-1`'s structural fix landed first, so
> `AppQueryFilters.ActivityProfileUserScope` now scopes profile reads — including the seeder's own guard.
> Every ordinary test context has a non-null `ScopeUserId` (`FakeLoggedUserService.IsAuthenticated` is
> hard-coded `true`), which hides user A's rows from the guard and makes the bug *undetectable* through
> the factories the base class hands out. The suite therefore builds its own host with an
> unauthenticated `ILoggedUserService`, which is what dev seeding runs as in production — and is the
> first behavioural exercise of the `ScopeUserId == null ||` escape (`MIG-1`), previously asserted only
> on the predicate.

## The defect

Three `IPerUserDevSeeder` implementations open with a **global** existence check:

| File | Line | Guard |
|---|---|---|
| `infrastructure/persistence/seeder/dev/ActivityBacklogProfileSeeder.cs` | 26 | `if (await dbContext.Set<ActivityBacklogProfile>().AnyAsync()) return;` |
| `.../ActivityProjectProfileSeeder.cs` | 25 | same |
| `.../ActivityBucketListProfileSeeder.cs` | 25 | same |

`.../MemoryAnchorSeeder.cs:24` is the correct shape: `AnyAsync(ma => ma.UserId == userId)`.

So on a dev database with two users, the second user gets no profiles — silently, because the guard
returns before the `LogWarning` path that would otherwise say why.

## How to test a per-user dev seeder

Follow `AdhdTimeOrganizer.IntegrationTests/Seeding/PerUserDefaultSeederTests.cs` — it resolves the
seeders from a scope and calls them directly rather than going through HTTP. The dev seeders are
`IPerUserDevSeeder, IScopedService`, so:

```csharp
using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
var seeder = scope.ServiceProvider.GetServices<IPerUserDevSeeder>()
    .Single(s => s.SeederName == "ActivityBacklogProfile");
await seeder.SeedForUser(userId);
```

`ActivityProjectProfileSeeder.SeederName` was `"ActivityDiyProfile"` when this prompt was written;
`CQ-12` renamed it to **`"ActivityProjectProfile"`** on 2026-09-04, so all three keys now match their
entity names.

⚠ These seeders match activities **by name** (`GetActivityId("Gaming Session")` etc.,
`ActivityBacklogProfileSeeder.cs:75-84`). Your two test users each need activities with those exact
names, or the seeder legitimately produces zero rows and the test passes vacuously. Seed at least
`"Gaming Session"` and `"Meditation"` per user and assert on **those** ids.

⚠ Do **not** call `TruncateTable()` in a test — it is `TRUNCATE ... CASCADE` against a shared
container database and will take other suites' rows with it.

## Scenarios

1. **Two users, both seeded.** Create a second user through `UserManager`, give both users the named
   activities, run `SeedForUser` for user A then user B, and assert **both** have backlog profile
   rows in a fresh context. Fails today at the second call.
2. **Idempotence per user.** Run `SeedForUser(userA)` twice; the row count for A does not change.
   This is what the guard was *trying* to do, and the fix must not lose it.
3. **A user with no activities is a warning, not a crash.** `SeedForUser` on a user with no
   activities logs and returns (`:33-37`); assert no rows and no exception.
4. Repeat 1–2 for the project and bucket-list seeders (a `[Theory]` over the three seeder names is
   fine; the assertion set differs only in the entity type).

## The fix

`if (await dbContext.Set<ActivityBacklogProfile>().AnyAsync(p => p.Activity.UserId == userId)) return;`
— the traversal is needed because these three entities are not `IEntityWithUser`. Note that if
`SEC-1`'s structural fix (a mirrored user-scope query filter on the profiles) lands first, this
query runs with **no ambient user** during seeding and therefore relies on the
`ScopeUserId == null ||` escape being present in that filter — see `MIG-1`. Worth a comment in the
seeder pointing at it.
