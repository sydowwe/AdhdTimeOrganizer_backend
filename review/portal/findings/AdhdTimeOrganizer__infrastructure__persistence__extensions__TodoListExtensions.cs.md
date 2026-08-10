# Review: AdhdTimeOrganizer/infrastructure/persistence/extensions/TodoListExtensions.cs
Role: other (DbSet extension helpers)
Summary: Small, EF-translatable set of `DbSet<T>` helpers for display-order computation; correctness relies entirely on the implicit global user query filter, which is not visible at any of these call sites.
Coverage: n/a

## Issues
- [Low][Security] TodoListExtensions.cs:33-47 — `GetDisplayOrderById` and `GetGroupIdById` take only an `id` and never filter by `userId`, unlike `GetNextDisplayOrder` on the same class which explicitly does `e.UserId == userId`.
  Why: correctness for cross-user isolation currently depends entirely on `BaseTodoListItem` inheriting `IEntityWithUser` (via `BaseEntityWithActivity : BaseEntityWithUser`) and thus picking up `AppDbContext`'s global query filter — nothing in this file enforces or documents that; a caller reached via `IgnoreQueryFilters()`, a future entity that stops inheriting the user chain, or a reviewer skimming this file in isolation would reasonably assume these two lookups are unscoped IDOR vectors (they return `null`/default rather than another user's row today, but that safety is invisible here).
  Fix: add a short comment noting reliance on the global `IEntityWithUser` query filter (matching the pattern already documented in CLAUDE.md for other module reads), or accept a `userId` parameter for defense-in-depth consistent with the sibling method.
  Confidence: Med

- [Nit][Quality] TodoListExtensions.cs:22,27 — the two non-generic `GetNextDisplayOrder` overloads pass a "timePeriodId" parameter that for `TodoListItem` is actually filtered against `TaskPriorityId`, which is a misleading parameter name (`timePeriodId` used for a task-priority grouping).
  Why: readability/maintainability — a future caller/editor could reasonably assume it means the same thing as the `RoutineTodoList` overload's `TimePeriodId` and misuse it.
  Fix: rename the parameter on the `TodoListItem` overload (e.g. `taskPriorityId`) to match its actual semantic use.
  Confidence: Med

No other issues found — the LINQ shapes (`Where`/`Select`/`MinAsync`/`FirstOrDefaultAsync`) are all server-translatable projections with no client-evaluation risk, no `Include` chains (so no N+1 here), and read-only projection queries against a `DbSet` don't carry tracked entities into memory (the `Select` projections return scalars, so `AsNoTracking` is moot for these specific queries).
