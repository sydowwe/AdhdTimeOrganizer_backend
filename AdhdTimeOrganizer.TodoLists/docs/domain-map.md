# AdhdTimeOrganizer.TodoLists — Domain Map

Navigation index. Open only what you need; `summary.md` is the orientation.

## Model

```mermaid
erDiagram
    User ||--o{ TodoList : owns
    User ||--o{ TodoListItem : owns
    User ||--o{ TodoListCategory : owns
    User ||--o{ TaskPriority : owns

    TodoListCategory ||--o{ TodoList : "groups (SetNull)"
    TodoList ||--o{ TodoListItem : "contains (Cascade)"
    TaskPriority ||--o{ TodoListItem : "ranks (Restrict)"
    Activity ||--o{ TodoListItem : "is what (Cascade)"
    Activity ||--o{ TodoListItem : "paired leisure (SetNull)"
    TodoListItem ||--o{ TodoListStep : "checklist (owned, ToJson)"
```

`User` and `Activity` are Core's. Neither carries an inverse collection back into this slice — that
is what keeps Core free of a reference to it.

Not shown, because they are **not in this project**: `RoutineTodoList` and `RoutinePeriodCompletion`
also derive from `BaseTodoListItem` and hang off `RoutineTimePeriod`, but they belong to
`AdhdTimeOrganizer.Routines`, which references this project for the bases. `PlannerTask` points at
`TodoListItem` from the Planning side only.

## Entities — `domain/model/entity/`

Files are flat in that folder; the namespace is `AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList`.

| Type | Notes |
|---|---|
| `BaseTodoListItem` | The shared base (`BaseEntityWithIsDone`, `IEntityWithDoneAndTotalCount`): `DisplayOrder`, `DoneCount` / `TotalCount`, `Note`, `SuggestedTime`, the `TodoListStep` collection. **Routines derives from this**, which is why it lives here and not in Core. `SetDone` snaps `IsDone`, `DoneCount` and every step together, and only rewrites `DoneCount` when `TotalCount.HasValue` — deliberate. |
| `TodoList` | Grouped by an optional `TodoListCategory`. Carries computed `ItemCount` / `CompletedCount` off the loaded collection — not mapped, so they are zero on a projection that didn't include the items. |
| `TodoListItem` | `BaseTodoListItem` + `TaskPriorityId` (required), `TodoListId` (optional), `ActivityId`, `DueDate` / `DueTime`, `PairedLeisureActivityId`, `CompletedTimestamp`. **No `PlannerTask` navigation** — the relationship is owned from Planning. **No navigation for the paired activity either** — reads carry the bare id. `CompletedTimestamp` is written **only** by `TodoListItemCompletionInterceptor`, never by hand. |
| `TodoListCategory` | Per-user lookup, unique on `(UserId, Name)`. |
| `TodoListStep` | The checklist sub-items. **Not an entity** — an owned type serialised `ToJson` into the parent's row, keyed by a client-supplied `Guid` (`ValueGeneratedNever`). There is no steps table. |
| `TaskPriority` | Per-user lookup; unique key is `(user_id, priority)`, **not** `Text`. Matters for `Collides` in its seeder and for any reorder. |

## Configuration — `infrastructure/`

| File | Covers |
|---|---|
| `persistence/configuration/TodoListConfiguration.cs` | Unique `(UserId, Name)`; items cascade from the list. |
| `persistence/configuration/ToDoListItemConfiguration.cs` | Note the `ToDoList` spelling in the file name; the class is `TodoListItemConfiguration`. The FK family, `SuggestedTime`'s converter, and four indexes — see *Invariants*. |
| `persistence/configuration/TodoListCategoryConfiguration.cs` | Unique `(UserId, Name)`; lists **SetNull** on category delete. |
| `persistence/configuration/TaskPriorityConfiguration.cs` | Unique `(UserId, Priority)` + a sort index on `Priority`. Moved here from `configuration/activityPlanning/` — the folder was lying about which domain owns it. The `TaskPriority → TodoListItem` relationship is declared **once**, from the dependent side, not here. |
| `persistence/TodoListEntityConfigurationExtensions.cs` | `BaseTodoListConfigure<TEntity>()` — the three check constraints, `DisplayOrder` / `IsDone` / `Note`, and the `OwnsMany(Steps).ToJson()` mapping. Applied by the Routines configurations too. |
| `persistence/TodoListExtensions.cs` | `GetNextDisplayOrder<TEntity>` (generic), `GetNextDisplayOrder(DbSet<TodoListItem>, …, taskPriorityId)`, `GetDisplayOrderById`, `GetGroupIdById`. All take an explicit `userId` as defence-in-depth beside the global filter. The `DbSet<RoutineTodoList>` overload is in Routines. |
| `settings/TodoListSettings.cs` | `MaxItems`, `DisplayOrderGap`; `DisplayOrderStart` is **derived** (`MaxItems * DisplayOrderGap`), not configured. Bound in `Program.cs`; consumed by the reorder/move endpoints and the dev seeder. |
| `persistence/interceptor/TodoListItemCompletionInterceptor.cs` | The sole writer of `TodoListItem.CompletedTimestamp`. Overrides both the sync and async save paths. Registered host-side in `Program.cs`'s `AddInterceptors` call — the class lives here because it names `TodoListItem`, but nothing in this slice wires it up. Fires only on a genuine `IsDone` transition; `ExecuteUpdateAsync` bypasses it. |

