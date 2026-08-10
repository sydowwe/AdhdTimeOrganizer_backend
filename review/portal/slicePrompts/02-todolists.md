# Extract `AdhdTimeOrganizer.TodoLists`

The first real slice. `AdhdTimeOrganizer.Core` must already exist and be committed.

This slice has **zero outbound dependencies on other slices** — verified by grepping its
endpoint tree and its entity folder for every other slice's types. It depends only on Core.
That makes it the de-facto pilot: if the extraction pattern is wrong, you find out here.

---

## Ground rules

- Windows box. Use **PowerShell**. **Never round-trip a file through the shell** (no
  `Get-Content | Set-Content`, no `sed -i`) — codepage 1252 double-encodes UTF-8. Use editor
  tools to change file contents.
- `framework/` is a **git submodule**. Do not touch it. Parent repo only.
- `git mv` so history survives. Change only the root namespace prefix. **Do not rename types.**
- **Never reference the host from a slice.** TodoLists → Core + `Sydowwe.Framework`. Host → TodoLists.
- Slice services take a plain **`DbContext`**, never `AppDbContext`. The `DbContext` →
  `AppDbContext` alias is already registered in DI.
- Confirm the baseline first: `dotnet test AdhdTimeOrganizer.IntegrationTests` =
  **198 passed, 6 skipped, 0 failed**. Match it at the end.

## Registering with the host — four places, none break the build

1. `AdhdTimeOrganizer/Program.cs` → FastEndpoints `o.Assemblies` (`DisableAutoDiscovery = true`).
   Missing → endpoints **404 silently**.
2. `config/dependencyInjection/ModuleServiceExtensions.cs` → `ModuleAssemblies`. **Not also in
   the `AddDependencyInjection` sweep** — it `Except`s this list, and being in both registers
   every service twice, doubling every `IEnumerable<T>` (seeders run twice). Nothing throws.
3. `infrastructure/persistence/AppDbContext.cs` → `ApplyHostConfigurations` (~line 128), a
   hard-coded list of `ApplyConfigurationsFromAssembly` calls. Missing → entities absent from
   the model.
4. `AdhdTimeOrganizer.sln`.

## The migration gate

`BaseEntityConfigure` derives table and column names from the **class** name, not the
namespace — moving types changes **no** table or column names. Run
`dotnet ef migrations add TodoListsSlice` and confirm `Up`/`Down` are **empty**. If not, you
renamed something. `AppDbContextModelSnapshot.cs` will diff hugely with no schema in it;
never hand-edit it.

## Security invariant

`AppDbContext.OnModelCreating` applies the global query filter over every `IEntityWithUser` —
that is what keeps other users' rows out, **not** the endpoints (`ApplyUserScoping` is a no-op
virtual). Slice entities stay `IEntityWithUser` and keep their FKs and cascades.

---

## What moves

`application/endpoint/todoList/` holds **59** `.cs` files. **20 of them are routine files and
must stay behind** — anything under `routineTodoList/` or `routineTimePeriod/` belongs to the
next slice. Move the other ~39.

- **Endpoints** — `application/endpoint/todoList/**` minus the routine folders. This includes
  the shared bases at the folder root (toggle / step / reorder), which **stay in TodoLists**:
  Routines will reference them from here.
- **Entities** — `domain/model/entity/todoList/` (9 files). `BaseTodoListItem`, `TodoList`,
  `TodoListItem`, `TodoListCategory`, `TodoListStep` come here. `RoutineTodoList`,
  `RoutineTimePeriod` and `RoutinePeriodCompletion` **stay behind for the Routines slice.**
- **`TaskPriority`** — entity and configuration. ⚠ **The folder structure lies:**
  `infrastructure/persistence/configuration/activityPlanning/TaskPriorityConfiguration.cs` is
  filed under Planning but the entity belongs **here**. Do not assign files to projects by
  directory.
- **Configurations** — `TodoListConfiguration`, `TodoListCategoryConfiguration`,
  `ToDoListItemConfiguration`, plus
  `configuration/extensions/TodoListEntityConfigurationExtensions.cs` (`BaseTodoListConfigure`).
- **Query helpers** — `infrastructure/persistence/extensions/TodoListExtensions.cs`.
- **DTOs, validators, seeders** — the to-do request/response/filter DTOs, their validators, and
  `TaskPrioritySeeder` (its `Order` was rebased into the 100–199 band during the Core commit).

---

## Slice-specific gotchas

**`BaseChangeDisplayOrderTodoListEndpoint` currently takes `AppDbContext` in its primary
constructor.** That is a host reference and will not compile once the file is in a slice
project. Change it to a plain `DbContext` — the DI alias already resolves it. Sweep the rest of
the moved files for the same pattern before you build; it is the most likely single cause of a
red build here.

**The shared bases are load-bearing for Routines.** `BaseTodoListItem`, `TodoListStep`,
`BaseTodoListConfigure`, `TodoListExtensions` and the toggle/step/reorder endpoint bases all
stay in TodoLists and are referenced from Routines next. Do not move them into Core "to be
safe" — they are to-do concepts, not core ones, and Core is meant to shrink.

**`TodoListItem` has no `PlannerTask` navigation.** The `TodoListItem ↔ PlannerTask`
relationship is owned entirely from the Planning side (`PlannerTask.TodolistItemId`). If you
find yourself wanting a navigation back into Planning, stop — that would create the cycle this
whole split exists to avoid.

**`DoneCount` is step-counted.** `BaseTodoListItem.SetDone` only rewrites `DoneCount` when
`TotalCount.HasValue`. Do not "fix" that guard; a `DoneCount` without a `TotalCount` is not a
state the app can produce.

---

## Done when

- `dotnet build AdhdTimeOrganizer.sln` clean
- `dotnet test` = **198 passed, 6 skipped, 0 failed**
- `dotnet ef migrations add TodoListsSlice` produces an **empty** `Up`/`Down`
- one to-do endpoint manually smoke-tested (a missing FastEndpoints registration is a 404, not
  a build error)
- `docs/summary.md` + `docs/domain-map.md` written in the new project
- one commit