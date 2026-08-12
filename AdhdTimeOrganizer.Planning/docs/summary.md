# AdhdTimeOrganizer.Planning — summary

The fifth vertical slice. Everything about **planning a day**: the calendar, the four planner-task
types, day templates, task importance, per-user planner settings, the suggestion read-models — and
**reminders**, which are part of this slice rather than a project of their own.

Navigation index: [`domain-map.md`](domain-map.md). Read this file first; open the map when you need
to find a specific type.

## What is in here

| Area | Types | Endpoints |
|---|---|---|
| Calendar | `Calendar` | 4 (by-date, by-id, filter-sort, update) |
| Planner tasks | `BasePlannerTask`, `PlannerTask` | 10 |
| Repeating planner tasks | `RepeatingPlannerTask` | 6 (incl. suggestions) |
| Template planner tasks | `TemplatePlannerTask` | 7 |
| Day templates | `TaskPlannerDayTemplate` | 7 (incl. suggestions) |
| Task importance | `TaskImportance` | 7 |
| Planner settings | `UserPlannerSettings` | 2 |
| Reminders | `Reminder`, `ReminderRegistrationService` | 5 |
| Suggestion read-models | `PlannerSuggestionFromPlannerTask`, `…FromActivityHistory`, `…FromDayTemplate` | — |

Plus their DTOs, 12 validators, `TaskPlannerHelper`, and five seeders (`CalendarSeeder`,
`TaskImportanceSeeder`, `UserPlannerSettingsSeeder` as per-user defaults; `TaskPlannerDayTemplateSeeder`
and `TemplatePlannerTaskSeeder` as dev fixtures). Seeder `Order` values stay in the **400–499 band**
— see `AdhdTimeOrganizer.Core/infrastructure/persistence/seeder/SeederOrderBands.md`.

## Project references — Core and the framework, nothing else

```
AdhdTimeOrganizer.Planning
  ├── AdhdTimeOrganizer.Core
  ├── Sydowwe.Framework
  └── Sydowwe.Framework.Contracts
```

**Zero outbound slice edges.** No reference to TodoLists, History or Routines, and none to the host.
Two edges that the planning docs predicted turned out not to need one:

### 1. `Planning → History` does not exist

`review/portal/04-slicing-verification.md` and `slicePrompts/05-planning.md` both record that
`GetSuggestionsRepeatingPlannerTaskEndpoint` "reads `ActivityHistory`", making History a
prerequisite. It does not. It reads **`PlannerSuggestionFromActivityHistory`** (named
`ActivityHistoryPattern` until the suggestion read-models were renamed), the entity mapped over the
`mv_activity_history_pattern` materialized view — a *different* type that was never in the History
slice and moved here with its two siblings. The materialized view is itself the decoupling: Planning
reads a view derived from History's table without naming a single History type.

The view SQL, `SuggestionPatternViewInstaller`, `SuggestionPatternRefreshInterceptor` and
`SuggestionPatternRefreshJob` all stay **host-side**, as planned. Only the three entity classes and
their configurations live here, because this slice is their only consumer.

### 2. `Planning → TodoLists` is declared host-side instead

`PlannerTask.TodolistItemId` is a real FK to `todo_list_item` with `ON DELETE SET NULL`, and it
keeps that behaviour. What was removed is the **navigation property** `PlannerTask.TodolistItem`,
which nothing ever read — every call site (`PatchPlannerTaskStatusEndpoint`,
`ApplyTemplatePlannerTaskEndpoint`, `PlannerTaskResponse`, both `*IsDoneChangedEvent` handlers)
carries the bare id and looks the item up on the TodoLists side. The navigation was the only thing
forcing a project reference.

The relationship is now declared in `AppDbContext.ConfigureCrossSliceRelationships`, where both
entity types are visible and where the host already owns the schema. Column, nullability, delete
behaviour and the pinned constraint name are unchanged — `dotnet ef migrations add PlanningSlice`
produced an **empty** `Up`/`Down`.

⚠ This is a *silent* seam in both directions, like `IActivityMembershipSource`: nothing fails to
build if the host-side declaration is deleted, the FK just vanishes from the model.
`PlanningRouteSmokeTests.PlannerTaskToTodoListItem_ForeignKey_SurvivesTheSliceSplit` is what keeps it
honest — it asserts the FK, its column, its `SetNull`, its constraint name, and the absence of
navigations on both ends.

## Day-plan completion streak

The flame chip on the home page. It replaces a localStorage counter in the SPA that could only ever
count days *this browser* had the app open, and whose un-tick path guessed at its own inverse.

**Derived, never stored.** There is no entity, no column, no migration and no repair path — the
streak is recomputed from `planner_task` rows on every read. This is the opposite of the Routines
slice's `Streak` / `BestStreak` columns, and the difference is not taste: a routine period *wipes its
items* on reset, so its counter has to be the record, while planner days keep every task row forever.
Deriving it makes un-ticking an exact inverse (there is no increment to undo), makes editing a past
day recompute across the gap it opens, and makes drift unrepresentable.

The rules, all settled against the frontend's B1 ask rather than inherited from the client:

| Question | Answer |
|---|---|
| What makes a day count? | Every **qualifying** task is `Completed`. Qualifying = not `IsBackground`, not `IsOptional`, not `Cancelled`. |
| Does a skip break the day? | **No.** `Cancelled` leaves the denominator entirely — the app already presents skipping as a legitimate way to close a task. |
| Does skipping everything earn a day? | **No.** Zero denominator ⇒ the day is *empty*, not complete. |
| Does an unplanned day break the streak? | **No** — and it does not extend it either. The number means "days I completed". |
| Does a background-only day count? | No — it is empty, exactly like an unplanned one. |
| Optional tasks? | Excluded, same as background. **Not in the ask** — added because `BasePlannerTask.IsOptional` already existed. |
| Grace days? | **None.** The three rules above are already lenient; grace on top would make the number unaccountable. If one is ever wanted it goes in `Walk`, not `Evaluate`. |
| Day boundary? | The user's own `User.Timezone`. The response returns `Today` + `Timezone` so the client can stop computing dates. |
| Retroactive edits? | Recomputed, including across the gap. Free, given the above. |

Two rules exist for the client's sake rather than the domain's, and both are load-bearing:

- **Today can never break the streak, only extend it.** An unfinished today is unfinished, not
  failed. Without this the chip reads 0 every morning and climbs back by evening.
- **`CurrentStreak` is the value to display, already zeroed when dead.** The client owns no
  "is it still alive" decision — that judgement depends on the skip and empty-day rules, which live
  here.

⚠ **The streak's denominator is narrower than the progress ring's.** The ring counts every
non-background task; the streak also drops optional and cancelled ones. A day can read 4/5 on the
ring and still be complete. Clients must read `IsTodayComplete` rather than comparing counts.

⚠ **It rides on `CalendarResponse.Streak` and is null on every calendar read except
`GetByDateCalendarEndpoint`**, which fills it in after the projection — `Projection` runs per row and
cannot walk days. Nothing breaks if that line is dropped; the field just goes null and the chip
sticks at zero, which is why `PlannerStreakTests` asserts it is non-null on the plan response.

⚠ **`PlannerStreakReader` owns the qualifying-task predicate, `PlannerStreakService` owns the rules.**
Keep it that way — a second place deciding what a qualifying task is, is how the server and the
client end up disagreeing about the same number. The read is deliberately **uncapped**: a lookback
window would turn `BestStreak` into a rolling maximum, so a record the user really set could quietly
shrink.

## Gotchas

- **Slice code takes a plain `DbContext`**, never `AppDbContext`. There is no `dbContext.PlannerTasks`
  here — it is `dbContext.Set<PlannerTask>()`. The host aliases `DbContext` → `AppDbContext` in
  `ModuleServiceExtensions`.
- **`TaskPriority` is not ours** — it went to TodoLists, despite its configuration having lived in the
  Planning folder. `TaskImportance` *is* ours.
- **Google Calendar stays host-side, deliberately.** `GoogleCalendarService`, `ConnectGoogleCalendarEndpoint`,
  `GetGoogleCalendarAuthUrlEndpoint` and `SyncCalendarToGoogleEndpoint` remain in `AdhdTimeOrganizer`
  and reference the `Calendar` entity here — host → slice, which is fine. Do not pull them in "to keep
  calendar together": they carry a `Google.Apis.Auth` dependency that must not spread.
- **`PlannerTaskSeeder` stayed host-side**, alone among the planning seeders. It is a dev fixture that
  links a seeded planner task to a seeded `TodoListItem`, which is the one thing in the whole slice
  that genuinely needs another slice's rows. Moving it would have re-imposed the TodoLists reference to
  buy nothing but tidiness. Its `Order` (420) is still in Planning's band.
- **`CalendarSeeder` does not subclass `BasePerUserDefaultSeeder`** — its key is a date range rather
  than a row set, so it hand-rolls `SetupDefaults` / `ResetDefaults` and does its own
  `IgnoreQueryFilters()`. Deliberate; do not normalise it.
- **The completion fan-out runs through events and must stay that way.** `PlannerTask` and
  `TodoListItem` complete each other through `PlannerTaskIsDoneChangedEvent` /
  `TodoListItemIsDoneChangedEvent`, whose records live in Core. The handlers stay host-side. Never
  replace an event with a direct call — that is a second route to the same coupling the FK change just
  removed.
- **Two unrelated "Reminders" exist.** `framework/Sydowwe.Reminders` is an opt-in framework module in
  the git submodule with its own `ReminderDefinition`, ledgers and retention job. This slice's
  `Reminder` talks to it **only** through `Sydowwe.Framework.Contracts` (`IReminderRegistry`,
  `IQuietHoursReader`, the payload records). Never reference `Sydowwe.Reminders` directly, and never
  merge the two.
- **Keep the `Reminder` → `PlannerTask` cascade.** It is why the delete endpoints read reminder ids
  *before* deleting the task and cancel through the registry afterwards: the module-side
  `ReminderDefinition` is not reached by that cascade.

## Host wiring

Four places, none of which break the build if you miss them:

1. `Program.cs` → FastEndpoints `o.Assemblies` — missing means every route 404s.
2. `config/dependencyInjection/ModuleServiceExtensions.cs` → `ModuleAssemblies` — and **not** also in
   the `AddDependencyInjection` sweep, which `Except`s this list.
3. `AppDbContext.ApplyHostConfigurations` → one `ApplyConfigurationsFromAssembly` call.
4. `AdhdTimeOrganizer.sln`.

⚠ **The host's own configuration scan is anchored on `AppDbContext`, not on a configuration class.**
It used to say `typeof(PlannerTaskConfiguration).Assembly` — which was the host assembly until this
extraction moved that type here, at which point the line silently became a second Planning scan and
every remaining host configuration (three tracking entities, two tracker mappings, the module User
FKs) fell out of the model. Nothing failed to build; the next `migrations add` produced ~500 lines of
table renames and dropped partition keys. `AppDbContext` cannot leave the host, so the anchor cannot
repeat it.
