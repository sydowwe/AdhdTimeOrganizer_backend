# AdhdTimeOrganizer.TodoLists — Agent Summary

**Purpose:** the to-do domain — lists, the items on them, the steps inside an item, the categories
that group lists, and the per-user `TaskPriority` lookup that ranks items. It also owns the
**shared to-do primitives that the Routines slice builds on**: `BaseTodoListItem`, `TodoListStep`,
`BaseTodoListConfigure`, `TodoListExtensions`, `TodoListSettings`, and the toggle / step /
reorder endpoint bases.

**This is the second slice of the portal split**, and the first with no outbound slice edges — it
depends on `AdhdTimeOrganizer.Core` and nothing else. Read
`review/portal/slicePrompts/00-README.md` for the plan and the remaining order (Routines → History
→ Planning → Reminders → Tracking).

## Bounded context

Owns: `TodoList`, `TodoListItem`, `TodoListCategory`, `TodoListStep`, `TaskPriority` and the
abstract `BaseTodoListItem`, their EF configurations, ~39 endpoints, their DTOs and validators, and
the `TaskPrioritySeeder` / `TodoListSeeder` pair.

Does **not** own — and must never reference: `AppDbContext`, `Program.cs`, the migrations, the DI
wiring, or any other slice. In particular it does not own `RoutineTodoList`, `RoutineTimePeriod` or
`RoutinePeriodCompletion`; those are still host-side and become `AdhdTimeOrganizer.Routines`.

## Dependency seams

- **References:** `AdhdTimeOrganizer.Core`, `Sydowwe.Framework`, `Sydowwe.Framework.Contracts`.
  No host reference, by construction — see the comment in the csproj.
- **Referenced by:** `AdhdTimeOrganizer` (the host), and `AdhdTimeOrganizer.Routines` next.
- **Zero outbound slice edges**, which is why this one went first: if the extraction pattern were
  wrong, it would have shown up here with nothing else to blame.

## Gotchas — things that will bite you

- **Everything here takes a plain `DbContext`, never `AppDbContext`.** That is what lets the project
  exist separately; `ModuleServiceExtensions.AddModuleServices` aliases `DbContext` → `AppDbContext`,
  so the real app context (global query filters and all) is what arrives at runtime. At the call
  site that means no `dbContext.TodoListItems` — use `dbContext.Set<TodoListItem>()`. The
  `DbContextHelper` extensions all extend `DbContext` and work unchanged.

- **`Microsoft.EntityFrameworkCore` is a global using** (declared in the csproj), because nearly
  every file here names `DbContext`. Don't be surprised by files with no EF using line.

- **The shared bases stay here — do not "promote" them to Core.** `BaseTodoListItem`,
  `TodoListStep`, `BaseTodoListConfigure`, `TodoListExtensions` and the toggle/step/reorder endpoint
  bases are to-do concepts, not core ones, and Core is meant to shrink. Routines references *this*
  project for them.

- **The reverse of that rule is what split `GetNextDisplayOrder`.** The generic
  `GetNextDisplayOrder<TEntity>` is here; the `DbSet<RoutineTodoList>` overload that grouped by
  `TimePeriodId` moved out to the host's `RoutineTodoListExtensions`, because a TodoLists file naming
  a Routines entity would have inverted the one-way edge. It travels to Routines when that slice is
  extracted.

- **`TodoListItem` has no `PlannerTask` navigation, deliberately.** The relationship is owned
  entirely from the Planning side (`PlannerTask.TodolistItemId`). Wanting a navigation back into
  Planning means you are about to create the cycle this whole split exists to avoid; the completion
  fan-out goes through the Core event records instead (`TodoListItemIsDoneChangedEvent`).

- **`TodoListItem.CompletedTimestamp` is stamped by an interceptor — never set it by hand.**
  `TodoListItemCompletionInterceptor` (`infrastructure/persistence/interceptor/`) watches the
  ChangeTracker and writes it on every `IsDone` transition, in the same `UPDATE`. It is registered
  host-side in `Program.cs`'s `AddInterceptors` call, alongside `SuggestionPatternRefreshInterceptor`.
  Two things about it are load-bearing: it fires only on an *actual* transition (re-stamping on every
  save that touched a done item would drag finished tasks into whichever day they were last edited),
  and `ExecuteUpdateAsync` bypasses it entirely — keep `IsDone` writes on tracked entities. Dropping
  the registration is silent: items still complete, they just vanish from the daily recap.

- **The daily recap crosses into History, and does it through Core.**
  `GetDailyRecapTodoListItemEndpoint` needs time logged per item; that lives in `ActivityHistory`.
  It reads it via Core's `ITodoListItemLoggedTimeSource`, implemented in History — this slice keeps
  its zero outbound edges. The attribution is exact, keyed on `ActivityHistory.TodoListItemId`
  (stamped when the user accepts the save-to-history prompt on completing an item), and must never be
  widened to "any time logged against this item's activity": two items can share one activity, so
  that fallback credits the same seconds twice. See `AdhdTimeOrganizer.Core/application/seam/README.md`.

- **`DoneCount` is step-counted.** `BaseTodoListItem.SetDone` only rewrites `DoneCount` when
  `TotalCount.HasValue`. That guard is correct — a `DoneCount` without a `TotalCount` is not a state
  the app can produce. Don't "fix" it.

- **`TaskPriority` was filed under the Planning folder.** Its configuration lived in
  `configuration/activityPlanning/` while the entity belongs here. The folder structure lies; assign
  files to projects by what the type *is*, never by directory.

- **Registering the slice with the host is four places, none of which break the build.**
  FastEndpoints `o.Assemblies` in `Program.cs` (missing → every to-do route silently 404s);
  `ModuleServiceExtensions.ModuleAssemblies` (being in *both* that list and the `AddDependencyInjection`
  `AppDomain` sweep doubles every `IEnumerable<T>`, so each seeder runs twice, silently);
  `AppDbContext.ApplyHostConfigurations` (missing → the tables vanish from the model); and the
  solution file. `TodoListsRouteSmokeTests` pins the first; `CoreRouteSmokeTests` pins the second for
  every slice at once.

- **One FK constraint name is pinned by hand, and it is load-bearing.**
  `PlannerTaskConfiguration` now calls `.HasConstraintName("fk_planner_task_todo_list_items_todolist_item_id")`
  on the `TodolistItem` FK. EF derives that name from whether `TodoListItem`'s `ToTable` has run yet
  when the FK is named, which depends on the order of the `ApplyConfigurationsFromAssembly` calls —
  and that order shifts every time a slice comes out. Without the pin, extracting this slice emitted
  a constraint rename in an otherwise empty migration. Expect to pin more of these as further slices
  land; don't remove this one.

- **Seeder `Order` is banded.** TodoLists owns **100–199**. See
  `AdhdTimeOrganizer.Core/infrastructure/persistence/seeder/SeederOrderBands.md` before adding a
  seeder anywhere in the solution.

## Navigation

`docs/domain-map.md` is the index: what lives where, and which invariants are load-bearing.
