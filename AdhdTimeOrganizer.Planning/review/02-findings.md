# AdhdTimeOrganizer.Planning — Review 02: Findings

> Point-in-time snapshot, 2026-08-31. Severity: 🔴 blocker (legal / security / data corruption) ·
> 🟠 important (wrong behaviour or a maintainability hole) · 🟡 polish.
>
> **Numbering note.** `CQ-14` appears in a test display name
> (`PlannerTaskStatusAndSpanTests.CreatingAnOvernightTask_IsAcceptedWithNegativeDuration`). That id
> belongs to the earlier **portal-wide** review under `review/portal/`, not to this file. The `CQ-n`
> ids below are this slice's own and start at 1.

## Previously known — not re-flagged

Recorded so nobody re-raises them, and so file 03 can rank around them.

| Item | Where it is pinned | Status |
|---|---|---|
| `PlannerTaskValidator` accepts `EndTime <= StartTime` (overnight tasks) | portal review `CQ-14`; green probe test asserts current behaviour and names the assertion to flip | Open, deliberate. A **new consequence** is `CQ-7` below |
| `Reminder`, `RepeatingPlannerTask`, `TemplatePlannerTask`, `TaskImportance`, `UserPlannerSettings`, `PlannerSuggestionFromDayTemplate` absent from the user data export | `UserDataExportAndDeletionTests.KnownGaps` | Open, classified |
| `GetSuggestionsTaskPlannerDayTemplateEndpoint` has no test | `docs/testing.md` known-gap list | Open → `TEST-13` |
| `CalendarProvisioner`'s `DbUpdateException` race branch not exercised concurrently | `docs/testing.md` known-gap list | Open → `TEST-18` |
| `PlannerBatchLimits` ceilings unpinned at their boundary | `docs/testing.md` known-gap list | Open → `TEST-3` |
| `Calendar.Location` / `Weather` / `Notes` have no behavioural coverage | `docs/testing.md` known-gap list | Open, accepted |
| `RepeatingPlannerTask` has no materialisation path | `docs/testing.md` known-gap list | Open, feature not built |
| Case-sensitive `Contains` in grid text filters | Core review, `GridFilterCaseSensitivity.md` | Open, portal-wide family; `GridTaskImportanceEndpoint` is another instance |

---

## 5. Security & authorization

### `SEC-1` 🟠 Foreign-key ids are accepted from the request without an ownership check

`application/dto/request/taskPlanner/PlannerTaskRequest.cs:61-65` and `:75-82` ·
`application/validator/PlannerTaskValidator.cs:12-33` ·
`application/dto/request/taskPlanner/UserPlannerSettingsRequest.cs:45`

`PlannerTaskRequest` writes `ActivityId`, `ImportanceId` and `TodolistId` straight onto the entity. The
validator only checks each is `> 0`. `UserPlannerSettingsRequest` does the same for
`DefaultApplyTemplateId`. The database FK proves the row *exists*; nothing proves it belongs to the
caller. `CreateReminderEndpoint`/`UpdateReminderEndpoint` show the pattern that should be used here —
they check `PlannerTaskId` with an `AnyAsync` through the user-scoped set, and
`ReminderRegistrationTests.AttachingToAnotherUsersTask_IsRefused` pins it.

**Why it matters.** Today the blast radius is contained but real:

- A planner task can be filed against another user's `Activity`. `PlannerTask.Color =>
  Activity.Role.Color` and `PlannerTaskResponse` project through that navigation, so the victim's role
  **colour, name and category are served to the attacker** on every read of their own day.
- A planner task can be linked to another user's `TodoListItem`. The cross-user *write* does not fire
  today only because `TodoListItemIsDoneChangedEventHandler` runs under an HTTP scope where
  `ScopeUserId` resolves to the publisher — see `SEC-2`, which is the other half of this.
- `DefaultApplyTemplateId` can point at another user's template. `UserPlannerSettingsResponse` returns
  the bare id and no name, so this one leaks nothing — it is an integrity defect only.

**Failure scenario.** User A `POST /api/planner-task` with `ActivityId` = a value belonging to user B.
The FK passes, the row is created, and `GET /api/calendar/day-plan/{date}` returns A's task carrying
B's activity name and role colour.

**Fix.** Add an ownership probe in `AfterMapping` on `CreatePlannerTaskEndpoint` /
`UpdatePlannerTaskEndpoint` / `UpdatePlannerSettingsEndpoint`, copying `CreateReminderEndpoint`'s shape
(`AnyAsync` through the filtered set is both the existence *and* the ownership check). Do the same for
the tasks built by `PlannerTemplateApplier.BuildTasks`, whose `ActivityId`s also come off the wire.

`Doc impact: domain-map.md → Invariants (add a row: "a planner task's Activity / Importance / TodoListItem belongs to the same user — App only, per write endpoint")`

### `SEC-2` 🟠 `TodoListItemIsDoneChangedEventHandler` has no `UserId` predicate; its sibling does

`application/eventHandler/TodoListItemIsDoneChangedEventHandler.cs:36-39` ·
compare `application/eventHandler/RoutineTodoListIsDoneChangedEventHandler.cs:34`

```csharp
.Where(pt => pt.TodolistItemId == eventModel.TodoListItemId && pt.Calendar.Date == today)
```

No user predicate. The Routines handler on the next file over writes
`pt.ActivityId == eventModel.ActivityId && pt.UserId == eventModel.UserId && …`. The asymmetry looks
accidental rather than reasoned — the file's own comment justifies the *join column*, not the absence
of scoping.

**Why it matters.** The handler resolves its `DbContext` from a **fresh scope**
(`scopeFactory.CreateScope()`), and the global filter is
`context.ScopeUserId == null || e.UserId == context.ScopeUserId`
(`framework/Sydowwe.Framework/infrastructure/persistence/UserDbContextExtensions.cs:128-130`), with
`ScopeUserId => loggedUserService.IsAuthenticated ? … : null`
(`framework/Sydowwe.Framework/infrastructure/BaseDbContext.cs:33`). Under HTTP dispatch the ambient
`HttpContext` flows into the new scope, so the filter binds to the publisher and the query is safe
**today**. The moment this event is published from anywhere without an authenticated principal — a
scheduled job, a console seeder, a future background fan-out — `ScopeUserId` is null, the filter
becomes a no-op, and the handler flips **every user's** planner task linked to that item id. Combined
with `SEC-1` (which lets an attacker point their task at a victim's item id), that is a cross-user
write.

**Fix.** Add `&& pt.UserId == eventModel.UserId` to the predicate. `TodoListItemIsDoneChangedEvent`
already carries the id used by `PlannerTaskIsDoneChangedEventHandler`'s equivalent guard, so this is a
one-line change plus the assertion in `TEST-21`'s prompt.

`Doc impact: none`

### `SEC-3` 🟠 `GET /planner/settings` is a get-or-create with no uniqueness backstop

`application/endpoint/plannerSettings/GetPlannerSettingsEndpoint.cs:26-33` ·
`infrastructure/persistence/configuration/activityPlanning/UserPlannerSettingsConfiguration.cs:13`

`IsOneWithOneUser<UserPlannerSettings>()` declares a **non-unique** `HasIndex(r => r.UserId)` before
declaring the one-to-one FK
(`framework/…/configuration/extensions/EntityWithUserBuilderExtensions.cs:31-35`), so the explicit
index is what backs the FK and there is no unique constraint on `user_id`. `docs/domain-map.md` already
concedes this — *"one row per user… no unique constraint of its own beyond the 1:1 mapping"* — and
`docs/testing.md` calls a second row *"invisible until two devices read different settings"*.

**Failure scenario.** A phone and a desktop both hit `GET /api/planner/settings` for a user with no row.
Both `FirstOrDefaultAsync` return null; both insert; both commit. The user now has two settings rows.
Every later read takes whichever `FirstOrDefaultAsync` happens to return, so
`ReminderMinutesBefore` and `DefaultConflictResolution` become non-deterministic per request — and
`ReminderRegistrationService.ApplyUserDefaultsAsync` reads the same table, so the lead-offset prefill
starts flip-flopping between two values with nothing in the logs.

The existing pins (`RepeatedGet_CreatesExactlyOneRow`, `GetThenPut_StillOnlyOneRow`) are sequential and
cannot reach this.

**Fix.** Make the index unique (`builder.HasIndex(s => s.UserId).IsUnique()` in
`UserPlannerSettingsConfiguration`, since the Framework helper deliberately does not), add the
migration, and catch `DbUpdateException` in the endpoint to re-read the winner — the same shape
`CalendarProvisioner.EnsureForDateAsync` already uses for `(user_id, date)`. See `MIG-1`: existing
duplicates must be collapsed before the index can be created.

`Doc impact: domain-map.md → Invariants ("One UserPlannerSettings row per user" currently reads "DB — 1:1 mapping"; after the fix it is a real unique index, before the fix it is App-only and racy)`

### `SEC-4` 🟡 Six custom endpoints omit `Roles()` and lean on a host-side fallback

`plannerTask/command/PatchPlannerTaskStatusEndpoint.cs:16-31` ·
`plannerTask/command/ApplyTemplatePlannerTaskEndpoint.cs:25-39` ·
`plannerSettings/GetPlannerSettingsEndpoint.cs:11-20` ·
`plannerSettings/UpdatePlannerSettingsEndpoint.cs:12-23` ·
`repeatingPlannerTask/query/GetSuggestionsRepeatingPlannerTaskEndpoint.cs:21-33` ·
`taskPlannerDayTemplate/query/GetSuggestionsTaskPlannerDayTemplateEndpoint.cs:17-29`

**This is not an authorization hole and should not be reported as one.** The host applies
`ep.Roles("User", "Admin", "Root")` to any endpoint that registered none
(`AdhdTimeOrganizer/Program.cs:575-576`), and `FrameworkRoles.Configure(PortalRoleCatalog.Create(),
RoleTier.User)` at `Program.cs:228` makes `GetDefaultRoles()` resolve to that same set. The six
endpoints above land on exactly the gate their nine siblings ask for explicitly. Two of them
(the suggestion endpoints) even have the blank line where the call used to be.

What it costs is a silent coupling: the slice's gate is decided by one line in a file the slice cannot
see, and a host that ever narrows or removes that configurator widens six Planning routes with no build
error and no failing test. Every other custom endpoint in the slice declares
`protected virtual string[] AllowedRoles() => this.GetDefaultRoles();` and calls `Roles(AllowedRoles())`.

**Fix.** Add the two lines to each of the six, for symmetry with the rest of the slice.

`Doc impact: none`

---

## 6. Code quality

### `CQ-1` 🟠 `RoutineTodoListIsDoneChangedEventHandler` sets `Status` directly instead of `ApplyStatus`

`application/eventHandler/RoutineTodoListIsDoneChangedEventHandler.cs:41`

```csharp
task.Status = eventModel.NewIsDone ? PlannerTaskStatus.Completed : PlannerTaskStatus.NotStarted;
```

`PlannerTask.ApplyStatus` exists precisely to be the shared reset, and its own XML doc names this
handler's sibling as a call site: *"the reset shared by every status-mutation call site
(PatchPlannerTaskStatusEndpoint, TodoListItemIsDoneChangedEventHandler)"*
(`domain/model/entity/activityPlanning/PlannerTask.cs:43-48`). `docs/testing.md` states the rule
outright: *"A call site that sets `Status` directly leaves stale actual times that the trend then
measures."* This is that call site.

**Failure scenario.** A user starts a planner task (`InProgress`, `ActualStartTime = 09:05`), then
un-ticks the routine to-do list for the same activity. The handler sets `Status = NotStarted` but
leaves `ActualStartTime = 09:05`. `GetPlanVsActualTrendPlannerTaskEndpoint` then hands
`PlanVsActualTrendService.Aggregate` a `NotStarted` row that still carries an actual time — it is
counted in `NeverHappenedCount` while the stale time sits in the database waiting for the next status
change to make it look measured.

**Fix.** `task.ApplyStatus(...)`.

`Doc impact: none`

### `CQ-2` 🟠 The same handler never re-syncs reminders

`application/eventHandler/RoutineTodoListIsDoneChangedEventHandler.cs:40-43`

It changes planner-task **status** and stops. `docs/summary.md` is explicit: *"Any write path that
moves a task's time or changes its status must call `IReminderRegistrationService.SyncForPlannerTasksAsync`,
or the reminder keeps firing at the old time with nothing in the logs."* `PatchPlannerTaskStatusEndpoint`
does it, `UpdatePlannerTaskEndpoint` does it, `PatchPlannerTaskSpanEndpoint` does it, and the sibling
`TodoListItemIsDoneChangedEventHandler` does it at line 57 with a comment saying so.

**Failure scenario.** A task has a reminder for 14:00. The user completes the routine to-do list at
13:30. The task goes `Completed`; the module's `ReminderDefinition` is untouched; at 14:00 the user is
nudged about work they finished half an hour ago. `SyncCoreAsync` would have cancelled it
(`ReminderRegistrationService.cs:81-84`).

**Fix.** Resolve `IReminderRegistrationService` from the same scope and call
`SyncForPlannerTasksAsync(changedTaskIds, ct)` after the save — copy lines 30 and 57 of the sibling.

`Doc impact: none`

### `CQ-3` 🟠 The same handler has no try/catch, so a Planning failure 500s a Routines endpoint

`application/eventHandler/RoutineTodoListIsDoneChangedEventHandler.cs:25-44`

CLAUDE.md lists log-and-swallow in cross-slice event handlers as one of this solution's load-bearing
silent-failure mechanisms, and the sibling handler wraps its whole body with the reasoning spelled out
(`TodoListItemIsDoneChangedEventHandler.cs:59-65`): the publisher awaits via
`PublishAsync(Mode.WaitForAll, …)` after its own commit, so anything thrown here surfaces as a 500 on
an endpoint that already succeeded.

**Failure scenario.** A concurrent `PATCH /planner-task/{id}/status` wins the `row_version` race. This
handler's `SaveChangesAsync` throws `DbUpdateConcurrencyException`. The user's routine toggle — which
committed successfully — returns 500, and the client shows the routine as un-toggled when it is not.

**Fix.** Wrap in the sibling's `catch (Exception ex) when (ex is not OperationCanceledException)` and
log.

`Doc impact: none`

### `CQ-4` 🟠 The same handler overwrites `Cancelled` tasks; its sibling deliberately does not

`application/eventHandler/RoutineTodoListIsDoneChangedEventHandler.cs:40-41` ·
compare `TodoListItemIsDoneChangedEventHandler.cs:44-46`

The sibling filters `t.Status != PlannerTaskStatus.Cancelled` with the comment *"A task the user
deliberately cancelled that day is left alone — the parent to-do toggle must not silently un-cancel or
re-complete it."* The Routines handler applies its status to every match, cancelled ones included.
`domain-map.md` records the rule for one handler and not the other, which reads as an intended
difference but is not: both are "a parent toggle fans out onto today's planner tasks", and the skip
rule is a product ruling about the user's cancellation, not about which slice published the event.

**Failure scenario.** The user cancels "Morning Exercise" for today with a skip reason, then completes
the routine list containing it. The task flips to `Completed`, the skip reason is orphaned on a
completed row, and the day's streak tally moves a task from the cancelled bucket into the completed one.

**Fix.** Apply the same `!= Cancelled` filter, or record in `domain-map.md` why the two differ.

`Doc impact: domain-map.md → Navigation index (the RoutineTodoListIsDoneChangedEventHandler row should say whether it skips Cancelled, as the sibling row does)`

### `CQ-5` 🟠 "Today" is UTC in three write paths and the user's own zone in both read paths

`application/eventHandler/RoutineTodoListIsDoneChangedEventHandler.cs:30` ·
`application/eventHandler/TodoListItemIsDoneChangedEventHandler.cs:34` ·
`application/endpoint/plannerTask/command/PatchPlannerTaskStatusEndpoint.cs:73`

All three compute `DateOnly.FromDateTime(DateTime.UtcNow)`. Meanwhile `PlannerStreakReader.cs:26-29`
and `GetPlanVsActualTrendPlannerTaskEndpoint.cs:61-62` both go out of their way to resolve the user's
`TimeZoneInfo` first, each with a comment explaining that a UTC boundary would misattribute a
Bratislava user's late evening. `ReminderRegistrationService.ComposeTaskInstant` composes in the user's
zone too. The three write paths are the odd ones out.

**Failure scenario.** A Bratislava user (UTC+2 in summer) ticks a to-do item at 01:15 local on the 12th.
UTC is 23:15 on the 11th. The fan-out looks for planner tasks on calendar date **11 August** and
silently updates yesterday's task while today's stays open. In `PatchPlannerTaskStatusEndpoint` the same
skew decides whether `PlannerTaskIsDoneChangedEvent` is published at all, so a task completed after
local midnight fans out to nothing.

**Fix.** Resolve the zone the way the two readers do (`IUserTimeZoneResolver` is already injected into
`GetPlanVsActualTrendPlannerTaskEndpoint`) and derive `today` from it. Note
`CompletionFanOutTests.TogglingTodoItem_OnlyFlipsTodaysPlannerTask` currently pins the UTC reading via
`DateOnly.FromDateTime(DateTime.UtcNow)` at line 58 — it must be updated in the same change.

`Doc impact: domain-map.md → Business rules (both handler rows, and the day-plan section, should state which "today" the fan-out uses — the streak section already states its own)`

### `CQ-6` 🟠 Carved task segments inherit execution state and the to-do link

`application/helper/PlannerTemplateApplier.cs:181-196`

`CloneTaskWithNewTimes` copies `Status`, `SourceTemplateTaskId` and `TodolistItemId` onto every segment,
and drops `ActualStartTime`, `ActualEndTime`, `SkipReason` and `GoogleEventId`. Both halves are wrong,
in opposite directions, and the codebase already knows it: `CopyToDaysPlannerTaskEndpoint.CopyOnto`
(`:113-127`) resets `Status` to `NotStarted` and carries no to-do link, with a class-doc paragraph
explaining that *"a copied `TodolistItemId` would point several tasks at one item and fan every
done-toggle across all of them"*. `MergeOverwrite` carving an existing task into two segments does
exactly that.

**Failure scenario.** A user has a 09:00–12:00 task linked to a to-do item and already `Completed` with
actual times 09:05–11:50. They apply a template with `MergeOverwrite` and a 10:00–11:00 line. The
original is deleted and re-added as 09:00–10:00 and 11:00–12:00, **both** `Completed`, **both** carrying
`TodolistItemId`, **neither** carrying actual times. The trend now sees two completed-but-unmeasurable
tasks where there was one measured one, and un-ticking the to-do item flips both segments at once.

**Fix.** Decide the semantics explicitly and state them in `domain-map.md` § *Applying a day template*.
The consistent reading, given `CopyOnto`: a carved segment is a *new* block, so it should reset `Status`
to `NotStarted` and carry no `TodolistItemId` — or, if preserving execution state is intended, carry the
actual times too and keep the link on at most one segment.

`Doc impact: domain-map.md → Business rules → Applying a day template (add a row to the four-mode table: what a carved segment inherits)`

### `CQ-7` 🟠 Carving an overnight task returns zero segments — a silent delete

`application/helper/PlannerTemplateApplier.cs:150-179` (with `application/helper/TaskPlannerHelper.cs:19`)

`CarveTaskAroundBlockers` walks `currentStart` forward and only emits a segment when
`currentStart < segmentEnd`. For a task whose `EndTime < StartTime` — which the create/update path
happily produces, the pre-existing `CQ-14` — every comparison reads on the wrong side of midnight and
the method returns an **empty list** while `MergeCarveExistingTasksAroundNew` has already staged the
original for removal (`:137-143`).

**Failure scenario.** An existing 23:00→01:00 task (created through `POST /api/planner-task`, accepted
today). The user applies a template with `MergeOverwrite` containing a 00:00→23:59 line.
`TasksOverlap` reports an overlap (`23:00 < 23:59 && 01:00 > 00:00`), the task is added to
`tasksToRemove`, `CarveTaskAroundBlockers` returns `[]` — `currentStart` jumps to 23:59, which is not
`< task.EndTime (01:00)` — and the task is deleted with nothing put back. Its reminders cascade away
too, and `StagedTaskDeletions` faithfully cancels them, so the loss is *consistent* and completely
silent. The response is 201.

This is a distinct consequence from the `CQ-14` probe, which only asserts that the row can be created
and that `TasksOverlap` misreads it — it never reaches the applier.

**Fix.** Ideally fix `CQ-14` (add the ordering rule to `PlannerTaskValidator`, matching
`PlannerTaskChangeSpanValidator`, and normalise existing rows — see `MIG-3`). Independently, make
`CarveTaskAroundBlockers` defensive: if it would return zero segments for a task it did not fully
overlap, keep the original rather than dropping it.

`Doc impact: domain-map.md → Invariants (see DOC-1 — the same line is wrong for a second reason)`

### `CQ-8` 🟠 The two `RepeatingPlannerTask` CRUD endpoints are the only ones that do not wire their validator

`application/endpoint/repeatingPlannerTask/command/CreateRepeatingPlannerTaskEndpoint.cs:3-4` ·
`application/endpoint/repeatingPlannerTask/command/UpdateRepeatingPlannerTaskEndpoint.cs:3-4`

Both are bare one-line subclasses with no `Configure()` override, so neither calls
`Validator<RepeatingPlannerTaskValidator>()`. Every other endpoint in the slice that has a validator
wires it explicitly — `CreateTaskImportanceEndpoint`, `CreateTemplatePlannerTaskEndpoint`,
`CreatePlannerTaskEndpoint`, `CreateReminderEndpoint`, `UpdateCalendarEndpoint`, and the rest.

FastEndpoints may bind `Validator<RepeatingPlannerTaskRequest>` by convention from the scanned
assemblies, in which case the 80 lines of recurrence-shape rules do run today. **Nothing in the test
suite settles which it is** — `PlanningCrudAuthMatrixTests` exercises only the Framework base
behaviour, and no test posts a malformed recurrence. So the slice's most intricate validator is either
dead code or untested code, and the two states are indistinguishable from here.

**Failure scenario (if unbound).** `POST /api/repeating-planner-task` with
`RecurrenceType: "DayOfWeek"` and `ScheduledDays: []` is accepted. The row is inert —
`GetSuggestionsRepeatingPlannerTaskEndpoint.cs:68` evaluates `ScheduledDays.Contains(...)` against an
empty collection and never matches — so the user's recurrence silently never suggests anything.

**Fix.** Add the two-line `Configure()` override to both, matching every sibling. Then add `TEST-11`,
which settles the question either way.

`Doc impact: none`

### `CQ-9` 🟠 `ApplyTemplatePlannerTaskEndpoint` collapses every failure to 400

`application/endpoint/plannerTask/command/ApplyTemplatePlannerTaskEndpoint.cs:92-97`

```csharp
var result = DbUtils.HandleException(ex, "Create");
AddError(result.ErrorMessage!);
await Send.ErrorsAsync(400, ct);
```

It computes `result.ErrorType` and then throws it away. `PatchPlannerTaskStatusEndpoint.cs:85-87`, three
files over, does the same call correctly:
`Send.ErrorsAsync(EndpointHelper.ToStatusCode(result.ErrorType), ct)`.

**Failure scenario.** Two devices apply templates to the same day. One loses the `row_version` race on
the calendar's stamp; `DbUtils.HandleException` classifies it as a concurrency conflict, and the client
receives **400 Bad Request** instead of 409. A client that retries on 409 and surfaces a validation
message on 400 shows "invalid request" for a transient conflict. A genuine 500 is also served as a 400.
Note the `Summary` block does not declare a 409 either, so the OpenAPI contract matches the bug rather
than the intent.

**Fix.** Use `EndpointHelper.ToStatusCode(result.ErrorType)` and add `s.Response(409, …)` to the summary.

`Doc impact: none`

### `CQ-10` 🟡 `PatchPlannerTaskStatusEndpoint` calls `.Update()` on an already-tracked entity

`application/endpoint/plannerTask/command/PatchPlannerTaskStatusEndpoint.cs:64`

The entity came from a tracking query at line 37. `dbContext.Set<PlannerTask>().Update(entity)` marks
every property `Modified`, so the `UPDATE` rewrites all ~15 columns instead of the two or three that
changed. Harmless today, but it defeats the change-tracker's own diff and — once the audit interceptor
is wired (CLAUDE.md: it exists but is not wired here) — it would record every column as changed on every
status patch. Deleting the line is sufficient; `SaveChangesAsync` picks up the modifications.

`Doc impact: none`

### `CQ-11` 🟡 Free-text `Tags` are comma-joined into one column

`infrastructure/persistence/configuration/activityPlanning/TaskPlannerDayTemplateConfiguration.cs:24-35`

`Tags` (`List<string>`, user-authored) round-trips through `string.Join(',', v)` /
`v.Split(',', …)`. A tag containing a comma is silently split into two on the next read, and there is no
validator rule on `Tags` at all (`TaskPlannerDayTemplateValidator` covers Name, Description, Icon and
the two times). `PredefinedSkipReasons` and `LeadOffsetsMinutes` — the same shape of data — are `jsonb`
and do not have this problem, with a comment in `ReminderConfiguration.cs:20-24` explaining why jsonb
is the right choice for exactly this.

`RepeatingPlannerTask.ScheduledDays` / `ScheduledForDayTypes` use the same conversion but are
constrained to enum names by `RepeatingPlannerTaskValidator` — *if* that validator runs (`CQ-8`).

**Fix.** Move `Tags` to `jsonb`, matching the two nearby columns. See `MIG-4`.

`Doc impact: none`

### `CQ-12` 🟡 The midnight-wrap window predicate exists twice, verbatim

`application/endpoint/plannerTask/query/GetFilteredSortedPlannerTask.cs:14-37` ·
`application/endpoint/templatePlannerTask/query/GetFilteredSortedTemplatePlannerTask.cs` (same block)

Eighteen lines of three-way overlap logic, duplicated character-for-character across two filter
endpoints over two different entities. `docs/summary.md` names this exact hazard — *"a second place
deciding any of these is how the two ends of a feature start disagreeing about the same number"* — for
three other rule/query splits, and this one slipped through. Neither copy has a test (`TEST-8`).

**Fix.** Lift it into `TaskPlannerHelper` as
`IQueryable<T> InTimeWindow<T>(this IQueryable<T>, TimeOnly from, TimeOnly until) where T : BasePlannerTask`.
Both call sites already type over `BasePlannerTask` properties only.

`Doc impact: domain-map.md → Navigation index (TaskPlannerHelper row gains the new extension)`

### `CQ-13` 🟡 Both suggestion endpoints dereference a materialized-view row's navigation without a null check

`application/endpoint/taskPlannerDayTemplate/query/GetSuggestionsTaskPlannerDayTemplateEndpoint.cs:51,58` ·
`application/endpoint/repeatingPlannerTask/query/GetSuggestionsRepeatingPlannerTaskEndpoint.cs:158,183`

The three `PlannerSuggestionFrom*` entities are keyless mappings over materialized views refreshed by a
**host-side job**, so the view lags the base tables by design.
`.Include(p => p.Template)` then `TaskPlannerDayTemplateResponse.Projection(new[] { p.Template }…)`
assumes the principal row still exists; `p.Activity.Role.Color` in the other endpoint assumes the same
for `Activity`.

**Failure scenario.** A user deletes a day template. Before the next refresh, they open a day and the
client calls `GET /api/task-planner-day-template/suggestions/{calendarId}`. `p.Template` is null and the
projection NREs — an unhandled 500 on a page load, from an action the user themselves just took.

**Fix.** `.Where(p => p.Template != null)` / `.Where(p => p.Activity != null)` before projecting, or an
inner join through the navigation. `TEST-13` pins it.

`Doc impact: domain-map.md → Suggestion read-models (state that a view row may outlive its principal until the next refresh, and that the endpoints must tolerate it)`

---

## 9. Performance hotspots

| Where | Issue | Impact | Mitigation |
|---|---|---|---|
| `CopyToDaysPlannerTaskEndpoint.cs:81` | One `AnyAsync` per target day inside the loop | 366 round trips at the ceiling, on top of 366 saves | `PERF-1`: resolve all target ids in one `Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToHashSetAsync()` before the loop; keep the per-day save |
| `GetFilteredSortedCalendar.cs:14` | `From`/`Until` range is **uncapped**, unlike its two siblings | A client asking for 1900→2100 scans the user's whole calendar table and runs two correlated subqueries per row | `PERF-2`: apply `PlannerBatchLimits.MaxSummaryRangeDays` via a `CalendarFilter` validator, matching `CalendarTaskSummariesValidator` |
| `CalendarResponse.Projection:62-63` | `TotalTasks` / `CompletedTasks` are two correlated subqueries per calendar row | 62 subqueries for a month grid | `PERF-2`: acceptable at 31 rows; becomes the cost driver once the range above is unbounded |
| `ReminderRegistrationService.SyncCoreAsync:94` | `SaveChangesAsync` inside the per-reminder loop of `SyncForPlannerTasksAsync` | One round trip per reminder whose instant moved, after the batch loading at `:170-190` deliberately avoided exactly this | `PERF-3`: mutate all reminders, then save once after the loop, then register |
| `GetSuggestionsRepeatingPlannerTaskEndpoint.cs:56-61` | Loads **every** active `RepeatingPlannerTask` with three `Include`s, filters recurrence in memory, then runs `Projection(new[]{task}.AsQueryable()).Single()` per match | Grows with the user's recurrence count; the per-row `AsQueryable()` projection allocates an expression tree per task | `PERF-4`: push the `DayOfWeek`/`DayOfMonth`/`DateRange` predicates into SQL where expressible, and project the matched set once instead of per row |
| `CalendarDayLookup.cs:15-18` | Materialises full tracked `Calendar` entities to read eight fields | Every History calendar-dashboard read pays full-row I/O plus change-tracker entries it never uses | `PERF-6`: add `.AsNoTracking()`; keep the in-memory `DayIndex` projection (the comment is right that it cannot go to SQL) but select the eight columns |

### Indexes that should exist

Nothing is missing on the hot paths — `calendar (user_id, date)` unique,
`planner_task (user_id, calendar_id, start_time)`, `reminder (user_id, remind_at)` and
`reminder (planner_task_id)` each cover the query that needs them, and the cross-slice
`todolist_item_id` FK gets its index from the host-side relationship declaration.

**`PERF-5` — three indexes that should *not* exist.** Each is a single low-cardinality column with no
user prefix, so no query in the slice can use it; every one is pure write amplification:

| Index | Declared at | Why it is dead |
|---|---|---|
| `planner_task (status)` | `PlannerTaskConfiguration.cs:45` | Five distinct values; every read filters by user and calendar first |
| `calendar (day_type)` | `CalendarConfiguration.cs:27` | Five distinct values; ditto |
| `task_importance (importance)` | `TaskImportanceConfiguration.cs:17` | Redundant with the unique `(user_id, importance)`, which already serves ordering within a user. Its comment says "for sorting performance" — the composite does that |

Dropping them is a cheap migration with no read-path risk (`MIG-5`).

---

## Code/doc drift

### `DOC-1` 🟠 The `EndTime > StartTime` invariant claims an enforcement that does not exist

`docs/domain-map.md:91` says:

> `EndTime > StartTime` on a planner task | **App only** — **every planner-task validator rejects it**.
> There is no CHECK constraint, and `TaskPlannerHelper.TasksOverlap` assumes it

Three of the five reject it (`PlannerTaskChangeSpanValidator:19`, `ApplyTemplateToTaskPlannerValidator:32`,
`ApplyTemplateToCalendarsBatchValidator:56`). **Three do not**: `PlannerTaskValidator` (the create *and*
update path for every ordinary planner task), `RepeatingPlannerTaskValidator`, and
`TemplatePlannerTaskValidator` — each validates the hour/minute range of both fields and stops.

The repo's own `CreatingAnOvernightTask_IsAcceptedWithNegativeDuration` proves the doc wrong, and its
body spells it out: *"`PlannerTaskValidator`, unlike `PlannerTaskChangeSpanValidator`, has no ordering
rule."* The doc was written after that test and got the direction backwards.

**Which side is wrong: both.** The code should gain the rule (that is the pre-existing `CQ-14`, and
`CQ-7` shows what it costs today). Until it does, the doc must state the actual split, because an agent
reading the invariant table will reasonably assume `TasksOverlap`'s precondition holds.

`Doc impact: domain-map.md → Invariants (rewrite the EndTime>StartTime row: name the three validators that enforce it and the three that do not, and note the CQ-14/CQ-7 consequence)`

### `DOC-2` 🟡 `Calendar` names a `Holiday` day type that the enum does not have

`domain/model/entity/Calendar.cs:12` — `public required DayType DayType { get; set; } // Workday, Weekend, Holiday, Vacation, SickDay, Special`