Everything general (`BaseEntityConfigure`, `BaseNameTextEntityConfigure`, `NullableIntTimeConverter`, …)
comes from `Sydowwe.Framework`; the `IsManyWithOneUser` / `IsManyWithOneActivity` helpers come from Core.

## Navigation index

### Cross-slice seams — `application/seam/`

| Name | Kind | Responsibility | Path |
|---|---|---|---|
| `TodoListActivityMembershipSource` | Seam impl (keyed) | "This activity is on a to-do list", faceted by `TaskPriorityId`. Composed into the caller's query, so it does no user scoping of its own | `application/seam/TodoListActivityMembershipSource.cs` |
| `TodoListActivityReferenceSource` | Seam impl (keyed) | Counts + repoints both activity columns on an activity merge, including the self-pairing collapse | `application/seam/TodoListActivityReferenceSource.cs` |

### Shared endpoint bases — `application/endpoint/`

Subclassed by both this slice and Routines. This is the only reason a base sits in a slice rather
than in Framework: each is typed on `BaseTodoListItem`.

| Name | Kind | Responsibility | Path |
|---|---|---|---|
| `BaseToggleIsDoneTodoListEndpoint<T>` | Endpoint base | `PATCH /{entity}/toggle-is-done`. Owns `IsDoneLogic` — the step-counted tick/untick state machine — with `FetchAndPrepare` / `AfterItemToggled` / `AfterSave` / `PublishEvent` hooks | `application/endpoint/BaseToggleIsDoneTodoListEndpoint.cs` |
| `BaseToggleStepIsDoneEndpoint<T>` | Endpoint base | `PATCH /{entity}/{itemId}/steps/{stepId}/toggle`. Toggles one step and reconciles the parent's `IsDone` / `DoneCount`; `BeforeToggle` lets Routines reset an expired period first | `application/endpoint/BaseToggleStepIsDoneEndpoint.cs` |
| `BaseChangeDisplayOrderTodoListEndpoint<T>` | Endpoint base | `PATCH /{entity}/change-display-order`. Midpoint reordering between two siblings, transactional, with a rebalance-and-retry when the gap closes. `GroupFilterExpression` is the subclass's grouping column | `application/endpoint/BaseChangeDisplayOrderTodoListEndpoint.cs` |
| `BaseCreateStepEndpoint<T>` | Endpoint base | Append a checklist step | `application/endpoint/steps/BaseCreateStepEndpoint.cs` |
| `BaseUpdateStepEndpoint<T>` | Endpoint base | Edit a step's fields | `application/endpoint/steps/BaseUpdateStepEndpoint.cs` |
| `BaseDeleteStepEndpoint<T>` | Endpoint base | Remove a step | `application/endpoint/steps/BaseDeleteStepEndpoint.cs` |

### `TodoListItem` — `application/endpoint/todoListItem/`

