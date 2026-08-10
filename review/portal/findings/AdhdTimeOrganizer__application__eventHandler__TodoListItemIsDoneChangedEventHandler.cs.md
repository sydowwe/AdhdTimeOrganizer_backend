# Review: AdhdTimeOrganizer/application/eventHandler/TodoListItemIsDoneChangedEventHandler.cs
Role: handler
Summary: Propagates a TodoListItem's IsDone flag to today's linked PlannerTasks by directly overwriting Status, but skips the side effects (reminder sync, ActualStart/EndTime reset, Cancelled-status protection) that the equivalent direct-update path (`PatchPlannerTaskStatusEndpoint`) performs, so the two paths drift.
Coverage: n/a

## Issues
- [High][Quality] TodoListItemIsDoneChangedEventHandler.cs:27-28 — setting `task.Status` here never calls `IReminderRegistrationService.SyncForPlannerTasksAsync`, unlike `PatchPlannerTaskStatusEndpoint` which explicitly retires/re-registers the task's reminder on every status change.
  Why: a task flipped to Completed via its parent TodoListItem keeps its reminder scheduled, so the user gets a nudge for work already marked done (and conversely a task pushed back to NotStarted loses no stale reminder either way) — silent notification drift with no exception or log.
  Fix: inject `IReminderRegistrationService` into this handler and call `SyncForPlannerTasksAsync(tasks.Select(t => t.Id), ct)` after `SaveChangesAsync`, mirroring the endpoint.
  Confidence: Med

- [Medium][Quality] TodoListItemIsDoneChangedEventHandler.cs:27-28 — reverting a task to `NotStarted` here does not clear `ActualStartTime`/`ActualEndTime`, whereas `PatchPlannerTaskStatusEndpoint` clears both for `Cancelled`/`NotStarted`.
  Why: a task shown as "not started" can retain stale actual-start/end timestamps from before the parent item was unchecked, corrupting any duration/streak reporting that reads those fields.
  Fix: clear `ActualStartTime`/`ActualEndTime` when setting `PlannerTaskStatus.NotStarted`, matching the endpoint's switch.
  Confidence: Med

- [Medium][Quality] TodoListItemIsDoneChangedEventHandler.cs:27-28 — the loop unconditionally forces every matching task to `Completed` or `NotStarted`, including one the user explicitly set to `Cancelled` that day.
  Why: toggling the parent TodoListItem (e.g. marking a recurring checklist item done) silently un-cancels/re-completes a task the user deliberately cancelled, overwriting an intentional user action with an unrelated side effect.
  Fix: skip tasks whose current `Status == PlannerTaskStatus.Cancelled` (or otherwise make the overwrite opt-in), rather than blanket-assigning Status to every match.
  Confidence: Low

- [Low][Quality] TodoListItemIsDoneChangedEventHandler.cs:27-28 — status-mutation logic (which statuses to set, what fields to reset) is duplicated between this handler and `PatchPlannerTaskStatusEndpoint` instead of a shared helper.
  Why: the two copies have already drifted (see above); future changes to one won't naturally propagate to the other.
  Fix: extract a shared `ApplyPlannerTaskStatus(task, status)` helper (or a domain method on `PlannerTask`) used by both call sites.
  Confidence: Low

- [Low][Concurrency] TodoListItemIsDoneChangedEventHandler.cs:17 — "today" is computed from `DateTime.UtcNow`, same pattern as `PatchPlannerTaskStatusEndpoint`; around UTC midnight this can select/exclude the wrong day's tasks relative to the user's local calendar day.
  Why: a task the user considers "today" near midnight can be missed or a stale prior-day task matched instead, so the parent-item toggle silently fails to propagate.
  Fix: derive `today` from the user's local timezone/offset if one is tracked elsewhere in the app, consistent across both call sites.
  Confidence: Low

- [Low][Quality] TodoListItemIsDoneChangedEventHandler.cs:12-31 — no try/catch around the query/save; `PlannerTask` carries a `row_version` concurrency token, so a concurrent update to the same task (e.g. a simultaneous `PatchPlannerTaskStatusEndpoint` call) throws `DbUpdateConcurrencyException` here, which propagates unhandled out of the FastEndpoints event publish (`Mode.WaitForAll`) into the toggling endpoint's request pipeline.
  Why: a benign race turns into an unhandled 500 on an unrelated endpoint (`ToggleIsDoneTodoListItemEndpoint`) rather than a graceful retry/skip.
  Fix: wrap in try/catch and log+skip the conflicting task, or catch `DbUpdateConcurrencyException` specifically and reload/retry.
  Confidence: Low
