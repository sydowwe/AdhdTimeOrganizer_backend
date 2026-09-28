# AdhdTimeOrganizer.Planning — Agent Summary

**Purpose:** planning a day — the `Calendar` row, the four planner-task shapes that hang off it
(one-off, repeating, template line, suggestion), day templates and the four ways of applying one,
per-user task importance and planner settings — **and** reminders, which are part of this slice
rather than a project of their own.

## Bounded context

Owns: `Calendar`, `BasePlannerTask` and its three concrete shapes (`PlannerTask`,
`RepeatingPlannerTask`, `TemplatePlannerTask`), `TaskPlannerDayTemplate`, `TaskImportance`,
`UserPlannerSettings`, `Reminder`, the three `PlannerSuggestionFrom*` read-models, their EF
configurations, ~59 endpoints, their DTOs and 14 validator files, the five domain services
(`PlannerStreakService`, `PlanVsActualTrendService`, `SkipSummaryService`, `CalendarDayFactory`,
`HolidayCalendar`), the
three application services (`CalendarProvisioner`, `PlannerStreakReader`,
`ReminderRegistrationService`), the two Core seam implementations, the two cross-slice event
handlers, and five seeders.

Tables live in the **`planning`** schema (`ModuleSchemas.cs`, keyed on this assembly).

Does **not** own — and must never reference: `AppDbContext`, `Program.cs`, the migrations, the DI
wiring, the **Google Calendar integration** (host-side, carries `Google.Apis.Auth`), the
**suggestion-pattern materialized views** themselves (SQL, installer, refresh interceptor and job are
host-side; only the three entity mappings live here), `PlannerTaskSeeder` (host-side, because it
links a seeded planner task to a seeded `TodoListItem`), and `TaskPriority` (that went to TodoLists,
despite its configuration having lived in the Planning folder — `TaskImportance` is ours).

## Dependency seams

- **References:** `AdhdTimeOrganizer.Core`, `Sydowwe.Framework`, `Sydowwe.Framework.Contracts`. No
  host reference and **zero outbound slice edges** — see the comment in the csproj.
- **Referenced by:** `AdhdTimeOrganizer` (the host), for the Google Calendar services that read
  `Calendar`, the GDPR export, and the cross-slice relationship declaration below.
- **`Planning → TodoLists` does not exist, deliberately.** `PlannerTask.TodolistItemId` is a real FK
  to `todo_list_item` with `ON DELETE SET NULL` and keeps that behaviour, but it is **id-only with no
  navigation property** — the navigation was the only thing forcing a project reference, and nothing
  ever read it. The relationship is declared host-side in
  `AppDbContext.ConfigureCrossSliceRelationships`, where both types are visible. Do not add
  `PlannerTask.TodolistItem` back.
- **`Planning → History` does not exist either.** `GetSuggestionsRepeatingPlannerTaskEndpoint` reads
  `PlannerSuggestionFromActivityHistory`, an entity mapped over the `mv_activity_history_pattern`
  materialized view — a *different* type from History's `ActivityHistory`, which this project never
  names. The view is the seam.
- **Two Core seams are implemented here**, both in `application/seam/`:
  - `PlannerTaskActivityReferenceSource` (`IActivityReferenceSource`, keyed
    `ActivityReferenceSourceKeys.PlannerTask`) exposes the `ActivityId` column to Core's
    activity-merge/delete machinery and repoints it on merge. It covers **all three** task tables —
    they share `BasePlannerTask` in C# but each has its own table, so each needs its own clause.
  - `CalendarDayLookup` (`ICalendarDayLookup`, resolved by type) publishes day metadata so History's
    calendar dashboard can join against it without referencing Planning.

  The reference seam resolves **by string key**, not by type. A missing, misregistered or renamed key
  is silent — no build error, the consumer just quietly stops finding rows.
- **Two `IEventHandler`s live here**, in `application/eventHandler/`:
  `RoutineTodoListIsDoneChangedEventHandler` and `TodoListItemIsDoneChangedEventHandler`. The
  publishers are Routines and TodoLists, but every row they touch is a `PlannerTask`, so the reaction
  belongs to the slice that owns it. Both event records are declared in Core, so neither slice
  references the other; both handlers resolve a `DbContext` from a **fresh scope**, because the
  publisher's scope is not guaranteed to outlive the dispatch.
