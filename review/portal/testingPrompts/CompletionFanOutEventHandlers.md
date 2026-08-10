# TEST-16 — Integration tests for the completion fan-out event handlers

## Context you need (do not rediscover)

**Repo:** `C:/Users/jakub/RiderProjects/AdhdTimeOrganizer`

**Under test** — `AdhdTimeOrganizer/application/eventHandler/`:
- `PlannerTaskIsDoneChangedEventHandler.cs`
- `TodoListItemIsDoneChangedEventHandler.cs`
- `RoutineTodoListIsDoneChangedEventHandler.cs`
- `ActivityAddedToHistoryEventHandler.cs` — **currently dead** (see D)
- `ActivityCreatedIsOnToDoListEventHandler.cs` — **currently dead** (see D)

Events live in `application/event/`. These are FastEndpoints in-process events published with
`PublishAsync(Mode.WaitForAll, ct)`; handlers open their **own DI scope and DbContext**, so they do
*not* share the publisher's transaction.

**Publishers** to drive the tests through (prefer real HTTP over publishing events by hand — the
wiring is part of what's under test):
- `application/endpoint/todoList/BaseToggleIsDoneTodoListEndpoint.cs` — raises the IsDone events for
  both to-do flavours.
- `application/endpoint/activityPlanning/plannerTask/command/PatchPlannerTaskStatusEndpoint.cs` —
  the planner-side status change.
- `application/endpoint/todoList/steps/` — the step-toggle endpoints.

**Test project:** `AdhdTimeOrganizer.IntegrationTests`, new file
`Endpoints/CompletionFanOutTests.cs`. Read `Infrastructure/AppDbContextFixture.cs` and
`Infrastructure/AuthTestBase.cs` first. xunit v3, FluentAssertions, `[Collection("Postgres")]`.
HTTP clients: `CreateUserRoleClient()`; for a second user, `CreateFactory(roles, userId)` (caller
disposes). `CreateDbContext()` to seed and to assert. Override `SeedAsync(db)`.

## Documented behavior (`docs/domain-map.md` → "Completion fan-out")

- Planner task done/undone → the matching `RoutineTodoList` (same activity + user) **and** the linked
  `TodoListItem` are synced, and `DoneCount` is snapped to `TotalCount` / 0 for step-counted items.
- To-do item or routine item done/undone → **today's** planner tasks for that activity/item flip
  between `Completed` and `NotStarted`.

## Scenarios to write

### A. `CQ-6` — planner fan-out must sync steps, not just counts (should FAIL today)

`SyncRoutineTodoList` / `SyncTodoListItem` set `IsDone` and snap `DoneCount`, but load the entity
**without** `.Include(x => x.Steps)` and never touch the steps — unlike
`BaseToggleIsDoneTodoListEndpoint.ResetSteps`, which always keeps them aligned.

1. Seed an activity with a linked `TodoListItem` that has `TotalCount = 3` and 3 steps, all unticked.
2. `PATCH` the linked planner task's status to `Completed` via `PatchPlannerTaskStatusEndpoint`.
3. Assert from a fresh context: item `IsDone == true`, `DoneCount == TotalCount`, **and all 3 steps
   `IsDone == true`**.
4. Then the follow-on corruption this causes — toggle one step off via the step endpoint and assert
   `DoneCount == 2` and `IsDone == false`. Today the handler leaves stale steps, so this arithmetic
   desyncs. This second assertion is the one that shows real user-visible damage.

### B. `CQ-7` — to-do fan-out must not clobber deliberate state (three tests, all FAIL today)

1. **`Cancelled` must survive.** Seed a to-do item and today's planner task for the same activity,
   set the task to `Cancelled`. Toggle the to-do item done. Assert the task is **still `Cancelled`**
   — today the handler unconditionally forces `Completed`.
2. **Reminders must be re-synced.** Seed a to-do item whose linked planner task has a registered
   reminder. Toggle the item done. Assert the reminder is retired/cancelled — the equivalent
   endpoint (`PatchPlannerTaskStatusEndpoint`) calls `SyncForPlannerTasksAsync` on every status
   change; this handler does not. Use the `Reminders/ReminderSeedHelper.cs` and whatever registry
   double `Reminders/ReminderRegistrationTests.cs` already uses.
3. **Actual times must be cleared.** Set a task `Completed` with `ActualStartTime`/`ActualEndTime`
   populated, then untick the parent to-do item. Assert the task is `NotStarted` **and both actual
   timestamps are null**.

### C. Round-trip / idempotency

- Toggle done → undone → done and assert the counterpart lands in a consistent state each time
  (`DoneCount` at `TotalCount` or 0, never in between, never above `TotalCount`).
- Publish the same event twice; assert no double-increment.
- Only **today's** planner tasks should flip. Seed a task yesterday and one today for the same
  activity, toggle the item, assert only today's changed. Note `today` is derived from
  `DateTime.UtcNow` in both the handler and the endpoint — if you can, add a case near a UTC-midnight
  boundary for a user in a non-UTC timezone and record what you find as a finding rather than
  bending the assertion.

### D. `CQ-8` — two events are never published (decide, then pin)

A repo-wide search (portal **and** the `framework/` submodule) for `ActivityAddedToHistoryEvent` and
`ActivityCreatedIsOnTodoListEvent` finds only the event records and their handlers — **no
`PublishAsync` call site anywhere**. Both handlers are DI-registered so they look live, and
`docs/domain-map.md:280-283` lists both as wired.

User-visible effect: creating an activity flagged "is on to-do list" does **not** create the
`TodoListItem`.

Write the test that *should* pass — `POST` an activity with the "is on todo list" flag set, assert a
`TodoListItem` is created — and expect it to fail. Do **not** fix it by publishing the event from
the test. This is a product decision: either the publish call is wired at the activity-creation
endpoint, or the event/handler pair and the domain-map entry are deleted. Tag
`[Trait("Status","KnownGap")]` referencing `CQ-8` and leave it red until that decision is made.

### E. `SEC-8` — cross-user isolation

`PlannerTaskIsDoneChangedEventHandler` looks up `TodoListItem` by `i.Id == eventModel.TodoListItemId`
**only** (no `UserId` predicate), relying entirely on the global query filter — which is a no-op
without an ambient authenticated user.

Seed two users each with a to-do item; drive user A's planner task through the handler with user B's
`TodoListItemId` if the event shape allows constructing that. Assert user B's item is untouched.
If the event cannot be forged through the HTTP path, publish it directly against the handler to
document the gap.

### F. Concurrency

`PlannerTask` and `TodoListItem` both carry a `row_version` token, and no handler catches
`DbUpdateConcurrencyException` — a benign race becomes an unhandled 500 on an unrelated endpoint.
Simulate concurrent toggles of the same task from two clients and assert the API returns something
sane (409 or a successful last-write), not a 500.

## Conventions

- Drive through HTTP wherever possible; publish events directly only where noted (D, E).
- Always assert from a **fresh** `CreateDbContext()` — handlers run in their own scope, so a tracked
  graph from the seeding context will not reflect what they wrote.
- Scenarios A, B and D are expected to fail against current `main`. Tag them
  `[Trait("Status","KnownGap")]` with the finding id (`CQ-6`, `CQ-7`, `CQ-8`) and remove the trait
  when each fix ships.
- No user emails/names in assertions or logs.
