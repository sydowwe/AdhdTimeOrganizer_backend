# TEST-18 — `CalendarProvisioner`'s race branch

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

## Why

`application/service/taskPlanner/CalendarProvisioner.cs:20-40` handles the real race: two devices
starting the same morning's first task on a day with no calendar row. `(user_id, date)` is unique, so
one insert loses:

```csharp
catch (DbUpdateException)
{
    dbContext.Entry(calendar).State = EntityState.Detached;   // <- the line under test
    var winner = await FindAsync(userId, date, ct);
    if (winner is null) throw;
    return winner;
}
```

`docs/testing.md` lists this as a known gap and names the failure precisely: *"A regression that forgets
the detach shows up as the **endpoint's** next save re-sending a duplicate insert, which no current test
reaches."* That is the point — the detach is not about this method's return value, it is about what the
caller's own `SaveChanges` does afterwards. All seven `LazyCalendarCreationTests` are single-writer and
cannot see it.

## Where

`AdhdTimeOrganizer.IntegrationTests/Endpoints/LazyCalendarCreationTests.cs`.

## Scenario A — two concurrent creates (the integration version)

Pick a date well outside the seeded years (e.g. `2029-06-14`) and confirm no calendar row exists.

Fire two `POST /api/planner-task` requests concurrently, both naming the day by **`date`** rather than
`calendarId`, from two `HttpClient` instances for the same user:

```csharp
var bodies = new[] { 8, 10 }.Select(hour => new
{
    StartTime = new { Hours = hour, Minutes = 0 },
    EndTime   = new { Hours = hour + 1, Minutes = 0 },
    IsBackground = false,
    ActivityId = activityId,
    Status = "NotStarted",
    Date = "2029-06-14"
});

var responses = await Task.WhenAll(bodies.Select(b =>
    CreateUserRoleClient().PostAsJsonAsync("api/planner-task", b, JsonOpts, CancellationToken)));
```

Assert:

- **Both** responses are 201. Losing the race is not the user's problem — the comment in the source says
  so: *"the loser reads it rather than failing a write the user has every right to make."*
- **Exactly one** calendar row exists for that `(user, date)`.
- **Both** planner tasks exist, and both point at that same `calendarId`. This is the assertion that
  fails if the detach is removed: the loser's endpoint save re-sends the failed calendar insert and
  takes its own task down with it.

A single test run may not actually collide. Repeat the pair over several distinct dates in a `[Theory]`
to raise the odds, and treat the test primarily as a regression pin for scenario B, which is
deterministic.

## Scenario B — the detach, deterministically (write this one first)

Drive `ICalendarProvisioner` directly and force the loss rather than racing for it:

1. Resolve `ICalendarProvisioner` and a `DbContext` from **one** scope.
2. From a **second, separate** context, insert the calendar row for `(userId, date)` and commit. The
   winner now exists and the first scope knows nothing about it.
3. Call `EnsureForDateAsync(userId, date, ct)` on the first scope. Its insert hits the unique index and
   throws `DbUpdateException`; the catch runs.
4. Assert:
   - the returned `Calendar` has the **winner's** id;
   - `dbContext.ChangeTracker.Entries<Calendar>()` contains **no** entry in state `Added` — the detach
     worked;
   - now stage an unrelated write on that same context (add a `PlannerTask` on the winner's calendar) and
     `SaveChangesAsync`. It must succeed. **Without the detach this throws**, because the failed calendar
     insert is still `Added` and gets re-sent. This is the assertion the known gap is about.

## Scenario C — hand-scoping

`FindAsync` uses `IgnoreQueryFilters()` and an explicit `c.UserId == userId`, deliberately: the
provisioner is *told* which user to act for, and a filtered read would find nothing and try to insert a
duplicate. `CalendarSeeder` does the same for the same reason.

Call `EnsureForDateAsync(userIdB, date, ct)` from a scope whose ambient logged user is **A**, where B
already has a row for that date. Assert it returns **B's existing row** rather than inserting a second
one. Then assert the mirror: with no row for B, it creates exactly one, owned by B.

## Scenario D — the shared metadata

Already covered by `ALazilyCreatedDay_CarriesTheSameMetadataTheSeederWouldHaveGivenIt`. Do not duplicate
it — but if you extend `CalendarDayFactory`, that theory is the one to extend, not this file.
