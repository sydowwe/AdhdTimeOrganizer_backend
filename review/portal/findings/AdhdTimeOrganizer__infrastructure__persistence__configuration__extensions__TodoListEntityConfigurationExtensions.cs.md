# Review: AdhdTimeOrganizer/infrastructure/persistence/configuration/extensions/TodoListEntityConfigurationExtensions.cs
Role: config
Summary: Small, focused builder-extension helper for `BaseTodoListItem` check constraints/props; both call sites correctly call `BaseEntityConfigure()` first, so the convention is respected.
Coverage: n/a

## Issues
- [Low][Quality] TodoListEntityConfigurationExtensions.cs:16 — `CK_{entityName}_DoneCount_LessOrEqual_TotalCount` compares `done_count <= total_count` but both columns are nullable; in Postgres, a CHECK evaluating to NULL (either side null) is treated as satisfied, so a row with `done_count` set and `total_count` NULL bypasses the invariant entirely.
  Why: Silently allows a `done_count` with no `total_count` to exist, which callers reading `IEntityWithDoneAndTotalCount` may not expect.
  Fix: If both fields being simultaneously null-or-set is intended, add an explicit constraint like `(total_count IS NULL) = (done_count IS NULL) OR done_count <= total_count`; otherwise leave as-is if the nullable-independent case is by design.
  Confidence: Low

No other issues found — table/PK/row_version/timestamps are correctly deferred to `BaseEntityConfigure()`, which both `TodoListItemConfiguration` and `RoutineTodoListConfiguration` call before `BaseTodoListConfigure()`; FK indexes are the concrete configurations' responsibility (present there) and out of scope for this shared helper.
