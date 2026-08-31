# TEST-8 — Toggle paths and the item streak (pins `CQ-7`)

Read `_SHARED-CONTEXT.md` first.

## Endpoints with no coverage

| Endpoint | File |
|---|---|
| `PATCH /api/routine-todo-list/toggle-is-done` (bulk, takes `ids`) | `application/endpoint/routineTodoList/command/RoutineToggleIsDoneTodoListEndpoint.cs` |
| step toggle | `application/endpoint/routineTodoList/command/ToggleStepIsDoneRoutineTodoListEndpoint.cs` |

Both subclass TodoLists' `BaseToggleIsDoneTodoListEndpoint<T>` / `BaseToggleStepIsDoneEndpoint<T>` —
check those bases for the exact route templates and request shapes before writing; the routes are
declared there, not in the Routines subclasses.

## The bug being pinned

`domain/service/RoutineResetService.cs:258-267`:

```csharp
public static void UpdateItemStreak(RoutineTodoList item, DateTime now)
{
    if (!item.IsDone) return;
    item.LastCompletedAt = now;
    item.Streak++;
    if (item.Streak > item.BestStreak) item.BestStreak = item.Streak;
}
```

It increments unconditionally whenever the item is done, and both toggle endpoints call it
(`RoutineToggleIsDoneTodoListEndpoint.cs:57`, `ToggleStepIsDoneRoutineTodoListEndpoint.cs:37`).
Nothing keys the increment to the period, so **toggling an item off and on again inside one cycle
increments `Streak` and `BestStreak` each time**. A user can sit and watch a daily routine's streak
climb.

The *period* streak is unaffected — it advances once per reset in the list `TryReset` (`:209`). Only
the per-item counters inflate.

## What to write

### Pure-domain — add to `Services/RoutineResetServiceTests.cs`

The file already has four `UpdateItemStreak` tests (`:506-554`); none of them calls it twice. Add:

- `UpdateItemStreak_CalledTwiceInTheSameCycle_IncrementsOnlyOnce` — item with `LastResetDate = today`,
  call `UpdateItemStreak(item, now)` twice, assert `Streak == 1`.
- `UpdateItemStreak_AfterANewCycle_IncrementsAgain` — the control: increment, then simulate a reset
  (`item.LastResetDate = tomorrow`, `IsDone = false` then true again), increment, assert `Streak == 2`.
  Without this, the fix could over-correct into "never increment twice, ever".

The guard will need `LastResetDate` / `LastCompletedAt` — both already exist on the entity — so shape
the tests around whichever the fix uses.

### Integration — new file `Routines/RoutineToggleIsDoneTests.cs`

- `Toggle_UntickThenRetick_DoesNotInflateTheItemStreak` — the real pin. Create an item, toggle done,
  toggle undone, toggle done. Assert through a fresh context that `Streak == 1`, not 3. **Fails today.**
- `Toggle_CompletingAnItem_SetsLastCompletedAtAndStreak` — the happy path, unpinned today.
- `Toggle_OnAnElapsedPeriod_ResetsFirstThenApplies` — `FetchAndPrepare` (`:22-53`) applies
  `CheckGrace` + the **list** `TryReset` before toggling, so a toggle can be the thing that first
  notices an elapsed period. Seed an elapsed period with a done item, toggle a *different* item, and
  assert a `RoutinePeriodCompletion` row was written and the first item was unticked. This is a
  documented reset site (`IRoutinePeriodNotificationService.cs:18-22`) and it is untested.
- `Toggle_OnAnElapsedPeriod_RaisesThePeriodEndedSummary` — swap in a double for
  `IRoutinePeriodNotificationService` (see `RoutineTodoListResetJobHandlerTests.cs:343-353`) and assert
  `NotifyPeriodEndedAsync` fired from `AfterSave` (`:65-69`).
- `Toggle_PublishesRoutineTodoListIsDoneChangedEvent` — the cross-slice fan-out.
  `Endpoints/CompletionFanOutTests.cs` already covers this from the toggle side; check before
  duplicating, and if it is covered, add a pointer comment instead of a second test.
- `StepToggle_CompletingTheLastStep_MarksTheItemDoneAndUpdatesTheStreak` — the step endpoint's
  `OnItemCompleted` hook (`:35-38`). Also assert the untick/retick case here, since it shares
  `UpdateItemStreak`.
- `StepToggle_OnAnElapsedPeriod_ResetsTheItemButNotThePeriod` — `BeforeToggle` (`:23-27`) calls the
  **single-item** `TryReset`, which deliberately does not advance `LastResetAt` or write a completion
  row. Assert exactly that: the item is fresh, `LastResetAt` unchanged, no completion row. This pins
  the difference between the two overloads, which `domain-map.md:42-43` calls out and nothing tests at
  the HTTP level.

## Auth

Add the IDOR case to both: toggling another user's item must 404 and leave the row unchanged. Neither
subclass filters by `UserId` — they rely on the global query filter (`CQ-12`), which is sanctioned but
unasserted.