- **Reminders reach `framework/Sydowwe.Reminders` only through `Sydowwe.Framework.Contracts`**
  (`IReminderRegistry`, `IQuietHoursReader`, the payload records), and only through
  `ReminderRegistrationService`.

## Gotchas — things that will bite you

- **Everything here takes a plain `DbContext`, never `AppDbContext`.**
  `ModuleServiceExtensions.AddModuleServices` aliases `DbContext` → `AppDbContext` at runtime (global
  query filters and all). At the call site that means no `dbContext.PlannerTasks` — use
  `dbContext.Set<PlannerTask>()`.

- **`Microsoft.EntityFrameworkCore` is a global using** (declared in the csproj) — nearly every file
  here names `DbContext`. Don't be surprised by files with no EF using line.

- **Two unrelated "Reminders" exist.** `framework/Sydowwe.Reminders` is an opt-in framework module in
  the git submodule with its own `ReminderDefinition`, ledgers and retention job. This slice's
  `Reminder` owns **user intent only** — title, instant, what it is attached to. Never mirror the
  module's status / next-occurrence / dispatch history onto the portal row, never reference
  `Sydowwe.Reminders` directly, and never merge the two.

- **`Reminder.RemindAt` is a cache when the reminder is attached to a task**, recomputed from
  `Calendar.Date` + `StartTime` on every sync. The task stays authoritative. Any write path that
  moves a task's time or changes its status must call
  `IReminderRegistrationService.SyncForPlannerTasksAsync`, or the reminder keeps firing at the old
  time with nothing in the logs.

- **The `Reminder` → `PlannerTask` cascade does not reach the module.** Deleting a task takes its
  portal reminder rows with it by FK, but the module-side `ReminderDefinition` is not in that graph —
  which is why every delete path reads the reminder ids *before* deleting and calls `CancelManyAsync`
  after the commit. Same reason `PlannerTemplateApplier.StagedTaskDeletions` reads the ChangeTracker
  before the save.

- **`ICalendarProvisioner.EnsureForDateAsync` commits.** Call it *before* staging your own writes —
  anything already pending on the ambient `DbContext` goes in with the calendar. All three callers
  resolve up front, which is why the two CRUD ones do it in `BeforeMapping` and not `AfterMapping`.
  It also **scopes by hand** (`IgnoreQueryFilters()` + explicit `UserId`), same as `CalendarSeeder`
  and for the same reason: it is told which user to act for, and filtered it would read no row and
  try to insert a duplicate onto the unique `(user_id, date)`.

- **`GetPlanVsActualTrendPlannerTaskEndpoint` and `GetSkipSummaryPlannerTaskEndpoint` filter `UserId`
  by hand**, not through the host's
  global `IEntityWithUser` filter — the slice compiles against a plain `DbContext` and cannot see it,
  and the id is already in hand for the timezone. The four batched calendar entry points do the
  opposite and lean on the filter alone, which is why an unowned day comes back
  `succeeded: false` rather than 403.

- **Each per-day batch failure must clear the ChangeTracker before continuing.** A failed
  `SaveChanges` leaves that day's staged entities on the tracker and the next day's save re-sends
  them, turning one bad day into a whole failed batch. The batched template apply re-reads the
  template afterwards, since clearing detaches it.

- **The rule/query splits are load-bearing, in four places.** `PlannerStreakReader` owns the
  qualifying-task predicate and the day boundary, `PlannerStreakService` owns the rules;
  `GetPlanVsActualTrendPlannerTaskEndpoint` owns the eligibility predicate, `PlanVsActualTrendService`
  owns the arithmetic; `GetSkipSummaryPlannerTaskEndpoint` / `SkipSummaryService` split the same way,
  and the two range reads share one last-eligible-day rule (`TaskPlannerHelper.LastDayThatIsOver`) because
  the client renders them as the same "this period"; `PlannerTemplateApplier` and `CalendarDayTypeRule` each have exactly one
  implementation behind two callers. A second place deciding any of these is how the two ends of a
  feature start disagreeing about the same number.

- **`CalendarSeeder` does not subclass `BasePerUserDefaultSeeder`** — its key is a date range rather
  than a row set, so it hand-rolls `SetupDefaults` / `ResetDefaults` (and `ResetDefaults` is a
  logged no-op) and does its own `IgnoreQueryFilters()`. Deliberate; do not normalise it. It fills
  per *missing date*, not "does the year have any rows", because `(user_id, date)` is unique.