| Name | Kind | Responsibility | Path |
|---|---|---|---|
| `CreateTodoListItemEndpoint` | Endpoint | Create | `…/todoListItem/command/CreateTodoListItemEndpoint.cs` |
| `UpdateTodoListItemEndpoint` | Endpoint | Full update (the frontend round-trips the whole item, pairing included) | `…/todoListItem/command/UpdateTodoListItemEndpoint.cs` |
| `DeleteTodoListItemEndpoint` | Endpoint | Delete | `…/todoListItem/command/DeleteTodoListItemEndpoint.cs` |
| `MoveTodoListItemEndpoint` | Endpoint | `PATCH …/{id}/move` — move to another list and recompute `DisplayOrder`. Overrides `HandleAsync`, so it reproduces the base's exception mapping by hand to keep the 409 on the unique index | `…/todoListItem/command/MoveTodoListItemEndpoint.cs` |
| `ChangePriorityTodoListItemEndpoint` | Endpoint | `PATCH …/{id}/priority` | `…/todoListItem/command/ChangePriorityTodoListItemEndpoint.cs` |
| `ChangeDisplayOrderTodoListItemEndpoint` | Endpoint | Reorder within a priority bucket | `…/todoListItem/command/ChangeDisplayOrderTodoListItemEndpoint.cs` |
| `ToggleIsDoneTodoListItemEndpoint` | Endpoint | Tick / untick; publishes the Core completion event | `…/todoListItem/command/ToggleIsDoneTodoListItemEndpoint.cs` |
| `ToggleStepIsDoneTodoListItemEndpoint` | Endpoint | Tick / untick one checklist step | `…/todoListItem/command/ToggleStepIsDoneTodoListItemEndpoint.cs` |
| `GetAllTodoListItemEndpoint` | Endpoint | `GET /todo-list-item` — the to-do view's list read | `…/todoListItem/query/GetAllTodoListItemEndpoint.cs` |
| `GetByIdTodoListItemEndpoint` | Endpoint | Single read | `…/todoListItem/query/GetByIdTodoListItemEndpoint.cs` |
| `GetDashboardTodoListItemEndpoint` | Endpoint | `GET …/dashboard-widget` — open items due within 3 days, by due date | `…/todoListItem/query/GetDashboardTodoListItemEndpoint.cs` |
| `GetDailyRecapTodoListItemEndpoint` | Endpoint | `GET …/daily-recap` — items completed on a day + per-item logged time via `ITodoListItemLoggedTimeSource`. Half-open UTC day range; explicit `UserId` filter | `…/todoListItem/query/GetDailyRecapTodoListItemEndpoint.cs` |
| `CreateStepTodoListItemEndpoint` | Endpoint | Checklist step create (subclasses the base) | `…/todoListItem/steps/CreateStepTodoListItemEndpoint.cs` |
| `UpdateStepTodoListItemEndpoint` | Endpoint | Checklist step update | `…/todoListItem/steps/UpdateStepTodoListItemEndpoint.cs` |
| `DeleteStepTodoListItemEndpoint` | Endpoint | Checklist step delete | `…/todoListItem/steps/DeleteStepTodoListItemEndpoint.cs` |

### `TodoList` — `application/endpoint/todoList/`

| Name | Kind | Responsibility | Path |
|---|---|---|---|
| `CreateTodoListEndpoint` · `UpdateTodoListEndpoint` · `DeleteTodoListEndpoint` | Endpoints | CRUD off the Framework bases | `…/todoList/command/` |
| `GetByIdTodoListEndpoint` | Endpoint | Single read | `…/todoList/query/GetByIdTodoListEndpoint.cs` |
| `FilterSortTodoListEndpoint` | Endpoint | Name contains + category filter; `CategoryId == -1` is the sentinel for *uncategorised* | `…/todoList/query/FilterSortTodoListEndpoint.cs` |
| `GetSelectOptionsTodoListEndpoint` | Endpoint | Picker options | `…/todoList/query/GetSelectOptionsTodoListEndpoint.cs` |

### `TodoListCategory` — `application/endpoint/todoListCategory/`

| Name | Kind | Responsibility | Path |
|---|---|---|---|
| `CreateTodoListCategoryEndpoint` · `UpdateTodoListCategoryEndpoint` · `DeleteTodoListCategoryEndpoint` | Endpoints | CRUD. Delete **SetNulls** the lists, it does not remove them | `…/todoListCategory/command/` |
| `GetByIdTodoListCategoryEndpoint` | Endpoint | Single read | `…/todoListCategory/query/GetByIdTodoListCategoryEndpoint.cs` |
| `GetFilterSortTodoListCategoryEndpoint` | Endpoint | `POST /todo-list-category/filter-sort` | `…/todoListCategory/query/GetFilterSortTodoListCategoryEndpoint.cs` |
| `GetSelectOptionsTodoListCategoryEndpoint` | Endpoint | Picker options | `…/todoListCategory/query/GetSelectOptionsTodoListCategoryEndpoint.cs` |

