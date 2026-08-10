# Extract `AdhdTimeOrganizer.Planning`

`Core`, `TodoLists`, `Routines` and `History` must already exist and be committed. Planning
depends on **TodoLists** (`PlannerTask.TodolistItemId`) and on **History** (the suggestions
endpoint reads `ActivityHistory`) — extracting it before either produces a slice→host
reference that will not compile.

This is the largest slice, ~44 endpoints.

---

## Ground rules

- Windows box. Use **PowerShell**. **Never round-trip a file through the shell** (no
  `Get-Content | Set-Content`, no `sed -i`) — codepage 1252 double-encodes UTF-8. Use editor
  tools to change file contents.
- `framework/` is a **git submodule**. Do not touch it. Parent repo only.
- `git mv` so history survives. Change only the root namespace prefix. **Do not rename types.**
- **Never reference the host from a slice.** Planning → TodoLists + History + Core +
  `Sydowwe.Framework`. Host → Planning.
- Slice services take a plain **`DbContext`**, never `AppDbContext`. The alias is registered.
- Confirm the baseline first: `dotnet test AdhdTimeOrganizer.IntegrationTests` =
  **219 passed, 6 skipped, 0 failed**. Match it at the end.

## Registering with the host — four places, none break the build

1. `AdhdTimeOrganizer/Program.cs` → FastEndpoints `o.Assemblies` (`DisableAutoDiscovery = true`).
   Missing → endpoints **404 silently**.
2. `config/dependencyInjection/ModuleServiceExtensions.cs` → `ModuleAssemblies`. **Not also in
   the `AddDependencyInjection` sweep** — it `Except`s this list; being in both registers every
   service twice and doubles every `IEnumerable<T>`. Nothing throws.
3. `infrastructure/persistence/AppDbContext.cs` → `ApplyHostConfigurations` (~line 128).
4. `AdhdTimeOrganizer.sln`.

## The migration gate

Table and column names come from the **class** name via `BaseEntityConfigure`, not the
namespace. Run `dotnet ef migrations add PlanningSlice` and confirm `Up`/`Down` are **empty**.
`AppDbContextModelSnapshot.cs` diffs hugely with no schema in it; never hand-edit it.

## Security invariant

The global query filter in `AppDbContext.OnModelCreating` over every `IEntityWithUser` is what
scopes reads — **not** the endpoints (`ApplyUserScoping` is a no-op virtual). Keep planning
entities `IEntityWithUser` with their FKs and cascades intact.

---

## What moves

- **Endpoints** — `application/endpoint/activityPlanning/**` (~44 files): planner tasks,
  repeating planner tasks, template planner tasks, day templates, calendar.
- **Entities** — `PlannerTask`, `BasePlannerTask`, the repeating/template planner task types,
  `TaskImportance`, `UserPlannerSettings`, and the day-template types, with their configurations.
- **`Calendar`** — ⚠ **the folder structure lies.** `domain/model/entity/Calendar.cs` sits at
  the entity root rather than under `activityPlanning/`, but it belongs to Planning. Do not
  assign files to projects by directory.
- **Helper** — `application/helper/TaskPlannerHelper.cs`.
- **Seeders** — `UserPlannerSettingsSeeder`, `CalendarSeeder`, and the dev `PlannerTaskSeeder`,
  `TemplatePlannerTaskSeeder`, `TaskPlannerDayTemplateSeeder`. Their `Order` values were rebased
  into the 400–499 band during the Core commit.
- **DTOs and validators** for the above.

---

## Slice-specific gotchas

**`TaskPriority` is NOT yours — it went to TodoLists.** Its configuration used to sit in
`configuration/activityPlanning/`, which is why it looks like a Planning type. It moved during
the TodoLists commit. `TaskImportance` *is* Planning's. Confirm both before touching anything.

**Google Calendar stays host-side, deliberately.** `GoogleCalendarService`,
`IGoogleCalendarService`, `ConnectGoogleCalendarEndpoint`, `GetGoogleCalendarAuthUrlEndpoint`,
`SyncCalendarToGoogleEndpoint` and their DTOs/validators stay in `AdhdTimeOrganizer`. They will
reference the `Calendar` entity in your slice — that is host → slice, which is fine. Do not pull
them in to "keep calendar together"; the Google integration is a portal concern and carries a
`Google.Apis.Auth` dependency that must not spread.

**`CalendarSeeder` is the one per-user default seeder that does not subclass
`BasePerUserDefaultSeeder`** — its key is a date range rather than a row set, so it hand-rolls
`SetupDefaults` / `ResetDefaults` and does its own `IgnoreQueryFilters()`. That is deliberate.
Do not "normalise" it onto the base class during the move.

**Seeder reads must keep `IgnoreQueryFilters()`.** `UserScoping` is on, so an `IEntityWithUser`
read inside a seeder is scoped to the *ambient* user; a seeder told to seed a different user
would read back zero rows and re-insert everything. The explicit `UserId` predicate is the
scoping.

**The completion fan-out runs through events, and it must stay that way.** `PlannerTask` and
`TodoListItem` complete each other through in-process FastEndpoints events
(`PlannerTaskIsDoneChangedEvent`, `TodoListItemIsDoneChangedEvent`,
`RoutineTodoListIsDoneChangedEvent`), whose records now live in **Core**. The entity FK is
one-way (`PlannerTask.TodolistItemId`; `TodoListItem` has no navigation back). The handlers in
`application/eventHandler/` **stay host-side** for now — do not move them and do not replace an
event with a direct call.

**`GetSuggestionsRepeatingPlannerTaskEndpoint` reads `ActivityHistory`** — that is the
`Planning → History` edge and the reason History must already be extracted. The suggestion
*materialized views* and their interceptor/installer/job stay host-side; only this endpoint's
read moves with you.

---

## Done when

- `dotnet build AdhdTimeOrganizer.sln` clean
- `dotnet test` = **219 passed, 6 skipped, 0 failed**
- `dotnet ef migrations add PlanningSlice` produces an **empty** `Up`/`Down`
- one planner-task endpoint and one calendar endpoint manually smoke-tested (a missing
  FastEndpoints registration is a 404, not a build error)
- toggling a planner task's done state still fans out to its linked to-do item — proves the
  event wiring survived
- `docs/summary.md` + `docs/domain-map.md` written in the new project
- one commit