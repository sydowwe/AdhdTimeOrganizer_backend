# AdhdTimeOrganizer.Planning — summary

The fifth vertical slice. Everything about **planning a day**: the calendar, the four planner-task
types, day templates, task importance, per-user planner settings, the suggestion read-models — and
**reminders**, which are part of this slice rather than a project of their own.

Navigation index: [`domain-map.md`](domain-map.md). Read this file first; open the map when you need
to find a specific type.

## What is in here

| Area | Types | Endpoints |
|---|---|---|
| Calendar | `Calendar` | 5 (by-date, **day-plan**, by-id, filter-sort, update) |
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

The pre-split analysis recorded that `GetSuggestionsRepeatingPlannerTaskEndpoint` "reads
`ActivityHistory`", making History a prerequisite. It does not. It reads **`PlannerSuggestionFromActivityHistory`** (named
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

## The one-request day plan

`GET /calendar/day-plan/{date}` (`GetDayPlanCalendarEndpoint` → `DayPlanResponse`) returns the
calendar row, its planner tasks ordered by start time, and the streak. It exists because the home page
could not ask for a day's tasks in one hop: `PlannerTaskFilter` is keyed on `CalendarId`, and the only
source of that id was `by-Date`. The two calls were serialised **by contract**, and after the
dashboard-refresh work they are paid on every stale tab-return and every five-minute poll, not once
per navigation.

Nothing about the old pair changed. The day-planner view still filters by calendar id and time window,
which is the shape it genuinely needs; only home stops using it.

Three rulings are baked into the response, and each answers a question the client had been guessing at:

| Question | Answer |
|---|---|
| What does a date with no calendar return? | **200 with `Calendar: null`, `Tasks: []`** — never 404. `by-Date` keeps its 404; it is a lookup, this is a page load, and a rejected promise there renders a retry button over a day that is merely unplanned. |
| Is a calendar created lazily on first task? | **Now yes** — see [below](#lazy-day-creation). It was not: `CalendarSeeder` bulk-created rows for a fixed set of years and nothing else could make one. |
| Is the `From`/`Until` window meaningful for a whole-day read? | **No.** Home only ever passed 00:00–23:59, so this route has no window at all. |
| Can a task belong to a date but not a calendar? | **No.** `PlannerTask.CalendarId` is non-nullable, so the calendar id is an implementation detail the client never has to hold. |

⚠ **Calendar presence still does not mean "the user planned this day".** `CalendarSeeder` seeds whole
years per user, so inside the seeded window every date has a row whether or not it was ever touched.
A client branching on presence would show its empty state on almost no day it should, so
`DayPlanResponse.HasPlan` (`Tasks.Count > 0`) is what an empty state reads.

⚠ **The streak is hoisted to the top level here** (`DayPlanResponse.Streak`), and the copy nested on
`Calendar.Streak` is nulled out. It has to survive a null calendar — it is a fact about the user, not
about the day — and two copies would eventually be filled in by different code paths and disagree.

Guard: `Endpoints.DayPlanEndpointTests` (6 over HTTP). The status code for an unplanned day and the
user scoping are both silent failures — a 404 there does not throw, and a lost query filter renders
somebody else's day rather than erroring.

## Lazy day creation

**A planner task may name its day by `Date` instead of `CalendarId`, and the calendar row is created
if the user has none.** Exactly one of the two is required, on `PlannerTaskRequest` and on
`ApplyTemplateToTaskPlannerRequest`; sending both is a 400 rather than a precedence rule, because the
two can disagree and the loser is a task filed on a day nobody asked for.

The defect it fixes: `CalendarSeeder` filled a hard-coded `{ 2025, 2026 }` at user setup and there is
**no create-calendar endpoint** — only `UpdateCalendarEndpoint`. Past the last seeded year every date
resolved to no row, so the planner rendered an ordinary empty day that silently refused every task,
and nothing in the app could make the row it needed. No exception, no log line, an expiry date on the
product.

| Type | Role |
|---|---|
| `HolidayCalendar` (`domain/service/`) | The holiday tables + Computus, lifted out of `CalendarSeeder` |
| `CalendarDayFactory` (`domain/service/`) | What an unplanned day *is* — day type, holiday name, default sleep window |
| `ICalendarProvisioner` / `CalendarProvisioner` | Read-or-create for one (user, date) |

Both creators go through `CalendarDayFactory`, and that is the point of it: a day filled in a year
ahead and the same day created on its first task have to be the same day. The three callers of the
provisioner are `CreatePlannerTaskEndpoint`, `UpdatePlannerTaskEndpoint` (a task dragged onto an
unplanned day) and `ApplyTemplatePlannerTaskEndpoint` (by date; by id it still 404s, because an id
resolving to nothing is a stale client rather than an unplanned day).

⚠ **`EnsureForDateAsync` commits.** Call it *before* staging your own writes — anything already
pending on the ambient `DbContext` goes in with the calendar. All three callers resolve up front for
this reason, which is why the two CRUD ones do it in `BeforeMapping` and not `AfterMapping`.

⚠ **It scopes by hand** (`IgnoreQueryFilters()` + explicit `UserId`), same as `CalendarSeeder` and for
the same reason: it is told which user to act for, and filtered it would read no row and try to insert
a duplicate onto the unique `(user_id, date)`.

⚠ **`(user_id, date)` is unique, and the race is real** — a phone and a desktop starting the same
morning's first task. The loser catches `DbUpdateException`, **detaches** its failed insert (an
`Added` entity survives a failed `SaveChanges` and would be retried by the endpoint's own next save)
and reads the winner's row.

`CalendarSeeder`'s year list is now `{ this year, next year }`, resolved when it runs. That is
hygiene, not the fix — it only helps users created from now on. Lazy creation is what makes an
arbitrary future date plannable for everyone.

Guard: `Endpoints.LazyCalendarCreationTests` (9 over HTTP, on dates far outside any seeded window).
The metadata theory is the drift guard — if the seeder and the provisioner ever stop sharing
`CalendarDayFactory`, a lazily created Christmas quietly loses its holiday name while every "does a
row exist" assertion still passes.

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
  `IgnoreQueryFilters()`. Deliberate; do not normalise it. It is also **no longer the only thing that
  creates a calendar row** — see [lazy day creation](#lazy-day-creation) — and it no longer owns the
  holiday tables or the day defaults; those moved to `HolidayCalendar` / `CalendarDayFactory` so both
  creators share them.
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
