# Review: AdhdTimeOrganizer/application/eventHandler/ActivityCreatedIsOnToDoListEventHandler.cs
Role: handler
Summary: Correctly-shaped fire-and-forget event handler, but `ActivityCreatedIsOnTodoListEvent` is never published anywhere in the repo, so this handler — and the "activity marked as on the todo list creates a TodoListItem" feature it implements — is dead/unreachable.
Coverage: n/a

## Issues
- [High][Quality] AdhdTimeOrganizer/application/eventHandler/ActivityCreatedIsOnToDoListEventHandler.cs:9-20 — `ActivityCreatedIsOnTodoListEvent` has no `PublishAsync` call anywhere in the codebase (confirmed via repo-wide search for both the event type and all `PublishAsync` call sites), so this handler never runs.
  Why: the intended behavior (auto-creating a `TodoListItem` when an activity is created as "is on todo list") silently does not happen; anyone relying on `docs/domain-map.md` listing this as a wired event will be misled.
  Fix: either wire the publish call at the activity-creation site that should raise this event, or delete the event/handler pair and the domain-map entry if the feature was abandoned.
  Confidence: High

- [Low][Quality] AdhdTimeOrganizer/application/eventHandler/ActivityCreatedIsOnToDoListEventHandler.cs:17-19 — on `AddEntityAsync` failure, the handler only logs and returns; there is no retry, dead-letter, or surfacing back to the request that triggered the event.
  Why: since this runs in its own DbContext scope/transaction separate from whatever saved the triggering entity, a save failure here silently drops the todo-list-item creation with only a log line as evidence — easy to miss in production.
  Fix: at minimum log at a severity that pages/alerts, or consider whether this write should instead happen synchronously in the same transaction as the activity creation if it must not be lost.
  Confidence: Low

- [Nit][Convention] AdhdTimeOrganizer/application/eventHandler/ActivityCreatedIsOnToDoListEventHandler.cs:1 — file name (`ActivityCreatedIsOnToDoListEventHandler.cs`, capital D) doesn't match the class name (`ActivityCreatedIsOnTodoListEventHandler`, lowercase d) or the event file (`ActivityCreatedIsOnToDoListEvent.cs` — also capital D — vs. `ActivityCreatedIsOnTodoListEvent` record).
  Why: minor discoverability friction; grepping for the exact class name won't find the file by name pattern.
  Fix: rename the file to match the class/record casing consistently.
  Confidence: Low
