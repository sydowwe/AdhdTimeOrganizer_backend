# AdhdTimeOrganizer.Planning — Agent Summary

**Purpose:** planning a day — the `Calendar` row, the four planner-task shapes that hang off it
(one-off, repeating, template line, suggestion), day templates and the four ways of applying one,
per-user task importance and planner settings — **and** reminders, which are part of this slice
rather than a project of their own.

## Bounded context

Owns: `Calendar`, `BasePlannerTask` and its three concrete shapes (`PlannerTask`,
`RepeatingPlannerTask`, `TemplatePlannerTask`), `TaskPlannerDayTemplate`, `TaskImportance`,
`UserPlannerSettings`, `Reminder`, the three `PlannerSuggestionFrom*` read-models and FO11's
`PlannerSuggestionDismissal`, their EF
configurations, ~62 endpoints, their DTOs and 16 validator files, the domain services
(`PlannerStreakService`, `PlanVsActualTrendService`, `SkipSummaryService`, `CalendarDayFactory`,
`HolidayCalendar`, `PlannerTaskPostponeRule`, `RepeatingPlannerTaskSchedule`, FO11's `SuggestionPatternWindow`,
and FO4's `PlannerTaskMissedRule` + `DayPacking`), the
six application services (`CalendarProvisioner`, FO10's `CalendarSleepWindowSync`, `PlannerStreakReader`,
`PlannedBlockAutoReminders`, `ReminderRegistrationService`, `RepeatingTaskMaterialiser`), the two Core seam implementations, the two cross-slice event
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
- **Three `IEventHandler`s live here**, in `application/eventHandler/`:
  `RoutineTodoListIsDoneChangedEventHandler` and `TodoListItemIsDoneChangedEventHandler`, whose
  publishers are Routines and TodoLists but every row they touch is a `PlannerTask`, so the reaction
  belongs to the slice that owns it; and `AwayDayOccurrenceWithdrawalHandler` (FO6), which reacts to
  Planning's own `CalendarDayTypesChangedEvent` beside Routines' handler of the same event. Both event records are declared in Core, so neither slice
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

- **Every path that creates a `PlannerTask` must hand the new ids to `IPlannedBlockAutoReminders`
  after its save** (audit N1, P1-03). That is what gives a new block its automatic reminder under
  `UserPlannerSettings.AutoReminderMode` (default `ImportantOnly`, importance ≥
  `AutoReminderMinImportance` = Critical): an ordinary `Reminder` row with `IsAuto = true`, registered
  as its own kind `PlannedBlockStarting` (mutable separately, held while the user is away). Today's
  callers: `CreatePlannerTaskEndpoint`, both template applies and the materialiser (read
  `PlannerTemplateApplier.StagedTaskAdditions` before the save, take the ids after — carved segments
  are new blocks too), and `CopyToDaysPlannerTaskEndpoint`. A path that forgets answers 201 and the
  block silently never rings. A move or status change must **not** call it: created-once is what lets
  a user delete an automatic reminder for good. Everything after creation — moves, status, deletes —
  is the existing `SyncForPlannerTasksAsync` / `CancelManyAsync` story, and
  `PlannedBlockStartingGuard` retires at fire time what a path missed (gone, started, moved).
  `SyncCalendarToGoogleEndpoint` (host) turns Google's own popups off for any block with a portal
  reminder, or the phone rings twice. Pinned by `PlannedBlockAutoReminderTests`.

- **`ICalendarProvisioner.EnsureForDateAsync` commits.** Call it *before* staging your own writes —
  anything already pending on the ambient `DbContext` goes in with the calendar. All three callers
  resolve up front, which is why the two CRUD ones do it in `BeforeMapping` and not `AfterMapping`.
  It also **scopes by hand** (`IgnoreQueryFilters()` + explicit `UserId`), same as `CalendarSeeder`
  and for the same reason: it is told which user to act for, and filtered it would read no row and
  try to insert a duplicate onto the unique `(user_id, date)`.

- **A day's sleep window is inherited unless the day says otherwise (FO10).** New days start with the user's
  `UserSleepWindow`, and a changed default is pushed onto existing days from today on by
  `CalendarSleepWindowSync` — except days with `HasCustomSleepWindow`. Write a day's window through
  `Calendar.MarkSleepWindow`, never by assigning `WakeUpTime` / `BedTime` directly: an assignment that skips
  the flag leaves the user's chosen window to be overwritten by their next change of default, with no error.
  Details in `domain-map.md` § *The sleep window*.

- **`GET /calendar/day-plan/{date}` and `GET /calendar/by-Date/{date}` write (FO6).** For the user's today and
  tomorrow each first runs
  `RepeatingTaskMaterialiser`, which puts every active `AutoPlace` rule's occurrence on the plan
  (`PlannerTask.SourceRepeatingTaskId`). The rule's create/update run it too, after their own save. What keeps it
  honest:
  - **`RepeatingPlannerTask.MaterialisedThrough` is the "never twice" record**, not the `planner_task` rows — a
    deleted occurrence takes its own evidence with it. Every day the materialiser looks at is decided (placed,
    already there, no match, away day, dropped by the conflict mode) and the watermark passes it.
  - **No unique `(source_repeating_task_id, calendar_id)`** on purpose: it would 409 a user's PUT that moves an
    occurrence onto a day already holding one. The materialiser dedupes per day instead, and the rule's
    `row_version` serialises two racing reads.
  - **Conflicts go through `PlannerTemplateApplier.StageAgainstDay`** with the user's `DefaultConflictResolution`,
    **made non-destructive**: `Overwrite` is read as `Ignore` and `MergeOverwrite` as `MergeIgnore`, so a GET never
    deletes or carves a block the user placed — the occurrence is skipped or carved instead.
  - **A day marked away after placement loses its untouched occurrence** (today on). `AwayDayOccurrenceWithdrawalHandler`
    takes `CalendarDayTypesChangedEvent` (Planning publishes it from both day-type writes) and calls
    `WithdrawFromAwayDaysAsync`; the day stays decided, so un-marking it brings nothing back.
  - **A rule edit reconciles** (`ReconcileAfterEditAsync`): *untouched* occurrences from today on (still
    `NotStarted`, no original slot, still the old rule's exact plan, no to-do/Google link and no reminder the
    *user* wrote — an automatic one does not count) follow the edit, or are removed when the rule stops placing
    there; a decided day the edit newly matches gets its block.
  - **It never fails its caller** and clears the tracker on failure; it **commits**, so the same ordering rule as
    `EnsureForDateAsync` applies.
  - **Day matching is `RepeatingPlannerTaskSchedule.FallsOn`**, shared with the suggestion endpoint, so the
    panel never offers a rule on a day the plan would not receive it.
  - **It also runs with nobody signed in**: `MaterialiseRepeatingTasksJobHandler` (every 15 min, zone-aware) calls
    it once per user per local day, after their midnight, so a user who never opens the app still gets the
    block — and its automatic reminder. Every row it writes carries an explicit `UserId`; in that job nothing
    could stamp one, and an insert relying on it dies on the FK. Away users (`UserPresence.IsAbsent`) are
    **skipped, not gated**, so their days stay undecided and their first read after the return places them.

- **FO4's re-plan never trusts a client placement — it never receives one.** `POST /calendar/day-plan/{date}/replan`
  previews (writes no task), `…/replan/apply` takes back only the preview's `previewToken` plus a decision per
  `wontFit` block, and **recomputes** the plan from the token's packed-from against the day as it now stands; a
  different token (the day changed, or it was already applied) or one older than ten minutes is a 409 `Conflict` with
  nothing written. `GET …/next-free-slot` is the same rule for one block. Details in `domain-map.md`
  § *Re-planning the rest of today*. What keeps it honest:
  - **One "missed"** — `PlannerTaskMissedRule` (`NotStarted`, foreground, end passed on the user's clock). Derived,
    never stored; N5's nudge (`PlannerSlipNudgeJobHandler`) reads it rather than re-deriving it.
  - **One placement rule** — `DayPacking`, pure, behind all three routes via `DayReplanPlanner`. Background blocks
    neither move nor hold time; `InProgress` / `OnHold` / `Completed` / unavoidable blocks hold their slots.
  - **"Now" is the injected `TimeProvider`**, and the apply feeds the same instant to `PlannerTaskMoveRecorder`'s
    `utcNow` overload, so every move is judged against one clock — and the tests can fix it.
  - **Bed time 00:00 (or after midnight) means 23:59**, the last end a block can have. FO10 changed only the
    input (the day's bed time now starts as the user's default), not this rule.

- **At most two blocks of a kind at any minute (planner lanes, 2026-10-13).** The SPA draws two overlapping
  foreground blocks side by side, and separately two background ones; `PlannerTaskLaneRule` refuses a third with a
  409 `Conflict` on `startTime`. It is checked on **create, the PUT and the span PATCH only**, through
  `PlannerTaskLaneExtensions.EnsureWithinLanesAsync` in `AfterMapping`. It judges only a write that *moves* a block
  (new, or a changed day / start / end / kind), so a day already holding three still accepts a rename or a status
  tick. The bulk paths (both template applies, copy-to-days, FO4's re-plan apply, FO6's materialiser, carry-over)
  are deliberately unchecked; the SPA draws a third block as a marked clash. A new single-task write that moves a
  block should call it too. Pinned by `PlannerTaskLaneRuleTests`.

- **The slip nudges (audit N5) are a sweep that only notifies.** `PlannerSlipNudgeJobHandler`, every five minutes,
  sends `PlannedBlocksMissed` (today's missed blocks — one notification per user per sweep, at most two a day, none on
  an away day or for a block created after its end) and `PlannedBlockOverrun` (a started, non-auto-complete block 5+
  minutes past its end — held while the user has a timer running, through Core's `IRunningTimerLookup`, implemented by
  History). Its marks, `MissedNotifiedAt` / `OverrunNotifiedAt`, cover **one slot**: a block moved or extended later
  can be announced again, and the daily cap counts the distinct missed stamps on the day. A write path needs to do
  nothing for it. Details in `domain-map.md` § *Slip nudges*.
- **The block pushes have buttons (P2-03).** `PlannerTaskNotificationActionHandler` carries out Start · Skip on
  `PlannedBlockStarting` and Done · +15 min on `PlannedBlockOverrun`, tapped on the lock screen, with the times on
  the user's clock. Each applies from one status and is a no-op from the one it leads to; anything else is `Gone`
  (the app opens). +N is from *now*, capped at 23:59, and only while the end has passed, so a retry adds nothing.
  The status side effects (reminder sync, done-changed event) live in `IPlannerTaskStatusEffects`, shared with
  `PatchPlannerTaskStatusEndpoint` — a new status write path should call it too.

- **Pattern suggestions are recent-only, and the numbers live twice (FO11).** `SuggestionPatternWindow` holds
  the windows (weekday: 3+ sittings in 12 weeks, one in the last 4; day of month: 3+ in 12 months, one in the
  last 3) and the dismissal lapse rule; both host-side `mv_*_pattern.sql` scripts repeat them literally, because
  a view takes no parameters. Change both. The window moves with `current_date` only when a view refreshes,
  which the host's refresh queue forces once a day. A dismissal (`PlannerSuggestionDismissal`) hides a pattern
  from both pattern tiers until it lapses — never a user-set recurrence.

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
  one, capture the reminder ids first. If it **moves** an existing task to a later day or time, call
  `PlannerTaskMoveRecorder.RecordPostponementAsync` before the save (FO3 — the PUT, the span patch and FO4's
  re-plan apply do); a path that skips it builds and answers 200, and its slips simply vanish from the to-do help.
  If it needs "is this block missed?" or "where is the next free slot?", call `PlannerTaskMissedRule` /
  `DayPacking` (through `DayReplanPlanner`) rather than writing a second copy.
- **Add a conflict-resolution mode:** it goes in `PlannerTemplateApplier.StageAgainstDay` and nowhere else —
  the single-day and the batched apply route through it (via `Stage`), and so does FO6's materialiser, and `StagedTaskDeletions` reads the
  ChangeTracker so a new mode's deletions are captured without threading a list through.
- **Add a per-user default:** subclass `BasePerUserDefaultSeeder<T>`, pick an `Order` in 400–499, and
  make `Collides` match the entity's **unique index**, not its display text.
- **Add a user-scoped entity:** it will fail `UserDataExportAndDeletionTests` until it is listed as
  exported or as a deliberate gap. That is the test working.

## Deeper reference

- `domain-map.md` — model, invariants, the derived-number rules, glossary, navigation index
- `testing.md` — test strategy, the load-bearing guards, and the known gaps
- `README.md` — business intro and module-specific setup
