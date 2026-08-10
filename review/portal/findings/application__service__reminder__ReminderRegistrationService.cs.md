# Review: AdhdTimeOrganizer/application/service/reminder/ReminderRegistrationService.cs
Role: handler
Summary: Well-documented adapter onto IReminderRegistry with correct idempotency and DST-aware instant composition, but has no compensation path when a registry call fails after the portal row has already committed, and does sequential per-item registry/DB round trips instead of batching.
Coverage: n/a

## Issues
- [High][Quality] ReminderRegistrationService.cs:120-126 (CancelAsync/CancelManyAsync), consumed by DeleteReminderEndpoint.AfterSave / DeletePlannerTaskEndpoint.AfterSave — callers run `reminders.CancelAsync`/`CancelManyAsync` *after* the portal delete has committed (by design, to avoid publishing against a nonexistent row), but there is no try/catch, retry, or outbox here: if `registry.CancelAsync` throws (transient DB/network failure on the module's own store), the portal row is already gone while the module's `ReminderDefinition` survives and keeps firing.
  Why: produces exactly the orphaned-reminder class of bug the class's own doc comments say this design exists to avoid — a reminder that outlives its portal row, now referencing a deleted `reminder.Id`/`PlannerTaskId` in its payload.
  Fix: wrap the post-commit cancel in a catch that logs loudly (distinct from normal error logging) and/or queue a background reconciliation retry; at minimum log with enough context to find and manually cancel the stray `ReminderDefinition`.
  Confidence: Med

- [Medium][Quality] ReminderRegistrationService.cs:122-126,128-139 — `CancelManyAsync` and `SyncForPlannerTasksAsync` iterate with a plain `foreach` + sequential `await`, each iteration performing its own registry round trip (and, inside `SyncAsync`, its own `PlannerTasks` query, `UserPlannerSettings`-adjacent timezone lookup, and possible `SaveChangesAsync`). A batch delete/update of many planner tasks (see `BatchDeletePlannerTaskEndpoint`) multiplies this into N sequential DB+module round trips.
  Why: N+1-shaped work; for a task with many attached reminders (or a large batch operation) this is a real latency/throughput cost, and a mid-loop failure leaves an undefined subset processed (see previous issue).
  Fix: batch the task/timezone lookups once per call (e.g. a single query keyed by distinct `PlannerTaskId`/`UserId` sets) and, if the registry contract allows it, register/cancel in bulk rather than one call per id.
  Confidence: Med

- [Low][Concurrency] ReminderRegistrationService.cs:205-227 (`ComposeTaskInstantAsync`) — only the spring-forward gap (`IsInvalidTime`) is special-cased; the autumn fall-back ambiguous hour (`IsAmbiguousTime`) is not, so `TimeZoneInfo.ConvertTimeToUtc` silently resolves it using its default rule (treats it as the earlier/standard occurrence) without the caller ever knowing the instant was ambiguous.
  Why: a task scheduled in the repeated hour during a fall-back transition can silently reminder an hour off from what the user intended, with no log trail the way the spring-forward branch gets one.
  Fix: mirror the `IsInvalidTime` branch with an `IsAmbiguousTime` check and log (or pick a documented policy — e.g. always resolve to the first occurrence) the same way the DST-gap case does.
  Confidence: Low

- [Low][Security] ReminderRegistrationService.cs:120-126 — `CancelAsync`/`CancelManyAsync` take a bare `reminderId`/`reminderId` list and call the module registry with no ownership check of their own; the interface doc even says "no-op for an unknown id" but says nothing about foreign ids. Today's two call sites (`DeleteReminderEndpoint`, `DeletePlannerTaskEndpoint`) are safe because they source ids from the current user's already-scoped `Reminder` query (global `IEntityWithUser` filter) or a prior `AuthorizeAsync` check, but the service itself is not the enforcement point.
  Why: a future call site that forwards a client-supplied reminder id straight into `CancelAsync` without re-deriving it from a user-scoped query would let one user cancel another user's reminder registration (module state, not the portal row).
  Fix: note this precondition prominently in the XML doc (it's partially there already) or, better, accept/verify the owning `userId` inside this service as a second line of defense.
  Confidence: Low

- [Nit][Performance] ReminderRegistrationService.cs:54-56 — the `PlannerTasks` lookup inside `SyncAsync` is not `AsNoTracking`, even though it is used only to read `Calendar.Date`/`StartTime`/`Status` and never mutated.
  Why: unnecessary change-tracker overhead on a hot per-reminder read path.
  Fix: add `.AsNoTracking()`.
  Confidence: Low
