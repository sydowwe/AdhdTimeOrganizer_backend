# Review: AdhdTimeOrganizer/domain/model/entity/todoList/BaseTodoListItem.cs
Role: entity
Summary: Thin anemic base entity; the DoneCount/TotalCount invariant is enforced only by DB check constraints and duplicated in scattered event handlers/services, not by the entity itself.
Coverage: n/a

## Issues
- [Medium][Quality] AdhdTimeOrganizer/domain/model/entity/todoList/BaseTodoListItem.cs:9-10 — `DoneCount`/`TotalCount` are plain mutable auto-properties with no invariant enforcement (e.g. `DoneCount <= TotalCount`, `DoneCount >= 0`) in the entity; the only enforcement is a Postgres check constraint added in `TodoListEntityConfigurationExtensions.BaseTodoListConfigure`, and the "snap to 0/TotalCount" sync logic is reimplemented separately in `PlannerTaskIsDoneChangedEventHandler` and `RoutineResetService`.
  Why: Any future code path that sets `DoneCount`/`TotalCount` directly (without going through the event handler) can violate the invariant in memory until `SaveChanges` hits the DB constraint, and the sync rule (snap DoneCount to 0/TotalCount when IsDone toggles) is duplicated in at least two call sites — a classic drift risk since it's not centralized on the entity.
  Fix: Consider a method like `SetDone(bool)` on `BaseTodoListItem` that updates `IsDone` and snaps `DoneCount` together, so the invariant lives in one place instead of being re-derived per caller.
  Confidence: Med

- [Low][Quality] AdhdTimeOrganizer/domain/model/entity/todoList/BaseTodoListItem.cs:14 — `Steps` is exposed as a publicly settable `ICollection<TodoListStep>`, allowing any caller (mapper, handler) to wholesale-replace or mutate the owned-JSON collection without going through domain logic (e.g. re-syncing `DoneCount`/`IsDone` when steps change, per the domain-map note that "DoneCount is snapped ... for step-counted items").
  Why: Direct collection mutation bypasses whatever consistency rule ties step completion to `DoneCount`, making it easy for a future caller to desync the two.
  Fix: If feasible, expose `Steps` as read-only (`IReadOnlyCollection<T>` backed by a private list) and add explicit `AddStep`/`RemoveStep`/`SetStepDone` methods; low priority since current codebase convention favors DTO-driven mapping over rich entities.
  Confidence: Low

No other issues found.
