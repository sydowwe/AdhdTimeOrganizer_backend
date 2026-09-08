# TEST-16 — concurrent `GET /planner/settings` must not create two rows

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

## The defect (`SEC-3`)

`AdhdTimeOrganizer.Planning/application/endpoint/plannerSettings/GetPlannerSettingsEndpoint.cs:26-33`
is a read-then-insert with no uniqueness backstop:

```csharp
var settings = await dbContext.Set<UserPlannerSettings>().FirstOrDefaultAsync(s => s.UserId == userId, ct);
if (settings == null)
{
    settings = new UserPlannerSettings { UserId = userId };
    await dbContext.Set<UserPlannerSettings>().AddAsync(settings, ct);
    await dbContext.SaveChangesAsync(ct);
}
```

`UserPlannerSettingsConfiguration.cs:13` calls `IsOneWithOneUser<UserPlannerSettings>()`, and that
Framework helper declares a **non-unique** `HasIndex(r => r.UserId)` before the one-to-one FK
(`framework/Sydowwe.Framework/infrastructure/persistence/configuration/extensions/EntityWithUserBuilderExtensions.cs:31-35`),
so `user_planner_settings.user_id` has no unique constraint. `docs/domain-map.md` and `docs/testing.md`
both already concede this in prose; nothing tests it.

`UpdatePlannerSettingsEndpoint` has the identical read-then-insert shape.

The existing pins — `UserPlannerSettingsTests.RepeatedGet_CreatesExactlyOneRow` and
`GetThenPut_StillOnlyOneRow` — are **sequential** and cannot reach the race.

## Where

`AdhdTimeOrganizer.IntegrationTests/Endpoints/UserPlannerSettingsTests.cs`.

## Scenarios

### A — concurrent GET

Pick a user with **no** settings row (delete it first if the per-user default seeder made one — check
whether `UserPlannerSettingsSeeder` ran for the test user and clear the row in `SeedAsync` if so).

Fire N concurrent requests from N clients for the same user:

```csharp
var client = CreateUserRoleClient();
var responses = await Task.WhenAll(Enumerable.Range(0, 8)
    .Select(_ => client.GetAsync("api/planner/settings", CancellationToken)));

responses.Should().OnlyContain(r => r.IsSuccessStatusCode);

await using var db = CreateDbContext();
var rows = await db.Set<UserPlannerSettings>().IgnoreQueryFilters()
    .CountAsync(s => s.UserId == UserId, CancellationToken);
rows.Should().Be(1, "GET /planner/settings is a get-or-create over a one-row-per-user table");
```

Note `IgnoreQueryFilters()` is not strictly needed on a raw context (`ScopeUserId` is null there, so the
filter is a no-op) but makes the intent explicit and survives a future filter change.

A single connection may serialise these enough to hide the race. If it does, drive it from two distinct
`HttpClient` instances and, if it *still* passes reliably, keep the test as a cheap regression pin and
add scenario C below, which is deterministic.

### B — concurrent GET + PUT

Same shape, one `GET` and one `PUT /api/planner/settings` issued together. Same assertion: exactly one
row.

### C — the deterministic version (recommended as the primary test)

Skip the race entirely and assert the **constraint**, which is what actually fixes this:

```csharp
await using var db = CreateDbContext();
db.Set<UserPlannerSettings>().Add(new UserPlannerSettings { UserId = UserId });
db.Set<UserPlannerSettings>().Add(new UserPlannerSettings { UserId = UserId });

var act = async () => await db.SaveChangesAsync(CancellationToken);

await act.Should().ThrowAsync<DbUpdateException>(
    "user_planner_settings.user_id must be unique — the read-then-insert in " +
    "GetPlannerSettingsEndpoint has no other backstop");
```

Expected to **fail** today: both rows insert cleanly. This is the assertion that turns green when the
unique index lands.

## The fix this test drives

1. `builder.HasIndex(s => s.UserId).IsUnique();` in `UserPlannerSettingsConfiguration` — the Framework
   helper deliberately does not do it, so the slice must.
2. Migration, in two steps — see `MIG-1`. Existing duplicates must be collapsed (keep the lowest id per
   user) before `CREATE UNIQUE INDEX CONCURRENTLY` can succeed.
3. Catch `DbUpdateException` in both endpoints and re-read the winner, copying
   `CalendarProvisioner.EnsureForDateAsync:20-40` — which already solves exactly this problem for
   `calendar (user_id, date)`, detach included.
