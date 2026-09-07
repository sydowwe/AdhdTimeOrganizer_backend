# TEST-8 / TEST-9 — Toggle paths and the item streak (pins `CQ-7`)

Read `_SHARED-CONTEXT.md` first.

§1–§3 below are `TEST-8`; §4 is `TEST-9`.

## Endpoints with no coverage

| Endpoint | Route | File |
|---|---|---|
| bulk toggle | `PATCH /api/routine-todo-list/toggle-is-done` | `application/endpoint/routineTodoList/command/RoutineToggleIsDoneTodoListEndpoint.cs` |
| step toggle | `PATCH /api/routine-todo-list/{itemId:long}/steps/{stepId:guid}/toggle` | `application/endpoint/routineTodoList/command/ToggleStepIsDoneRoutineTodoListEndpoint.cs` |

Both subclass TodoLists' `BaseToggleIsDoneTodoListEndpoint<T>` (`application/endpoint/`) /
`BaseToggleStepIsDoneEndpoint<T>` — the routes and request shapes are declared **there**, not in the
Routines subclasses. The bulk one takes `ToggleIsDoneRequest { Ids, ForceValue }`.

**A route-level search of the whole test project returns zero hits for either.** This is the densest
endpoint in the module and nothing touches it. In particular `Endpoints/CompletionFanOutTests.cs`
exercises `api/todo-list-item/toggle-is-done` — the **TodoLists sibling**, not this endpoint — so do
not assume the fan-out is covered from here.

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
Nothing keys the increment to the period or to the cycle. **Two** ways in, and the second is worse
than the first:

1. **Un-tick then re-tick** an ordinary item in the same cycle — `Streak` and `BestStreak` climb each
   time. A user can sit and watch a daily routine's streak grow.
2. **A counted item needs no un-tick at all.** `BaseToggleIsDoneTodoListEndpoint.IsDoneLogic` with
   `forceValue: true` clamps `DoneCount` to `TotalCount` and leaves `IsDone` true (`:91-101`), but the
   base still calls `AfterItemToggled` on every request (`:40`). So repeatedly `PATCH`-ing
   `{ ids: [x], forceValue: true }` at an already-complete counted item increments the streak every
   call.

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

### §1 Integration, streak inflation — new file `Routines/RoutineToggleIsDoneTests.cs`

- `Toggle_UntickThenRetick_DoesNotInflateTheItemStreak` — the real pin. Create an item, toggle done,
  toggle undone, toggle done. Assert through a fresh context that `Streak == 1`, not 3. **Fails today.**
- `Toggle_ForceTrueTwiceOnACompleteCountedItem_DoesNotInflateTheItemStreak` — the second route in.
  Seed an item with `TotalCount = 2`, `PATCH` `{ ids, forceValue: true }` three times. After the second
  call the item is complete; the third must not move `Streak`. **Fails today**, and this one needs no
  un-tick, so it is the case a fix keyed only on "was it un-ticked" would miss.
- `Toggle_CompletingAnItem_SetsLastCompletedAtAndStreak` — the happy path, unpinned today.

### §2 Integration, the reset side-effect

- `Toggle_OnAnElapsedPeriod_ResetsFirstThenApplies` — `FetchAndPrepare` (`:22-53`) applies
  `CheckGrace` + the **list** `TryReset` before toggling, so a toggle can be the thing that first
  notices an elapsed period. Seed an elapsed period with a done item, toggle a *different* item, and
  assert a `RoutinePeriodCompletion` row was written, `LastResetAt` advanced, and the first item was
  unticked. This is a documented reset site (`IRoutinePeriodNotificationService.cs:18-22`) and it is
  untested.
- `Toggle_OnAnElapsedPeriod_RaisesThePeriodEndedSummary` — swap in a double for
  `IRoutinePeriodNotificationService` (see `RoutineTodoListResetJobHandlerTests.cs:343-353`) and assert
  `NotifyPeriodEndedAsync` fired from `AfterSave` (`:65-69`), i.e. **after** the commit.

### §3 Integration, the cross-slice event

- `Toggle_PublishesRoutineTodoListIsDoneChangedEvent` — `PublishEvent` (`:71-75`) raises
  `RoutineTodoListIsDoneChangedEvent`, the module's only outbound event and the sole channel into
  Planning. `Endpoints/CompletionFanOutTests.cs` covers the *handler*, driven from
  `api/todo-list-item/toggle-is-done` — a different endpoint. Assert the fan-out from **this** route:
  seed a `PlannerTask` paired to the same activity and check it flips. Model the assertion on
  `CompletionFanOutTests`; do not duplicate its handler-level cases.
- While here, note for the fix: `BaseToggleIsDoneTodoListEndpoint.HandleAsync` (`:25-52`) has **no
  try/catch**, unlike every create/update/delete base. If `ConcurrentResetRace.md` (`SEC-3`) is being
  worked at the same time, this endpoint is the second of the two 500-producing sites.

### §4 (`TEST-9`) Integration, the step toggle

- `StepToggle_CompletingTheLastStep_MarksTheItemDoneAndUpdatesTheStreak` — the step endpoint's
  `OnItemCompleted` hook (`:35-38`). Also assert the repeat-toggle case here, since it shares
  `UpdateItemStreak`.
- `StepToggle_OnAnElapsedPeriod_ResetsTheItemButNotThePeriod` — `BeforeToggle` (`:23-27`) calls the
  **single-item** `TryReset`, which deliberately does **not** advance `LastResetAt`, write a completion
  row, or evaluate the streak. Assert exactly that: the item is fresh, `LastResetAt` unchanged, zero
  completion rows, `Streak` unchanged. `docs/testing.md`'s known-gaps list names this as pinned only by
  unit tests; this is the endpoint-level guard it asks for, and it is what stops a "unify the two
  overloads" refactor silently consuming a reset cycle without scoring it.

## Auth

Add the IDOR case to both: toggling another user's item must 404 and leave the row unchanged. Neither
subclass filters by `UserId` — they rely on the global query filter (`CQ-12`), which is sanctioned by
invariant 3 but asserted nowhere for these two routes (the CRUD auth matrix covers create/update/delete/
get-by-id, not the toggles).
