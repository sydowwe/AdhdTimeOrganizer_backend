# TEST-3 / TEST-5 — `POST` / `PUT /routine-todo-list` (pins `SEC-1` and `SEC-2`)

Read `_SHARED-CONTEXT.md` first.

Two unrelated bugs live on these two endpoints and neither has a test of any kind today. Write both
groups; they share a fixture.

## Endpoints

| | |
|---|---|
| Create | `POST /api/routine-todo-list` → `application/endpoint/routineTodoList/command/CreateRoutineTodoListEndpoint.cs` (a `BaseCreateEndpoint<RoutineTodoList, CreateRoutineTodoListRequest>`) |
| Update | `PUT /api/routine-todo-list/{id}` → `.../UpdateRoutineTodoListEndpoint.cs` (a `BaseUpdateEndpoint<…, UpdateRoutineTodoListRequest>`) |

**Request shape** (`application/dto/request/CreateRoutineTodoListRequest.cs`, extends
`BaseCreateTodoListRequest`): `activityId`, `timePeriodId`, `totalCount?`, `note?`, `suggestedTime?`
(`{hours, minutes}`), `suggestedDays: DayOfWeek[]`, `suggestedDayOfMonth?`, `steps?: [{name, order, note?}]`.
The update request adds `id`, `isDone`, `displayOrder`, `doneCount?`, and step `id?`s.

**Response**: `RoutineTodoListResponse` — but note `CQ-5`, its nested period is malformed; do not
build assertions on the nested `routineTimePeriod` here (that is `RoutineReadEndpointShape.md`).

## Group 1 — `SEC-1`: ownership of `timePeriodId` (🔴)

`timePeriodId` comes from the request body and nothing checks that the caller owns it. The validators
(`application/validator/{Create,Update}RoutineTodoListValidator.cs:34-63` / `:44-73`) load the period
only to read `LengthInDays`, and when the lookup returns null they **`return` without adding a
failure**. So a foreign or nonexistent id is accepted either way.

The damage is downstream: `RoutineTodoListResetJobHandler` runs unauthenticated with the global filter
inert, so it loads the foreign row into the victim's `period.RoutineTodoListColl` and counts it in
`completedCount / items.Count`. The attacker can break a stranger's streak invisibly.

Tests:

- `Create_WithAnotherUsersTimePeriodId_IsRejected` — seed a period for `OtherUserId`
  (`EnsureOtherUserAsync` first), `POST` as the default user pointing at it. Expect 400 or 404 (pick
  one and say which in the assertion message). **Then verify no row was written**: query
  `RoutineTodoList` for `TimePeriodId == otherPeriodId` through a fresh context and expect empty. The
  status code alone is not the point.
- `Create_WithANonexistentTimePeriodId_IsRejected` — `timePeriodId: 999999999`. Today the FK would
  reject this at the DB layer, so it may already "pass"; assert the response is a clean 400, not a
  Postgres FK message (see `SEC-4`).
- `Update_MovingAnItemIntoAnotherUsersPeriod_IsRejected` — create a legitimate item, then `PUT` it with
  `timePeriodId` set to the other user's period. Expect rejection **and** verify through a fresh
  context that the stored row's `TimePeriodId` is unchanged.
- `Create_WithOwnTimePeriod_Succeeds` — the control. Assert the row exists with the caller's `UserId`.

**The teeth of this group**: `ForeignTimePeriod_IsNotCountedAgainstTheVictimsStreak`. If you can only
write one test here, write this one. Insert the cross-tenant row directly through `CreateDbContext()`
(bypassing the endpoint, so the test survives the fix), give the victim's period one done item of its
own and threshold 100, run the reset job via `RunJobAsync` (copy from
`RoutineTodoListResetJobHandlerTests.cs:107`), and assert the victim's `Streak == 1` and the written
`RoutinePeriodCompletion.TotalCount == 1`. That pins the *consequence* rather than the input
validation, so it keeps its value whatever shape the fix takes.

## Group 2 — `SEC-2`: `PUT` flips `IsDone` without the cross-slice event (🟠)

`application/dto/request/UpdateRoutineTodoListRequest.cs:16` assigns `e.IsDone = IsDone` directly,
bypassing `BaseTodoListItem.SetDone` (which reconciles `DoneCount` against the steps),
`RoutineResetService.UpdateItemStreak`, and — critically —
`RoutineTodoListIsDoneChangedEvent.PublishAsync`, which is the **only** channel by which Planning's
`PlannerTaskIsDoneChangedEventHandler` learns a routine was completed (`summary.md:28-34`).
Cross-slice handlers log-and-swallow, so the desync is silent and permanent.

Tests:

- `Update_SettingIsDoneTrue_FansOutToTheLinkedPlannerTask` — the strongest form. Model it on
  `Endpoints/CompletionFanOutTests.cs`, which already sets up the routine ↔ planner-task pairing and
  asserts on rows; copy its fixture. Toggle via `PUT` and assert the planner task followed. Compare
  with the same suite's toggle-endpoint case, which passes.
- `Update_SettingIsDoneTrue_ReconcilesDoneCountWithSteps` — an item with `TotalCount = 3` and three
  steps; `PUT` with `isDone: true`; assert `DoneCount == 3` and all steps done, matching what the
  toggle endpoint produces.
- `Update_DoesNotInflateTheItemStreak` — see `ToggleIsDoneStreak.md`; the `PUT` path does not call
  `UpdateItemStreak` at all, so `Streak` should be unchanged. Pin whichever behaviour the fix chooses.

If the fix is to **remove `isDone` from the update request** (recommended in `SEC-2` — it matches how
`RoutineTimePeriodRequest` deliberately withholds `Streak`/`LastResetAt`), then these three tests
become one: `Update_CannotChangeIsDone`, asserting the field is ignored or the request is rejected.
Decide the fix first, then write the version that matches; do not write both.

## Also worth covering while you are here

- `Create_SetsDisplayOrderWithinItsPeriod` — `AfterMapping` calls the routine-shaped
  `GetNextDisplayOrder` grouped by `TimePeriodId` (`infrastructure/persistence/extensions/RoutineTodoListExtensions.cs`).
  Two items in two different periods should not interleave their orders.
- `Update_PreservesStepIdentity` — see `CQ-4`: `UpdateRoutineTodoListEndpoint.cs:20-27` rebuilds every
  step as a new object, so ids churn on every save. Capture the step ids before the `PUT` and assert
  they are the same afterwards. **This fails today**; tag `KnownGap` naming `CQ-4` if the fix is
  deferred.
- The `SuggestedDays` / `SuggestedDayOfMonth` bucket rule (daily → neither; ≤14d → days only; >14d →
  day-of-month only) is duplicated across both validators and is entirely untested. One `[Theory]` over
  the three buckets × both fields covers it.
