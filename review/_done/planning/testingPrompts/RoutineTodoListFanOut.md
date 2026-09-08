# TEST-21 — `RoutineTodoListIsDoneChangedEventHandler`

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**This is the highest-value prompt in the folder.** The handler has zero tests and four open findings
(`CQ-1`…`CQ-4`); its tested sibling has none. Write these tests first, then fix the handler.

## What you are testing

`AdhdTimeOrganizer.Planning/application/eventHandler/RoutineTodoListIsDoneChangedEventHandler.cs` — 45
lines. It reacts to `RoutineTodoListIsDoneChangedEvent` (declared in
`AdhdTimeOrganizer.Core/application/event/`, published by the Routines slice) by flipping **today's**
planner tasks for the same activity to `Completed` / `NotStarted`.

Current source, in full, so you do not have to guess:

```csharp
public async Task HandleAsync(RoutineTodoListIsDoneChangedEvent eventModel, CancellationToken ct)
{
    using var scope = scopeFactory.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

    var today = DateOnly.FromDateTime(DateTime.UtcNow);

    var tasks = await dbContext.Set<PlannerTask>()
        .Include(pt => pt.Calendar)
        .Where(pt => pt.ActivityId == eventModel.ActivityId && pt.UserId == eventModel.UserId && pt.Calendar.Date == today)
        .ToListAsync(ct);

    if (tasks.Count == 0) return;

    foreach (var task in tasks)
        task.Status = eventModel.NewIsDone ? PlannerTaskStatus.Completed : PlannerTaskStatus.NotStarted;

    await dbContext.SaveChangesAsync(ct);
}
```

The **sibling** — `TodoListItemIsDoneChangedEventHandler.cs` in the same folder — is the reference
implementation. It does four things this one does not, and each is a test below.

## Where the tests go

`AdhdTimeOrganizer.IntegrationTests/Endpoints/CompletionFanOutTests.cs`, as a new region. That class
already claims to cover this handler in its class doc (see `DOC-5`) and already has the seeding you
need. If it gets unwieldy, a new `RoutineCompletionFanOutTests` in the same folder is fine.

## How to drive it

Two options; prefer the first where it works.

1. **End to end through Routines.** Toggle the routine to-do list via its HTTP endpoint and let
   `PublishAsync(Mode.WaitForAll, …)` dispatch. Look at how `CompletionFanOutTests.
   TogglingTodoItem_LeavesCancelledPlannerTaskAlone` drives the sibling and mirror it. This is the only
   way to test `CQ-3` (the missing try/catch), because it needs a real publisher awaiting the handler.
2. **Directly against the handler.** Construct it with the test host's `IServiceScopeFactory` and call
   `HandleAsync(new RoutineTodoListIsDoneChangedEvent(...), ct)`. `CompletionFanOutTests.
   PlannerTaskEvent_CannotTouchAnotherUsersTodoItem` does exactly this for the host-side handler and
   explains why (HTTP cannot forge a cross-user event). Use it for the isolation scenario.

## Scenarios

### A — happy path (write green, should pass today)

Seed an activity, today's calendar for the test user, and a `PlannerTask` on it for that activity with
`Status = NotStarted`. Fire the event with `NewIsDone = true`.

Assert: the task is `Completed`. Also seed a **second** task for the same user, same activity, on
**yesterday's** calendar and assert it is untouched — the sibling has this test
(`TogglingTodoItem_OnlyFlipsTodaysPlannerTask`) and this handler needs it too.

### B — `CQ-1`: actual times must be cleared (**expected to fail today**)

Seed today's task with `Status = InProgress`, `ActualStartTime = 09:05`, `ActualEndTime = null`.
Fire the event with `NewIsDone = false`.

Assert: `Status == NotStarted` **and** `ActualStartTime == null`.

Today the handler assigns `Status` directly instead of calling `PlannerTask.ApplyStatus`, so
`ActualStartTime` survives. `docs/testing.md` states the rule: *"A call site that sets `Status` directly
leaves stale actual times that the trend then measures."* The sibling's equivalent green test is
`UntickingTodoItem_ClearsPlannerTaskActualTimes`.

Write this red-and-tagged, or green-against-current-behaviour with a `// FINDING: CQ-1` comment naming
the assertion to flip. Do not leave it silently red.

### C — `CQ-2`: the reminder must be retired (**expected to fail today**)

Seed today's task **with a reminder attached** via `ReminderSeedHelper` (portal row + module
`ReminderDefinition`). Fire the event with `NewIsDone = true`.

Assert: the module-side `ReminderDefinition` for `ReminderRegistrationService.KeyFor(reminderId)` is
cancelled. Copy the assertion from `ApplyTemplatePlannerTaskConflictResolutionTests.
AssertReminderCancelledAsync` or from `ReminderRegistrationTests.FinishingTheTask_CancelsItsReminder`.

Today nothing calls `SyncForPlannerTasksAsync`, so the definition stays active and the user is nudged
about work they finished. The sibling's green counterpart is
`TogglingTodoItem_RetiresLinkedPlannerTaskReminder`.

### D — `CQ-4`: a `Cancelled` task must be left alone (**expected to fail today**)

Seed today's task with `Status = Cancelled` and a `SkipReason`. Fire the event with `NewIsDone = true`.

Assert: `Status` is still `Cancelled` and `SkipReason` is intact.

The sibling does this deliberately (`TogglingTodoItem_LeavesCancelledPlannerTaskAlone`) with the
comment *"the parent to-do toggle must not silently un-cancel or re-complete it."*

### E — `CQ-3`: a handler failure must not 500 the publisher (**expected to fail today**)

Drive it end to end through the Routines toggle endpoint and make the handler's `SaveChangesAsync`
throw — the cleanest lever is a concurrent `PATCH /api/planner-task/{id}/status` that wins the
`row_version` race, which is how `CompletionFanOutTests.
ConcurrentStatusPatches_ShouldNotReturnInternalServerError` sets up the same shape.

Assert: the Routines toggle response is its normal success code (**not** 500), and the routine's own
committed state is intact. The sibling swallows and logs; this handler has no `try`/`catch` at all.

### F — `CQ-5`: which "today"?

Seed a user whose `User.Timezone` is `Europe/Bratislava` (UTC+2 in summer) and a planner task on the
**local** today, at a moment where UTC is still on the previous date.

This one is a **judgement call, not a bug fix you can assume**: `PlannerStreakReader` and
`GetPlanVsActualTrendPlannerTaskEndpoint` both use the user's own zone; all three write paths use UTC;
and `CompletionFanOutTests.TogglingTodoItem_OnlyFlipsTodaysPlannerTask` currently pins the UTC reading
for the sibling. Write the test to document whichever behaviour is decided, and if the decision is
"user's zone", update that sibling test in the same change.

Making the moment deterministic needs a clock seam or a frozen `TimeProvider`; if the test host has
none, seed both candidate dates and assert which one moved rather than trying to control the clock.

### G — isolation

Seed the same activity and today's calendar for **two** users, each with a planner task. Fire the event
naming user A. Assert only A's task moved and B's is byte-identical.

This one should pass today — the handler does filter by `eventModel.UserId` — and is worth pinning
precisely because the sibling handler does **not** have that predicate (`SEC-2`). Add the mirrored
assertion to the sibling once `SEC-2` is fixed.

## Definition of done

Seven tests. A, G green today. B, C, D, E either red-and-tagged or green-with-`// FINDING` comments
naming the assertion to flip. F documents the decision. After the handler is fixed, B–E flip to plain
green and `docs/testing.md` § *The guards* gains a row for this handler (`DOC-5`).