### `TaskPriority` — `application/endpoint/taskPriority/`

| Name | Kind | Responsibility | Path |
|---|---|---|---|
| `CreateTaskPriorityEndpoint` · `UpdateTaskPriorityEndpoint` · `DeleteTaskPriorityEndpoint` | Endpoints | CRUD. **No dedicated reorder route** — rank changes go through PUT, one row at a time | `…/taskPriority/command/` |
| `GetAllTaskPriorityEndpoint` · `GetByIdTaskPriorityEndpoint` · `GetSelectOptionsTaskPriorityEndpoint` · `GridTaskPriorityEndpoint` | Endpoints | Reads: list, single, picker options, admin-style grid | `…/taskPriority/query/` |

### DTOs and validators

DTOs sit under `application/dto/` — `request/` (with `request/base/`, `request/step/`,
`request/taskPriority/`), `response/`, and three `filter/` requests. Mapping lives on the DTOs
(`ICreateRequest` / `IUpdateRequest` / `IProjectionResponse`), not a mapper generic. Validators are
the 11 files under `application/validator/`.

### Seeding — `infrastructure/persistence/seeder/`

Band **100–199**; the contract is
`AdhdTimeOrganizer.Core/infrastructure/persistence/seeder/SeederOrderBands.md`.

| Name | Kind | Responsibility | Path |
|---|---|---|---|
| `TaskPrioritySeeder` | Per-user default seeder (Order 100) | The four default ranks — Today / This week / This month / This year. `Collides` keys on `Priority`, not `Text`. **Does not truncate** | `…/seeder/userDefault/TaskPrioritySeeder.cs` |
| `TodoListItemSeeder` | Per-user **dev** seeder (Order 100) | Fixture items across the three near-term priorities, with steps and due dates. Only dev seeders truncate. **The file is named `TodoListSeeder.cs`; the class is `TodoListItemSeeder`** | `…/seeder/dev/TodoListSeeder.cs` |

## Invariants

Which of these the database actually enforces, and which are only app-level:

1. **No reference to `AdhdTimeOrganizer`, and none to another slice.** *Compile-enforced* — the
   csproj has neither. Wanting `AppDbContext` means wanting `DbContext`.
2. **Nothing here may name a Routines or Planning type.** Both edges point inward. The same goes for
   History: the daily recap reads logged time through Core's `ITodoListItemLoggedTimeSource` rather
   than touching `ActivityHistory`. *Compile-enforced*, and pinned from the other side by the route
   smoke tests.
3. **Per-user scoping is the DbContext's job**, via the named global filter on `IEntityWithUser` —
   not the endpoints (`ApplyUserScoping` is a no-op virtual). *DB-enforced at query time* as long as
   these entities stay `IEntityWithUser`. The seams and `TodoListExtensions` deliberately pass an
   explicit `userId` on top of it.
4. **`TodoListItem` is unique on `(UserId, ActivityId, TodoListId)`** — one item per activity per
   list. *DB-enforced* (unique index); surfaces as a 409 through the base endpoints' exception
   mapping.
5. **`TaskPriority` is unique on `(UserId, Priority)`.** *DB-enforced*, checked immediately rather
   than deferred — so a rank swap needs a temporary unused value.
6. **`TodoListCategory` and `TodoList` are each unique on `(UserId, Name)`.** *DB-enforced.*
7. **`DoneCount` bounds:** `>= 0`, `TotalCount` between 2 and 99, `DoneCount <= TotalCount`.
   *DB-enforced (check constraints) — with one hole*: all three are `IS NULL OR …`, so `DoneCount`
   set alongside a NULL `TotalCount` passes. Only reachable by bypassing the validators.
8. **`CompletedTimestamp` is stamped from the ChangeTracker, not at the call sites.** *App-enforced
   only, by an interceptor registered in the host.* There are five places that write `IsDone` and a
   sixth is always one feature away; a site that forgot to stamp would break nothing visible — the
   item would just never appear in a daily recap. Anything bypassing the ChangeTracker —
   `ExecuteUpdateAsync` in particular — bypasses the stamp.
