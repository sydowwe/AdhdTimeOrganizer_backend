# AdhdTimeOrganizer.TodoLists — Agent Summary

**Purpose:** the to-do domain — lists, the items on them, the steps inside an item, the categories
that group lists, and the per-user `TaskPriority` lookup that ranks items. It also owns the
**shared to-do primitives that the Routines slice builds on**: `BaseTodoListItem`, `TodoListStep`,
`BaseTodoListConfigure`, `TodoListExtensions`, `TodoListSettings`, and the toggle / step /
reorder endpoint bases.

**Bounded context.** Owns `TodoList`, `TodoListItem`, `TodoListCategory`, `TodoListStep`,
`TaskPriority` and the abstract `BaseTodoListItem`, their EF configurations, 34 endpoints plus the 6
abstract bases, their DTOs and validators, two Core seam implementations, and the
`TaskPrioritySeeder` / dev `TodoListItemSeeder` pair. Tables live in the **`todo`** schema.

Does **not** own — and must never reference: `AppDbContext`, `Program.cs`, the migrations, the DI
wiring, or any other slice. In particular it does not own `RoutineTodoList`, `RoutineTimePeriod` or
`RoutinePeriodCompletion`, which live in `AdhdTimeOrganizer.Routines` and derive from *this*
project's bases.

## Dependency seams

- **References:** `AdhdTimeOrganizer.Core`, `Sydowwe.Framework`, `Sydowwe.Framework.Contracts`.
  No host reference, by construction — see the comment in the csproj.
- **Referenced by:** the host, and `AdhdTimeOrganizer.Routines` — the solution's **only** outbound
  slice edge, and it exists for the shared to-do bases listed above.
- **Zero outbound slice edges of its own.** Everything it needs from another slice goes through Core.
- **Implements three Core seams** (`application/seam/`), so other slices can reach to-do rows without
  referencing this project:
  - `TodoListActivityMembershipSource` (key `ActivityMembershipSourceKeys.TodoList`) — "this activity
    is on a to-do list", faceted by `TaskPriorityId`. **Consumed by nobody today** — History's grid
    filters were its only consumer and went with the grid (`CQ-1`).
  - `TodoListActivityReferenceSource` (key `ActivityReferenceSourceKeys.TodoList`) — counts and
    repoints **two** activity columns during an activity merge.
  - `TodoListItemOwnedReferenceVerifier` (key `UserOwnedReferenceKeys.TodoListItem`) — "is this item
    id the caller's?", so History can refuse a recording linked to somebody else's task. It filters on
    the user id it is handed rather than the ambient query filter: this answers a security question,
    not a scoped read, and must not change with whoever happens to be executing.
- **Consumes one Core seam:** `ITodoListItemLoggedTimeSource`, implemented in History, for the daily
  recap's per-item logged time.
- **Publishes completion through Core events** (`TodoListItemIsDoneChangedEvent`) rather than calling
  into Planning or History directly.

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
  `GetNextDisplayOrder<TEntity>` and the `DbSet<TodoListItem>` overload are here; the
  `DbSet<RoutineTodoList>` overload that groups by `TimePeriodId` lives in Routines, because a
  TodoLists file naming a Routines entity would invert the one-way edge.

- **`TodoListItem` has no `PlannerTask` navigation, deliberately.** The relationship is owned
  entirely from the Planning side (`PlannerTask.TodolistItemId`). Wanting a navigation back into
  Planning means you are about to create the cycle this whole split exists to avoid; the completion
  fan-out goes through the Core event records instead (`TodoListItemIsDoneChangedEvent`).

- **`TodoListItem.CompletedTimestamp` is stamped by an interceptor — never set it by hand.**
  `TodoListItemCompletionInterceptor` (`infrastructure/persistence/interceptor/`) watches the
  ChangeTracker and writes it on every `IsDone` transition, in the same `UPDATE`. It is registered
  host-side in `Program.cs`'s `AddInterceptors` call, alongside `SuggestionPatternRefreshInterceptor`.
  Three things about it are load-bearing: it fires only on an *actual* transition (re-stamping on
  every save that touched a done item would drag finished tasks into whichever day they were last
  edited); it overrides **both** `SavingChanges` and `SavingChangesAsync`, because seeders still use
  the sync path; and `ExecuteUpdateAsync` bypasses it entirely — keep `IsDone` writes on tracked
  entities. Dropping the registration is silent: items still complete, they just vanish from the
  daily recap.

- **The daily recap crosses into History, and does it through Core.**
  `GetDailyRecapTodoListItemEndpoint` needs time logged per item; that lives in `ActivityHistory`.
  It reads it via Core's `ITodoListItemLoggedTimeSource`, implemented in History — this slice keeps
  its zero outbound edges. The attribution is exact, keyed on `ActivityHistory.TodoListItemId`
  (stamped when the user accepts the save-to-history prompt on completing an item), and must never be
  widened to "any time logged against this item's activity": two items can share one activity, so
  that fallback credits the same seconds twice. An item with no recording is an **ordinary** case
  (the prompt gets declined) and appears at zero, not omitted. See
  `AdhdTimeOrganizer.Core/application/seam/README.md`.