`AdhdTimeOrganizer.Core`'s `DayType` has five members and no `Holiday`. This is the *right* design —
`CalendarDayFactory.cs:35-37` documents that a holiday is a `Workday` row with a `HolidayName`, and
`RepeatingPlannerTaskValidator.ValidDayTypes` correctly lists the five real ones. Only the comment is
stale, and it is exactly the comment someone extending `RepeatingPlannerTask.ScheduledForDayTypes`
would read first.

**Fix the code comment**, not the enum.

`Doc impact: none (source comment only)`

### `DOC-3` 🟡 Two delete-behaviour tests are named for a contradiction that no longer exists

`AdhdTimeOrganizer.IntegrationTests/Endpoints/PlannerDeleteBehaviorTests.cs` —
`"FINDING: deleting a Calendar with tasks is Restricted (blocked), not Cascaded, contradicting docs/domain-map.md"`
and `"FINDING: deleting an Activity referenced by a PlannerTask cascades the task away, not a clean 409"`.

The rewritten `docs/domain-map.md:85` now states *"A calendar with tasks cannot be deleted | **DB** —
`planner_task.calendar_id` is `Restrict`"*, which is exactly what the test asserts. The display names
are archaeology from a superseded doc and will send the next reader looking for a contradiction that was
resolved.