9. **Delete behaviour is deliberate per FK:** owning `Activity` → **Cascade** (the item goes);
   `PairedLeisureActivityId` → **SetNull** (the task is unpaired, not deleted); `TodoList` →
   **Cascade**; `TaskPriority` → **Restrict** (409 while items reference it); `TodoListCategory` →
   **SetNull**. *DB-enforced*, and pinned by `ActivityForeignKeyInventoryTests` and
   `TodoListDeleteBehaviorTests`.
10. **A to-do item is never paired with its own activity.** *App-enforced* — rejected by the create
    and update validators, and collapsed to null by the merge repoint.
11. **The shared bases stay in this project.** Routines depends on them from here; moving them to
    Core would grow the thing that is supposed to shrink. *Convention only.*
12. **Class names are table names, in the `todo` schema.** Renaming a type here is a migration;
    moving it is not — but see the pinned FK constraint name in `summary.md` for the one case where
    *ordering* changed a name.

## Business rules

**The step-counted done model.** An item is either a plain checkbox (`TotalCount` null — `IsDone`
flips, all steps follow) or **step-counted** (`TotalCount` set — it takes `TotalCount` ticks to
finish). `IsDoneLogic` in `BaseToggleIsDoneTodoListEndpoint` is the state machine:

| Call | Item state | Effect |
|---|---|---|
| no `ForceValue` | not done | `DoneCount++`; `IsDone` only at `DoneCount == TotalCount` |
| no `ForceValue` | done | `DoneCount = 0`, `IsDone = false` — a full reset, not a step back |
| `ForceValue = true` | any | `DoneCount++` — it does **not** jump straight to done |
| `ForceValue = false` | done | `DoneCount = TotalCount - 1`, `IsDone = false` |
| `ForceValue = false` | not done | `DoneCount--` (floored at 0) |

Every branch then snaps all steps to the parent's `IsDone`. The consequence worth knowing: reaching
"fully done" always takes `TotalCount` calls, whichever flag you pass.

**Checklist steps and `TotalCount` are independent tracks.** Adding or deleting a step never touches
`TotalCount` — there is no floor, no ceiling, and creating a 100th step succeeds even though
`TotalCount` is capped at 99. `BaseToggleStepIsDoneEndpoint` is where the two meet: ticking the last
open step increments `DoneCount` (or completes a non-counted item), and unticking any step on a fully
complete item reopens it and steps `DoneCount` back by one.

**Display order is a sparse midpoint list, descending.** New items get
`min(DisplayOrder) - DisplayOrderGap`, or `DisplayOrderStart` on an empty group. A reorder takes the
midpoint between the two siblings; when the gap closes the whole group is rebalanced and the midpoint
is retried once. The grouping column differs per subclass — `TaskPriorityId` for to-do items,
`TimePeriodId` for routine ones.

**Temptation bundling is intentionally unvalidated beyond the FK.** The picker is populated from
backlog profiles, but what is stored is the plain `Activity` id, and nothing checks that the paired
activity *has* a backlog profile — this slice cannot see `AdhdTimeOrganizer.ActivityProfiles`.
Restricting the choice to leisure activities is the picker's job. The one rule that *is* enforced is
that an item may not be paired with its own activity.

**The daily recap attributes time by item, never by activity.** Time comes from
`ActivityHistory.TodoListItemId`, stamped when the user accepts the save-to-history prompt.
Widening the match to the item's activity looks like it can only find more rows, and in fact
double-counts whenever two items share an activity. A completed item with no recording appears at
zero seconds; declining the prompt is an ordinary outcome, not a gap.

## Glossary

| Term | Meaning | Code |
|---|---|---|
| Priority | The user's own rank/bucket for a task — the to-do view's columns | `TaskPriority` (`Text` + `Priority` int) |
| Step | A checklist line inside one task; stored as JSON in the task's row, not its own table | `TodoListStep` |
| Step-counted item | A task that takes N ticks to finish rather than one | `TotalCount` / `DoneCount` |
| Temptation bundling | Pairing a task with a leisure activity to make starting it easier | `TodoListItem.PairedLeisureActivityId` |
| Daily recap | "What did I finish today, and how long did it take" | `GetDailyRecapTodoListItemEndpoint` |
| Display order | Sparse descending sort key with gaps, so a reorder is one row's update | `BaseTodoListItem.DisplayOrder`, `TodoListSettings.DisplayOrderGap` |