- **Calendar presence does not mean "the user planned this day".** The seeder fills whole years, so
  inside the seeded window every date has a row whether or not it was ever touched. An empty state
  reads `DayPlanResponse.HasPlan` (`Tasks.Count > 0`), never the presence of a calendar.

- **`TaskImportance.OptionalMarkerValue` (666) and `CriticalMarkerValue` (999) are read by code, not
  just seeded.** `BasePlannerTask.IsOptional` compares against 666 exactly, and `(user_id,
  importance)` is unique — so reordering importance levels needs a temporary value, and the sentinels
  must survive it.

- **Seeder `Order` is banded.** Planning owns **400–499**: `TaskImportanceSeeder` 400,
  `CalendarSeeder` 410, `UserPlannerSettingsSeeder` 420 (per-user defaults); `TaskPlannerDayTemplateSeeder`
  400, `TemplatePlannerTaskSeeder` 410 and the host-side `PlannerTaskSeeder` 420 (dev fixtures — the
  two kinds are ordered independently). See
  `AdhdTimeOrganizer.Core/infrastructure/persistence/seeder/SeederOrderBands.md` before adding one
  anywhere in the solution.

- **Class names are table names, namespaces are not.** Table/column names come from the *class* name
  via `BaseEntityConfigure`, so moving a type between folders or namespaces is not a migration —
  renaming one is. Note `TemplateTaskConfiguration.cs` configures `TemplatePlannerTask`; the file name
  and the entity name have already drifted apart.

- **Registering the slice with the host is four places, none of which break the build.**
  FastEndpoints `o.Assemblies` in `Program.cs` (missing → every planner route silently 404s);
  `ModuleServiceExtensions.ModuleAssemblies` (being in *both* that list and the
  `AddDependencyInjection` `AppDomain` sweep doubles every `IEnumerable<T>`, so each seeder and each
  seam registration runs/resolves twice, silently); `AppDbContext.ApplyHostConfigurations`
  (missing → the tables vanish from the model); and the solution file.

- ⚠ **The host's own configuration scan must stay anchored on `AppDbContext`.** It used to say
  `typeof(PlannerTaskConfiguration).Assembly` — the host assembly, until this extraction moved that
  type here, at which point the line silently became a second Planning scan and every remaining host
  configuration fell out of the model. Nothing failed to build; the next `migrations add` produced
  ~500 lines of table renames and dropped partition keys.

## Extension playbook

- **Add a planner-task field:** put it on `BasePlannerTask` only if all three shapes want it (each has
  its own table and its own configuration, so a base field is three columns and three migrations
  worth of surface). Then the DTO bases (`BasePlannerTaskRequest` / `BasePlannerTaskResponse`), the
  validators, and — if it is execution state — `CopyToDaysPlannerTaskEndpoint`, which deliberately
  does **not** carry execution state across a copy.
- **Add a fourth task shape:** entity → configuration (`BaseEntityConfigure()` first) →
  `ModuleSchemas` needs nothing (assembly-keyed) → **add a clause to
  `PlannerTaskActivityReferenceSource`**, both in `ReferencingActivityIds` and `RepointAsync`.
  Forgetting that file is a silent undercount and a merge that strands rows; nothing fails to build.
- **Add an endpoint:** check `framework/Sydowwe.Framework/application/endpoint/base/` first — most of
  what is here is a one-line subclass. Convention is `<Verb><Entity>Endpoint`; mapping lives on the
  DTOs. If it writes a planner task's time or status, call `SyncForPlannerTasksAsync`; if it deletes
  one, capture the reminder ids first.
- **Add a conflict-resolution mode:** it goes in `PlannerTemplateApplier.Stage` and nowhere else —
  both the single-day and the batched apply route through it, and `StagedTaskDeletions` reads the
  ChangeTracker so a new mode's deletions are captured without threading a list through.
- **Add a per-user default:** subclass `BasePerUserDefaultSeeder<T>`, pick an `Order` in 400–499, and
  make `Collides` match the entity's **unique index**, not its display text.
- **Add a user-scoped entity:** it will fail `UserDataExportAndDeletionTests` until it is listed as
  exported or as a deliberate gap. That is the test working.

## Deeper reference

- `domain-map.md` — model, invariants, the derived-number rules, glossary, navigation index
- `testing.md` — test strategy, the load-bearing guards, and the known gaps
- `README.md` — business intro and module-specific setup