**Fix the test names** (the assertions are correct and should not change).

`Doc impact: none (test naming only)`

### `DOC-4` 🟠 `DayPlanResponse.HasPlan` documents the pre-lazy-creation world

`application/dto/response/taskPlanner/DayPlanResponse.cs:52-53`:

> Calendar rows are bulk-seeded per user for whole years by `CalendarSeeder` — **they are not created
> lazily on first task.** So inside the seeded horizon every date has a row … and outside it no date
> does, no matter how much the user plans.

Both sentences are now false. `CalendarProvisioner.EnsureForDateAsync` creates the row on first task,
and `domain-map.md` § *Lazy day creation* is entirely about that. The **conclusion** the doc draws
(branch on `HasPlan`, not on calendar presence) is still right, and still worth keeping — for the
seeded-horizon reason, not the outside-it one.

**Fix the XML doc**; the property is fine.

`Doc impact: none (source XML doc only — domain-map.md already describes lazy creation correctly)`

### `DOC-5` 🟡 `testing.md` credits `CompletionFanOutTests` with covering a handler it never touches

`docs/testing.md:94` lists *"`CompletionFanOutTests` (the two `*IsDoneChangedEvent` handlers that live
in this project)"* among the load-bearing guards owned elsewhere. `CompletionFanOutTests`' own class
doc also names `RoutineTodoListIsDoneChangedEventHandler`. But of its nine test methods, four exercise
`PlannerTaskIsDoneChangedEventHandler` (host-side) and four exercise
`TodoListItemIsDoneChangedEventHandler`; **none** exercises the Routines one. That the four defects in
`CQ-1`…`CQ-4` all live in the untested handler and none in the tested one is not a coincidence.

**Fix both docs after `TEST-21` lands**; until then the claim is simply wrong.

`Doc impact: testing.md → The guards (correct the CompletionFanOutTests entry) and → Known gaps (add the uncovered handler until TEST-21 exists)`
