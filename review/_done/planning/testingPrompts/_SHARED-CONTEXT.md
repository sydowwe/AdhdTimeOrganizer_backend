# Shared context — Planning test prompts

**Read this before any sibling prompt in this folder.** Every prompt assumes it and does not repeat it.

These prompts were generated from a point-in-time review (`review/02-findings.md`) and are disposable.
The real tests live in `AdhdTimeOrganizer.IntegrationTests`. Delete a prompt once its test exists.

## Where the tests go

| Kind of test | Folder | Base class |
|---|---|---|
| Pure rules over hand-built inputs, no database | `AdhdTimeOrganizer.IntegrationTests/Services/` | none — plain xUnit class |
| Anything touching HTTP, the query filter, the FK graph or a timezone | `AdhdTimeOrganizer.IntegrationTests/Endpoints/` | `PostgresTestBase` |
| The portal `Reminder` **and** the `Sydowwe.Reminders` module surface it registers into | `AdhdTimeOrganizer.IntegrationTests/Reminders/` | `PostgresTestBase` |

Do **not** create a test project inside `AdhdTimeOrganizer.Planning` — the slice deliberately has none.

## The skeleton

```csharp
using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

[Collection("Postgres")]
public class MyThingTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    // Optional: runs once for the class, before any test.
    protected override async Task SeedAsync(DbContext db) { /* … */ }

    [Fact(DisplayName = "Says what the rule is, not what the method does")]
    public async Task TheRule_Holds()
    {
        await using var db = CreateDbContext();
        // arrange via PlanningTestSeedHelper

        var response = await CreateUserRoleClient()
            .PostAsJsonAsync("api/…", new { /* … */ }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verifyDb = CreateDbContext();   // fresh context — never assert off a stale one
        // assert on rows
    }
}
```

Members `PostgresTestBase` gives you:

- `CreateClient()` — authenticated, default role set.
- `CreateUserRoleClient()` — authenticated as a plain **User**. Use this by default; it is the role that
  proves an ordinary person can reach the route.
- An anonymous client for 401 assertions — follow whatever `TaskPlannerDayTemplatePinTests.
  Unauthenticated_Returns401` does; copy it rather than inventing one.
- A second-user client for cross-user assertions — copy `DayPlanEndpointTests.
  DayPlan_NeverReachesAnotherUsersDay`.
- `CreateDbContext()` — a fresh `DbContext` bound to the same container. **Always open a new one to
  verify**; asserting off the context you seeded with reads the change tracker, not the database.
- `JsonOpts` — the serializer options the app uses. Enums go over the wire as **strings**
  (`Status = "Completed"`), times as `{ Hours, Minutes }` (`TimeDto`).
- `CancellationToken` / `TestContext.Current.CancellationToken` — pass one to every async call.

## Route prefix

Every route is prefixed `api` (`Program.cs`: `config.Endpoints.RoutePrefix = "api"`). Write
`"api/planner-task"`, not `"/planner-task"`.

## Seeding

Use `PlanningTestSeedHelper` (`Endpoints/PlanningTestSeedHelper.cs`) — hand-rolling rows trips
`calendar (user_id, date)` unique or `planner_task.calendar_id` `Restrict` for reasons unrelated to
your test:

```csharp
SeedActivityAsync(db, name, userId = TestUserId, ct)
SeedCalendarAsync(...)            // one (user, date) day
SeedPlannerTaskAsync(...)
SeedTaskImportanceAsync(...)
SeedTaskPlannerDayTemplateAsync(...)
SeedTemplatePlannerTaskAsync(...)
SeedRepeatingPlannerTaskAsync(...)
```

For reminders use `ReminderSeedHelper` (`Reminders/ReminderSeedHelper.cs`) — it creates the portal
`Reminder` **and** the module-side `ReminderDefinition`. A reminder test that writes only the portal
row is testing half the seam.

Every helper takes a `userId`; pass a second user's id to build the cross-user half of a scenario.

## House rules for these tests

1. **Assert on rows, not on routes.** Nearly everything load-bearing in Planning answers `200` when it
   is broken: a batch that wrote to the wrong day, a day plan showing someone else's tasks, a streak
   field left null, a reminder whose module-side definition was never cancelled. Re-read the row.
2. **Every cross-day or batched surface gets a second user**, and you assert that user's rows are
   *untouched* — not merely that the response omitted them.
3. **Name tests after the rule.** `Streak_DayWhereEverythingWasSkipped_DoesNotCount`, not
   `TestEvaluate3`. Use `[Fact(DisplayName = "…")]` for anything whose reasoning is not obvious.
4. **Do not tag `[Trait("Status","KnownGap")]` unless the test is genuinely expected to fail** and the
   behaviour is a decision nobody has made yet. This repo's convention is a *green* regression pin with
   a comment explaining the current behaviour and naming the assertion to flip — see
   `PlannerTaskStatusAndSpanTests.CreatingAnOvernightTask_IsRejected` — which is the convention working as
   intended: it was that green pin on the *old* behaviour until `CQ-14` was fixed on 2026-09-08, and the
   named assertion was then flipped in place rather than the test being rewritten. Prefer that.
   Several prompts here describe defects; if you are writing the test **before** the fix, write it green
   against current behaviour with a `// FINDING: …` comment, or red-and-tagged — but not silently red.
5. **A new user-scoped entity fails `UserDataExportAndDeletionTests` until it is classified** as exported
   or as a known gap. That is the test working, not a flake.
6. **No `Thread.Sleep`, no wall-clock dependence.** The two pure domain services take `today` as a
   parameter for exactly this reason. For endpoints that read `DateTime.UtcNow`, seed relative to
   `DateOnly.FromDateTime(DateTime.UtcNow)` rather than a literal date.

## Things to know about this slice before you write anything

- **`Calendar` rows are seeded for whole years** (`CalendarSeeder`, this year + next), so a date inside
  that window already has a row for the test user. Pick dates well outside it (e.g. 2029) when you need
  "a day with no calendar".
- **Reads are scoped by a global query filter** on every `IEntityWithUser`, whose predicate is
  `ScopeUserId == null || e.UserId == ScopeUserId` — so an **unauthenticated** context sees everything.
  If you assert scoping, do it through an authenticated HTTP client, not through a raw `CreateDbContext()`.
- **Batched endpoints are deliberately not transactional.** They answer `200` with
  `results[] = [{ calendarId, succeeded }]`, one per requested id, **in request order**. A day the caller
  does not own is `succeeded: false`, never 403.
- **`TaskImportance.OptionalMarkerValue` is 666 and `CriticalMarkerValue` is 999**, and `(user_id,
  importance)` is unique — so reordering ranks needs a temporary out-of-range value.
- **`PlannerTask` has three sibling tables** (`PlannerTask`, `RepeatingPlannerTask`,
  `TemplatePlannerTask`) sharing a C# base but not a table. Anything covering "every planner task" needs
  three assertions.
