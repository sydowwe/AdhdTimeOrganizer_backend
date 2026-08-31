# Shared context for every TodoLists test prompt

Every prompt in this folder assumes this file. Read it once; the individual prompts do not repeat it.

## Where tests go

There is **no** `AdhdTimeOrganizer.TodoLists.Tests` project. All tests for this slice live in
`AdhdTimeOrganizer.IntegrationTests/Endpoints/`, namespace
`AdhdTimeOrganizer.IntegrationTests.Endpoints`, and run against a real Postgres through a real HTTP
client.

## The standard file skeleton

```csharp
using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// One paragraph saying WHY these tests exist — specifically, what fails silently if they are absent.
/// This suite's convention is that the class comment names the silent-failure mode, and each
/// [Fact(DisplayName = "...")] states the behaviour actually observed, not the behaviour assumed.
/// </summary>
[Collection("Postgres")]
public class SomethingTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "...")]
    public async Task Scenario()
    {
        long id;
        await using (var db = CreateDbContext())
            id = await /* seed */;

        var response = await CreateClient().PostAsJsonAsync("api/...", payload, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();   // ALWAYS a fresh context for assertions
        // assert on rows
    }
}
```

## What `PostgresTestBase` gives you

| Member | Use |
|---|---|
| `CreateClient()` | Authenticated as the default test user (`FakeLoggedUserService.TestUserId`) |
| `CreateUserRoleClient()` | Same, explicitly with only the `User` role |
| `CreateDbContext()` | A fresh `DbContext` for seeding and verification. `await using` it. |
| `JsonOpts` | The `JsonSerializerOptions` matching the app's FastEndpoints config — always pass it |
| `CancellationToken` | The test's token. (`TestContext.Current.CancellationToken` is equivalent and also appears in this suite; prefer the base's property in new files.) |

**Rule that is followed everywhere and must stay followed: verify on a fresh context.** Never assert
against the same `DbContext` you seeded with — the change tracker will make a broken write look
correct.

## Fixtures — reuse, do not re-create

`AdhdTimeOrganizer.IntegrationTests/Endpoints/TodoListTestSeedHelper.cs`:

```csharp
TodoListTestSeedHelper.TestUserId                                            // = FakeLoggedUserService.TestUserId
await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, userId, name, ct)
await TodoListTestSeedHelper.SeedTodoListAsync(db, userId, name, categoryId, ct)
await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, priority, userId, text, ct)
await TodoListTestSeedHelper.SeedTodoListItemAsync(
    db, activityId, taskPriorityId, todoListId, userId,
    totalCount, doneCount, isDone, steps, ct)                                // returns the new id
```

Activities come from `PlanningTestSeedHelper.SeedActivityAsync(db, name, userId, ct)` — every
`TodoListItem` needs one, and this is deliberately not duplicated into the TodoLists helper.

Cross-user (IDOR) scenarios use `AdhdTimeOrganizer.IntegrationTests.Reminders.ReminderSeedHelper`:

```csharp
await ReminderSeedHelper.EnsureOtherUserAsync(db, ct);
long otherUserId = ReminderSeedHelper.OtherUserId;
```

## Two seeding traps in this slice

1. **`todo_list_item` is unique on `(UserId, ActivityId, TodoListId)`.** Two seeded items sharing an
   activity *and* a list collide at `SaveChangesAsync` with a Postgres error that looks unrelated. Seed
   a fresh activity per item unless the collision is the thing under test.
2. **`task_priority` is unique on `(UserId, Priority)`,** and the whole collection shares one database
   and one test user. Existing files handle this inconsistently (`TaskPriorityReorderTests` hard-codes
   1 and 2; `TodoListDeleteBehaviorTests` uses 700; `TodoListStepsCrudTests` uses
   `Random.Shared.Next(1000, 999_000)`). **Do not copy the `Random.Shared` approach** — pick a fixed
   rank in a band no other file uses, and put the band in a comment.

## `TodoListItemCompletionInterceptor` affects your seeding

Registered host-side in `Program.cs` and active in tests. Any item you seed with `IsDone = true`, or
any tracked flip of `IsDone`, gets `CompletedTimestamp` stamped with "now" automatically. If a test
needs a completion on a specific day, set the column explicitly *after* the save, the way
`TodoListItemDailyRecapTests` does — do not fight the interceptor.

## Relevant config

`appsettings.json` binds `TodoListSettings` as `MaxItems: 100`, `DisplayOrderGap: 1000000`, so
`DisplayOrderStart` (derived, `MaxItems * DisplayOrderGap`) is `100_000_000`. Several prompts depend on
these exact numbers; read them from `IOptions<TodoListSettings>` rather than hard-coding where you can.

## Pinning a known-bad behaviour

Several prompts below are expected to **fail** on today's code — they document real defects from
`review/todo-lists/02-findings.md`. This repo does not use expected-failure traits as a matter of
course: the established convention (see `CompletionFanOutTests:34`, `DefaultUsersSeederTests:33`,
`SuggestionPatternRefreshTests:35`) is to write a **green regression pin** against the behaviour that is
*correct*, and only fall back to `[Trait("Status", "KnownGap")]` when the fix is out of scope for the
change at hand.

So: **fix the defect and write the test green** where the prompt says the behaviour is wrong. If you are
only writing tests, write the test asserting the *correct* behaviour, add
`[Trait("Status", "KnownGap")]`, and put the finding ID in the `DisplayName`:

```csharp
[Fact(DisplayName = "CQ-2: an item created into a group whose lowest DisplayOrder is 0 must not reset to DisplayOrderStart")]
[Trait("Status", "KnownGap")]
```

There is **no** `[Trait("Status", "KnownGap")]` anywhere in the test project today, so the first prompt
to use one is establishing the convention rather than following it. That is intended — but it means the
filter you would use to list known gaps (`dotnet test --filter Status!=KnownGap`) is not wired into any
CI step yet. Check before you rely on it.