- **The merge repoint has a collapse case, and it lives in this slice.**
  `TodoListActivityReferenceSource` counts `ActivityId` and `PairedLeisureActivityId` as two separate
  references, so an item pointing at the same merged activity twice counts twice. After repointing,
  an item paired with its own activity has the pairing dropped — a self-pairing is not a temptation
  bundle. That drop still counts as a repoint.

- **`DoneCount` is step-counted, and the check constraint has a hole.** `BaseTodoListItem.SetDone`
  only rewrites `DoneCount` when `TotalCount.HasValue` — deliberate, a `DoneCount` without a
  `TotalCount` is not a state the app can produce. But
  `CK_{Entity}_DoneCount_LessOrEqual_TotalCount` is `done_count IS NULL OR total_count IS NULL OR …`,
  so Postgres accepts `DoneCount` set with `TotalCount` NULL. The API surface never produces it
  (the create validator rejects the combination); direct EF writes can. Pinned by
  `TodoListToggleAndDoneCountTests.CheckConstraint_BypassedWhenTotalCountIsNull`.

- **`TaskPriority`'s unique key is `(user_id, priority)`, not `Text`.** Text is free to repeat. This
  matters for `Collides` in its seeder, and it means the rank column cannot be swapped in a single
  PUT — the index is immediate, not deferred, so a naive reorder onto a live rank 409s. Reorder via
  an unused temporary rank; `TaskPriorityValidator` requires `Priority >= 1`, so the temporary has to
  be a large unused rank, not a negative one.

- **`TaskPriority` was filed under the Planning folder.** Its configuration lived in
  `configuration/activityPlanning/` while the entity belongs here. The folder structure lies; assign
  files to projects by what the type *is*, never by directory. The same caution applies inside this
  project — the endpoint namespaces (`…endpoint.todoList.todoListItem.command`) carry an extra
  `todoList` segment the folders don't, and `ToDoListItemConfiguration.cs` holds a class named
  `TodoListItemConfiguration`.

- **Registering the slice with the host is four places, none of which break the build.**
  FastEndpoints `o.Assemblies` in `Program.cs` (missing → every to-do route silently 404s);
  `ModuleServiceExtensions.ModuleAssemblies` (being in *both* that list and the `AddDependencyInjection`
  `AppDomain` sweep doubles every `IEnumerable<T>`, so each seeder runs twice, silently);
  `AppDbContext.ApplyHostConfigurations` (missing → the tables vanish from the model); and the
  solution file. `TodoListsRouteSmokeTests` pins the first; `CoreRouteSmokeTests` pins the second for
  every slice at once.

- **One FK constraint name is pinned by hand, and it is load-bearing.**
  `PlannerTaskConfiguration` calls `.HasConstraintName("fk_planner_task_todo_list_items_todolist_item_id")`
  on the `TodolistItem` FK. EF derives that name from whether `TodoListItem`'s `ToTable` has run yet
  when the FK is named, which depends on the order of the `ApplyConfigurationsFromAssembly` calls —
  and that order shifted every time a slice came out. Without the pin, extracting this slice emitted
  a constraint rename in an otherwise empty migration. Don't remove it.

- **Seeder `Order` is banded.** TodoLists owns **100–199**. See
  `AdhdTimeOrganizer.Core/infrastructure/persistence/seeder/SeederOrderBands.md` before adding a
  seeder anywhere in the solution.

## Extension playbook

- **Add a field to `TodoListItem`:** entity → `ToDoListItemConfiguration` → the request DTOs
  (`CreateTodoListItemRequest` / `UpdateTodoListItemRequest`, mapping lives on the DTO) → the
  response projection → validator → migration **in the host**. If it is user-scoped data, check
  whether `UserDataExportAndDeletionTests` needs it in `Exported`.
- **Add a field to `BaseTodoListItem` instead** only if Routines genuinely needs it too — it changes
  both tables and both migrations. `CompletedTimestamp` is the counter-example: it stayed on
  `TodoListItem` because routine items already answer "when was this done" via
  `RoutinePeriodCompletion`.
- **Add an endpoint:** check `framework/Sydowwe.Framework/application/endpoint/base/` first — most of
  this slice is `BaseCreate/Update/Delete/GetById/GetAll/GetSelectOptions/FilterSort/Patch`
  subclasses with a `Mapping` override. Convention is `<Verb><Entity>Endpoint`. Don't override
  `AllowedRoles()` — nothing here is admin surface. If you override `HandleAsync` on a
  `BasePatchEndpoint`, you lose the base's exception mapping; `MoveTodoListItemEndpoint` shows the
  `DbUtils.HandleException` block you have to reproduce.
- **Add something both to-do and routine items need:** put the abstract base here, typed on
  `BaseTodoListItem`, and subclass it in Routines — that is what the six existing bases are.
- **Add a seeder:** band 100–199, `IPerUserDevSeeder` (may truncate) or `BasePerUserDefaultSeeder<T>`
  (must not). Read `SeederOrderBands.md` first.
- **Expose to-do rows to another slice:** never add a project reference. Declare a seam interface in
  Core, implement it here in `application/seam/` with `IScopedService`, and register the key in
  Core's seam registry — then add a `SeamWiringTests` case, because an unimplemented keyed seam
  lowers a count rather than throwing.

## Deeper reference

- `domain-map.md` — model, invariants, business rules, navigation index
- `testing.md` — test strategy and the guards that must not be deleted
