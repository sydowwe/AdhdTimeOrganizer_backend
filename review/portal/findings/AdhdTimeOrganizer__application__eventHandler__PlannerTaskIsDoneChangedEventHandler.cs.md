# Review: AdhdTimeOrganizer/application/eventHandler/PlannerTaskIsDoneChangedEventHandler.cs
Role: handler
Summary: Forces linked RoutineTodoList/TodoListItem to fully-done or fully-reset when a planner task's IsDone changes, but never touches the item's Steps collection, so the child steps drift out of sync with the DoneCount/IsDone it just set.
Coverage: n/a

## Issues
- [High][Quality] PlannerTaskIsDoneChangedEventHandler.cs:30-42,56-67 — `SyncRoutineTodoList`/`SyncTodoListItem` set `IsDone`/`DoneCount` to the fully-complete or fully-reset value but load the entity without `.Include(x => x.Steps)` and never touch `Steps`, unlike `BaseToggleIsDoneTodoListEndpoint.IsDoneLogic`/`ResetSteps`, which always keeps `Steps[].IsDone` aligned with the parent when it forces a value.
  Why: after this handler runs, the DB has an item marked fully done (DoneCount == TotalCount) while its steps are still partially/none done; the next `BaseToggleStepIsDoneEndpoint` call computes `allDone`/`wasFullyComplete` off those stale steps, so DoneCount math (increment/decrement) desyncs from TotalCount and IsDone can flip inconsistently with the visible step checklist.
  Fix: load `.Include(i => i.Steps)` and set every step's `IsDone` to match the new item state, mirroring `ResetSteps` in `BaseToggleIsDoneTodoListEndpoint`.
  Confidence: High

- [Medium][Concurrency] PlannerTaskIsDoneChangedEventHandler.cs:12-20 — `SaveChangesAsync` here is unguarded (no try/catch, no concurrency retry), and it runs from `PatchPlannerTaskStatusEndpoint` via `PublishAsync(Mode.WaitForAll, ct)` *after* the PlannerTask's own `SaveChangesAsync` already committed (line 69 in the endpoint).
  Why: if this handler throws (e.g. `DbUpdateConcurrencyException` from the `row_version` token on a concurrently-toggled TodoListItem/RoutineTodoList), the endpoint's catch block turns it into a 500 even though the PlannerTask status change is already durably committed — the caller sees a failure for a change that actually happened, and the linked list item is left unsynced with no retry.
  Fix: catch and log concurrency/DB exceptions inside the handler (or in the endpoint's post-publish step) instead of letting them surface as a false "the whole operation failed" 500.
  Confidence: Med

- [Low][Security] PlannerTaskIsDoneChangedEventHandler.cs:50-51 — `SyncTodoListItem` looks up by `i.Id == eventModel.TodoListItemId` only, not also `UserId == eventModel.UserId`, whereas the sibling `SyncRoutineTodoList` filters by both `ActivityId` and `UserId`.
  Why: relies entirely on the global `IEntityWithUser` query filter for scoping; per CLAUDE.md that filter degenerates to a no-op (`!IsAuthenticated || …`) when there's no ambient authenticated user, so any non-HTTP-triggered publish of this event (e.g. a future background job) would let `TodoListItemId` cross user boundaries. Currently unreachable since the only publisher (`PatchPlannerTaskStatusEndpoint`) runs inside an authenticated request, so this is defense-in-depth, not a live bypass today.
  Fix: add `&& i.UserId == eventModel.UserId` to the `TodoListItem` lookup for consistency and to remove the reliance on ambient auth state.
  Confidence: Low

No other issues found.
