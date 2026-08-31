# Shared context for every Routines test prompt

Read this first; each prompt in this folder assumes it and does not repeat it.

## Where things live

- **Module under test:** `AdhdTimeOrganizer.Routines` (a vertical slice). Its living docs are
  `AdhdTimeOrganizer.Routines/docs/summary.md`, `docs/domain-map.md` and `docs/testing.md` — read
  `summary.md` before writing anything. The docs' folder paths are **accurate** as of 2026-08-31; what
  is stale is the *namespaces*, which still carry a `todoList` segment the folders no longer have.
  `domain-map.md` says so at the top: trust the path column, not the namespace.
- **Test project:** `AdhdTimeOrganizer.IntegrationTests` (the host's suite — this slice has no test
  project of its own). Routine tests live in `Routines/` (jobs, notifications, freeze) and `Endpoints/`
  (HTTP-level CRUD and constraints); pure-domain tests live in `Services/`.
- **Namespaces still contain `.todoList`** even though the directories do not, e.g.
  `AdhdTimeOrganizer.Routines.domain.model.entity.todoList.RoutineTimePeriod`. Copy the `using` block
  from a neighbouring test rather than guessing.

## Test infrastructure to reuse — do not reinvent

```csharp
[Collection("Postgres")]
public class MyTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private static long OtherUserId => ReminderSeedHelper.OtherUserId;
}
```

- `PostgresTestBase` gives you `CreateDbContext()`, `CreateClient()`, `CreateUserRoleClient()`,
  `Fixture`, `CancellationToken`, `JsonOpts`, and an overridable `SeedAsync(DbContext db)`.
- **A second user must be a real row** — the `user_id` FK is NOT NULL. Call
  `await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken)` and use
  `ReminderSeedHelper.OtherUserId`. An id nobody owns will not do.
- `Fixture.CreateFactory(roles: null, configureServices: …)` builds a host with services swapped —
  see `RoutineTodoListResetJobHandlerTests.cs:349` for replacing `IRoutinePeriodNotificationService`,
  and `:394` for replacing `ILoggedUserService` with the local `UnauthenticatedLoggedUserService`
  so the global query filter goes inert the way it does for a real background job.
- Seed in a **disposed** context, then act, then verify through a **fresh** `CreateDbContext()`. A
  tracked graph left over from seeding will mask a query shape that forgets an `.Include`.
- Assertions use FluentAssertions (`.Should()`), test framework is xUnit.

## Seeding traps specific to this module

- `RoutineTimePeriod` has **two** unique indexes: `(UserId, Text)` **and** `(UserId, LengthInDays)`.
  Every period you seed for one user needs a distinct text *and* a distinct length. Use
  `Text = $"Period {Guid.NewGuid():N}"` and hand-pick lengths.
- `ck_routine_time_period_reset_anchor_day_range` only admits `ResetAnchorDay` 0–7 when
  `LengthInDays <= 7 || LengthInDays % 7 == 0`, and 0–30 otherwise. `ResetAnchorDay = 0` means
  "rolling" and is always safe.
- `StreakGraceDays` must be `< LengthInDays`. `ReminderLeadDays` must be null or `< LengthInDays`.
- `RoutineTodoList` needs a real `Activity`, which needs a real `ActivityRole`. Copy `SeedItemAsync`
  from `RoutineTodoListResetJobHandlerTests.cs:70-100`, or use `TodoListTestSeedHelper.SeedRoutineTimePeriodAsync`
  / `SeedRoutineTodoListAsync` where they fit.
- `RoutinePeriodCompletion` is uniquely keyed on `(TimePeriodId, PeriodStart)`, and
  `ck_routine_period_completion_frozen_at_matches_is_frozen` requires `IsFrozen` and `FrozenAt` to be
  set or null together.

## Running a scheduled job in a test

The jobs are keyed `IScheduledJobHandler`s, not Quartz `IJob`s. Do not boot Quartz — copy `RunJobAsync`
from `RoutineTodoListResetJobHandlerTests.cs:107-121`: open a scope, build the handler with
`ActivatorUtilities.CreateInstance<T>`, and hand it a hand-built `ScheduledJobContext` (the handlers
read nothing off it).

## Conventions

- **Check what already exists before writing.** Four shared suites cover routine routes without
  "Routine" in the filename: `Endpoints/TodoListCrudAuthMatrixTests.cs` (create / update / delete /
  get-by-id on **both** routine entities, with foreign-id and role cases),
  `Endpoints/TodoListDeleteBehaviorTests.cs` (the `Restrict` FK on period delete),
  `Endpoints/TodoListStepsCrudTests.cs` (one routine cross-check of the shared step base) and
  `Endpoints/BaseToggleIsHiddenEndpointTests.cs` (drives the base through the routine endpoint).
  Search by **route** (`api/routine-…`), not by filename.
- **Assert on rows and recomputed state, not on status codes.** Almost every failure mode in this
  module is silent — CLAUDE.md's "Silent failures" section is the house rule and these suites follow
  it. A test that only checks a 200 is not worth adding here.
- **Do not soften an assertion to make a test pass.** If the behaviour is genuinely broken and the fix
  is deferred, tag the test `[Trait("Status", "KnownGap")]` with a comment naming the finding id, and
  write it as expected-failing. No Routines test carries that trait today.
- Match the surrounding style: `function`-style naming `Subject_Condition_Expectation`, an XML
  `<summary>` on any test whose *reason for existing* is not obvious from the name, and a comment
  wherever a fixture value is load-bearing (the suites already do this well — imitate them).
