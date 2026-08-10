# Extract `AdhdTimeOrganizer.Planning` (planner **and reminders**)

`Core`, `TodoLists`, `Routines` and `History` must already exist and be committed. Planning
depends on **TodoLists** (`PlannerTask.TodolistItemId`) and on **History** (the suggestions
endpoint reads `ActivityHistory`) — extracting it before either produces a slice→host
reference that will not compile.

This is the largest slice, ~49 endpoints (44 planner + 5 reminder).

> **⚠ Reminders is part of this slice. There is no `AdhdTimeOrganizer.Reminders`.**
> `06-reminders.md` was deleted on 2026-08-11; this section replaces it. The reason is that the
> portal's `Reminder` ↔ `PlannerTask` coupling is **bidirectional**, which the old prompt missed:
> - **Reminders → Planning** — `Reminder.PlannerTaskId` FK + navigation with a cascade;
>   `ReminderRegistrationService` reads `PlannerTasks.Include(t => t.Calendar)` for
>   `Calendar.Date` + `StartTime` + `Status`, and `UserPlannerSettings.{RemindersEnabled,
>   ReminderMinutesBefore}`; `Create`/`UpdateReminderEndpoint` existence-check `PlannerTasks`.
> - **Planning → Reminders** — six planner-task endpoints (`Delete`, `BatchDelete`, `Update`,
>   `PatchSpan`, `PatchStatus`, `ApplyTemplate`) inject `IReminderRegistrationService`, plus the
>   host-side `TodoListItemIsDoneChangedEventHandler`.
>
> Two separate projects would therefore need **two** seams in Core — an id-only sync interface and
> a planner-task-instant source — and the second one abstracts "read this task's start time" for a
> single consumer while inheriting the seam's silent-misregistration failure mode, which for a
> notification path means reminders quietly stop syncing. The coupling is not accidental:
> `Reminder.RemindAt` is documented as a *cache* of the task's instant, recomputed on every sync.
> One slice, no seam. Folding it in also means the `IReminderRegistrationService` interface can
> move into the slice next to its implementation instead of staying in `domain/serviceContract/`.
>
> Reminders is **not** going into Core, for the record: `Reminder` carries a hard FK to
> `PlannerTask` (which Core cannot reference) and pulls `Sydowwe.Framework.Contracts.reminders` +
> notification payloads onto the hub project every slice consumes.

---

## Ground rules

- Windows box. Use **PowerShell**. **Never round-trip a file through the shell** (no
  `Get-Content | Set-Content`, no `sed -i`) — codepage 1252 double-encodes UTF-8. Use editor
  tools to change file contents.
- `framework/` is a **git submodule**. Do not touch it. Parent repo only.
- `git mv` so history survives. Change only the root namespace prefix. **Do not rename types.**
- **Never reference the host from a slice.** Planning → TodoLists + History + Core +
  `Sydowwe.Framework` + `Sydowwe.Framework.Contracts` (the reminder half needs
  `IReminderRegistry` / `IQuietHoursReader`). Host → Planning.
- Slice services take a plain **`DbContext`**, never `AppDbContext`. The alias is registered.
- Confirm the baseline first: `dotnet test AdhdTimeOrganizer.IntegrationTests` =
  **228 passed, 6 skipped, 0 failed**. Match it at the end.

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

### …and the reminder half

- **Endpoints** — all five: `application/endpoint/reminder/command/{Create,Update,Delete}` and
  `query/{GetById,GetByDate}`.
- **Entity** — `domain/model/entity/reminder/Reminder.cs` +
  `infrastructure/persistence/configuration/reminder/ReminderConfiguration.cs`.
- **Service** — `application/service/reminder/ReminderRegistrationService.cs` **and its interface**
  `domain/serviceContract/IReminderRegistrationService.cs`. The interface only lived in a separate
  folder to keep the host's planner endpoints off the implementation; both sides are now inside
  this slice, so put them together.
- **DTOs and validator** — `dto/request/reminder/ReminderRequest.cs`,
  `dto/response/reminder/{ReminderResponse,ReminderOnDateResponse}.cs`,
  `validator/ReminderValidator.cs`.
- No reminder seeder exists. If you add one it goes in **Planning's 400–499 band** — the old
  500–599 Reminders band is retired.
- **Stays host-side:** `application/eventHandler/TodoListItemIsDoneChangedEventHandler.cs`, which
  resolves `IReminderRegistrationService` from a scope. Same rule as the other event handlers —
  host → slice is fine.
- **Do not touch** `AdhdTimeOrganizer/reference/mojaCore/ReminderPersonalDataProvider.cs`; that
  folder is foreign reference code.

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

### Reminder-specific (carried over from the deleted `06-reminders.md`)

**⚠ There are two unrelated "Reminders" in this solution. Do not conflate them.**
- `framework/Sydowwe.Reminders` is an **opt-in framework module inside the git submodule**, with
  its own `ReminderDefinition` entity, ledgers, retention job and registrar. **Do not touch it, do
  not move anything into it, and do not merge the two.**
- The portal's `Reminder` entity and `ReminderRegistrationService` are what you are moving.

Namespace collisions are likely (`Sydowwe.Reminders.*` vs `AdhdTimeOrganizer.Planning.*` pulling
in both). Fully qualify or alias rather than renaming any type.

**`ReminderRegistrationService` talks to the framework module only through
`Sydowwe.Framework.Contracts`** (`IReminderRegistry`, `NotificationType`, the payload records).
Those contract interfaces are the seam — keep going through them; never reference
`Sydowwe.Reminders` directly.

**`IQuietHoursReader` must keep resolving to Notifications' `QuietHoursReader`.** The Reminders
*module* ships a `NoQuietHoursReader` for hosts without Notifications, deliberately carrying no
lifetime marker so it is never auto-registered — an auto-registered no-op silently disables quiet
hours everywhere. If this project's assembly ends up in a marker scan that picks it up, quiet
hours break with no error. Verify after wiring.

**`AdhdTimeOrganizer.IntegrationTests/Modules/ModuleWiringTests.cs` pins the composition root** —
double registration, a missing `ModuleAssemblies` entry, the `IQuietHoursReader` resolution. Run
it specifically, not just the whole suite. `Reminders/ReminderRegistrationTests.cs` and
`Reminders/ReminderSeedHelper.cs` will need their `using` lines updated; they stay in the parent
test project.

**Keep the `Reminder` → `PlannerTask` cascade.** It is the reason the FK is real rather than the
module's string `SubjectType`/`SubjectId` pair — no planner-task delete path can leave an orphaned
reminder row. The module-side `ReminderDefinition` is *not* reached by that cascade, which is why
the delete endpoints read reminder ids **before** deleting the task and cancel through the
registry afterwards. Both halves are now in your slice; do not "simplify" either away.

---

## Done when

- `dotnet build AdhdTimeOrganizer.sln` clean
- `dotnet test` = **228 passed, 6 skipped, 0 failed**, with `ModuleWiringTests` and
  `Reminders/ReminderRegistrationTests` green
- `dotnet ef migrations add PlanningSlice` produces an **empty** `Up`/`Down`
- one planner-task endpoint, one calendar endpoint and one reminder endpoint manually smoke-tested
  (a missing FastEndpoints registration is a 404, not a build error)
- toggling a planner task's done state still fans out to its linked to-do item — proves the
  event wiring survived
- `IQuietHoursReader` still resolves to Notifications' `QuietHoursReader`, not the no-op
- `docs/summary.md` + `docs/domain-map.md` written in the new project
- one commit