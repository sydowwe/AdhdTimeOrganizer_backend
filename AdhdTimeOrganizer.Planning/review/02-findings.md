# AdhdTimeOrganizer.Planning — Review 02: Findings

> Point-in-time snapshot, 2026-08-31. Severity: 🔴 blocker (legal / security / data corruption) ·
> 🟠 important (wrong behaviour or a maintainability hole) · 🟡 polish.
>
> **Numbering note.** `CQ-14` appears in a test display name
> (`PlannerTaskStatusAndSpanTests.CreatingAnOvernightTask_IsRejected`). That id belongs to the earlier
> **portal-wide** review under `review/portal/`, not to this file. The `CQ-n` ids below are this slice's
> own and start at 1. It is recorded here anyway because `CQ-7` and `DOC-1` are its consequences and all
> three were fixed together — see the `CQ-14` block in § 6.

## Previously known — not re-flagged

Recorded so nobody re-raises them, and so file 03 can rank around them.

| Item | Where it is pinned | Status |
|---|---|---|
| `PlannerTaskValidator` accepts `EndTime <= StartTime` (overnight tasks) | portal review `CQ-14`; `PlannerTaskStatusAndSpanTests.CreatingAnOvernightTask_IsRejected` | ✅ **Fixed 2026-09-08** — see the `CQ-14` block below. `CQ-7` and `DOC-1` were fixed with it; **`MIG-3` (normalising the rows that predate the rule) is deliberately still open** |
| `Reminder`, `RepeatingPlannerTask`, `TemplatePlannerTask`, `TaskImportance`, `UserPlannerSettings`, `PlannerSuggestionFromDayTemplate` absent from the user data export | `UserDataExportAndDeletionTests.KnownGaps` | Open, classified |
| `GetSuggestionsTaskPlannerDayTemplateEndpoint` has no test | `docs/testing.md` known-gap list | Open → `TEST-13` |
| `CalendarProvisioner`'s `DbUpdateException` race branch not exercised concurrently | `docs/testing.md` known-gap list | Open → `TEST-18` |
| `PlannerBatchLimits` ceilings unpinned at their boundary | `docs/testing.md` known-gap list | Open → `TEST-3` |
| `Calendar.Location` / `Weather` / `Notes` have no behavioural coverage | `docs/testing.md` known-gap list | Open, accepted |
| `RepeatingPlannerTask` has no materialisation path | `docs/testing.md` known-gap list | Open, feature not built |
| Case-sensitive `Contains` in grid text filters | Core review, `GridFilterCaseSensitivity.md` | Open, portal-wide family; `GridTaskImportanceEndpoint` is another instance |

---

## 5. Security & authorization

### `SEC-1` 🟠 ✅ FIXED 2026-09-08 · Foreign-key ids are accepted from the request without an ownership check

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

**Fixed 2026-09-08.** As prescribed, in a shared guard —
`AdhdTimeOrganizer.Planning/application/endpoint/extensions/PlannerReferencedIdOwnershipExtensions.cs`,
the Planning counterpart of `ActivityIdOwnershipExtensions` / `MappingTargetOwnershipExtensions`. Called from
`BeforeMapping` on `CreatePlannerTaskEndpoint` and `UpdatePlannerTaskEndpoint`, from the top of
`ApplyTemplatePlannerTaskEndpoint` and `ApplyTemplateBatchPlannerTaskEndpoint` (the `BuildTasks` path, checked
over the distinct ids of the whole task list rather than per line), and — for `DefaultApplyTemplateId` — from
`UpdatePlannerSettingsEndpoint`. 404 per offending field, same message for missing and foreign.

Two things went beyond the literal prescription, both forced by the code:

- `TodolistId` cannot be checked with an `AnyAsync` the way `CreateReminderEndpoint`'s `PlannerTaskId` can —
  Planning cannot see TodoLists and the column is navigation-free. It goes through Core's
  `IReferencedIdOwnershipService` over the `IUserOwnedReferenceVerifier` seam (key `todoListItem`, already
  implemented for History), which is why the seam README now lists Planning as a second consumer.
- The guard runs **before** `ICalendarProvisioner.EnsureForDateAsync`, which commits. Placed after it, a
  request on its way to a 404 would still leave a lazily-created calendar row behind.
  `CreatePlannerTask_WithForeignActivityIdByDate_ProvisionsNoCalendar` pins the ordering.

Pinned by `AdhdTimeOrganizer.IntegrationTests/Endpoints/PlannerReferencedIdOwnershipTests.cs` (10 tests),
each negative case with a control — the `TodolistId` control is also what catches the seam failing *closed*
for everyone.

**Not covered, deliberately:** `TemplatePlannerTaskRequest` and `RepeatingPlannerTaskRequest` inherit the same
`ActivityId` / `ImportanceId` from `BasePlannerTaskRequest` and are unguarded for the same reason. They are
outside this finding's fix list, and their consequence is narrower (neither feeds a read that projects through
`Activity.Role`), but they are the same hole and should be closed with the same helper.

`Doc impact: domain-map.md → Invariants (add a row: "a planner task's Activity / Importance / TodoListItem belongs to the same user — App only, per write endpoint") — done, plus a second row for DefaultApplyTemplateId and a Navigation-index row on the two handlers' scoping`

### `SEC-2` 🟠 ✅ FIXED 2026-09-08 · `TodoListItemIsDoneChangedEventHandler` has no `UserId` predicate; its sibling does

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

**Fixed 2026-09-08.** The predicate is as prescribed, but the finding is wrong about the cost:
`TodoListItemIsDoneChangedEvent` was `(long TodoListItemId, bool NewIsDone)` and did **not** carry a `UserId`
— that is `PlannerTaskIsDoneChangedEvent`, a different record. So the change is the record (now
`(long TodoListItemId, long UserId, bool NewIsDone)`, matching `RoutineTodoListIsDoneChangedEvent`'s shape),
both publishers (`ToggleIsDoneTodoListItemEndpoint`, `ToggleStepIsDoneTodoListItemEndpoint`, each passing the
item's own `UserId`), and the handler predicate. The handler's log line gained `{UserId}` for parity with the
routine sibling; both ids are non-PII.

Pinned by `TodoListItemFanOutUserScopingTests.TodoListItemEvent_CannotFlipAnotherUsersPlannerTask`, driven
against the handler from the unauthenticated factory's scope — that is the only way to reach the
`ScopeUserId == null` state the finding names, so the test exercises the real precondition rather than an
approximation of it. It asserts on both tasks: the publisher's own still flips (the control), the victim's
does not.

`Doc impact: none` — but `domain-map.md`'s navigation index gained a shared row stating that both handlers
scope on the event's `UserId` rather than the ambient filter, since that is now a rule the two hold jointly.

### `SEC-3` 🟠 ✅ FIXED 2026-09-08 · `GET /planner/settings` is a get-or-create that does not survive losing the race

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

**Fixed 2026-09-08 — and the finding's premise was half wrong, which changes what the fix is.** The
index *is* unique in the database and always has been: `20260819234301_Initial` created
`ix_user_planner_settings_user_id` with `unique: true`. EF's foreign-key index convention sees the 1:1
relationship the helper declares on its next line and marks the helper's own `HasIndex` unique — so the
non-unique call is real, and the uniqueness it ends up with is a convention nobody wrote down. **No
migration was needed and none was added** (the model snapshot already carried `.IsUnique()`).

Consequently two rows were never creatable and **`MIG-1` is moot** — there is nothing to collapse. What
*was* real is the other half: the loser of the race takes a unique violation, and neither endpoint caught
it, so a first-time `GET` — a page load — answered **500**. That is what the fix addresses:

- `UserPlannerSettingsConfiguration` now declares `HasIndex(s => s.UserId).IsUnique()` itself. Same index,
  no schema change; it stops the invariant resting on a convention that an edit to the relationship would
  silently drop.
- `GetPlannerSettingsEndpoint` catches the unique violation (`PostgresErrorCodes.UniqueViolation`,
  narrower than the bare `DbUpdateException` the finding suggested — a concurrency conflict is a different
  answer), clears the tracker so the rejected insert is not retried by a later save on the same request,
  and re-reads the winner's row.
- `UpdatePlannerSettingsEndpoint` was the fifth hand-written copy of the shape
  `BaseUserSettingsUpsertEndpoint` already exists for — the one `Sydowwe.Framework`'s own class doc counts
  as four — and now derives from it. It handles the same first-write race **and** the commoner one the
  finding does not mention: two writes to a row that already exists lose on `row_version`, which is a
  `DbUpdateConcurrencyException` a unique-violation catch would miss, and is now a 409 rather than a 500.
  The `DefaultApplyTemplateId` ownership check (`SEC-1`) moved to the base's `BeforeUpsertAsync`.

Pinned by `UserPlannerSettingsTests.ConcurrentFirstGet_BothSucceed_AndLeaveOneRow` and
`ConcurrentFirstPut_BothSucceed_AndLeaveOneRow` — raced rather than simulated, for the reason
`RoutineResetConcurrencyTests`' SEC-5 pair gives (no seeding can make the row absent at read time and
present at insert time), plus `UncontendedFirstGet_StillCreatesTheRow` as the control against the recovery
degrading into never inserting. Both race tests do reach the recovery branch: the run log carries the two
rejected `INSERT INTO planning.user_planner_settings` statements.

`Doc impact: domain-map.md → Invariants (now states the real unique index, that the helper's index is not what makes it unique, and that both endpoints recover) and testing.md → The guards — done`

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

> **One of the six left this list on 2026-09-08.** `UpdatePlannerSettingsEndpoint` now derives from
> `BaseUserSettingsUpsertEndpoint` (`SEC-3`), which calls `Roles(AllowedRoles())` itself with
> `GetUserRole()` — the same User + Admin + Root set the host fallback was applying. Five to go.

`Doc impact: none`

---

## 6. Code quality

> **`CQ-1`…`CQ-4` are all fixed (2026-09-08), together with `TEST-21` and `DOC-5`.** All four lived in
> `RoutineTodoListIsDoneChangedEventHandler`, and all four were the same defect in different clothes: the
> handler had drifted from its sibling `TodoListItemIsDoneChangedEventHandler`, which is the reference
> implementation for "a parent toggle fans out onto today's planner tasks". It now has the sibling's shape
> — `ApplyStatus`, the `Cancelled` skip, the reminder re-sync, the log-and-swallow wrapper — and
> `AdhdTimeOrganizer.IntegrationTests/Endpoints/RoutineCompletionFanOutTests.cs` (6 tests) pins each one.
> `CQ-5`, which also touches this handler, was fixed separately afterwards — see its own block below.

### `CQ-1` 🟠 ✅ FIXED 2026-09-08 · `RoutineTodoListIsDoneChangedEventHandler` sets `Status` directly instead of `ApplyStatus`

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

**Fixed 2026-09-08.** As prescribed. Pinned by
`RoutineCompletionFanOutTests.UntickingRoutineList_ClearsPlannerTaskActualTimes`, which was red before the
change. `PlannerTask.ApplyStatus`'s XML doc now names this handler in its call-site list too.

`Doc impact: none`

### `CQ-2` 🟠 ✅ FIXED 2026-09-08 · The same handler never re-syncs reminders

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

**Fixed 2026-09-08.** As prescribed, resolved from the same scope and called after the save with the ids
of the tasks that actually changed (so a day of only-cancelled tasks syncs nothing). Pinned by
`RoutineCompletionFanOutTests.TickingRoutineList_RetiresLinkedPlannerTaskReminder`, which asserts on the
module-side `ReminderDefinition` row rather than on the call.

`Doc impact: none`

### `CQ-3` 🟠 ✅ FIXED 2026-09-08 · The same handler has no try/catch, so a Planning failure 500s a Routines endpoint

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

**Fixed 2026-09-08.** As prescribed; the log message carries `{ActivityId}` and `{UserId}`, which are the
only two ids the event has and both non-PII. Pinned by
`RoutineCompletionFanOutTests.HandlerFailure_DoesNotFailTheRoutineToggle`, which throws from
`IReminderRegistrationService` rather than racing a real `row_version` conflict — see that file's class
doc for why the deterministic lever was preferred to the concurrency one the prompt suggested.

`Doc impact: none`

### `CQ-4` 🟠 ✅ FIXED 2026-09-08 · The same handler overwrites `Cancelled` tasks; its sibling deliberately does not

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

**Fixed 2026-09-08.** The first branch — the same filter, because the skip is a product ruling about the
user's cancellation and not about which slice published the event. `domain-map.md`'s navigation row now
states the rule and that the two handlers are deliberately identical. Pinned by
`RoutineCompletionFanOutTests.TickingRoutineList_LeavesCancelledPlannerTaskAlone`, which also seeds an
ordinary task alongside the cancelled one so the skip cannot regress into a whole-batch bail-out.

`Doc impact: domain-map.md → Navigation index — done`

### `CQ-5` 🟠 ✅ FIXED 2026-09-08 · "Today" is UTC in three write paths and the user's own zone in both read paths

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
`DateOnly.FromDateTime(DateTime.UtcNow)` at line 58 — it must be updated in the same change. **Since
2026-09-08 there is a second such pin**, `RoutineCompletionFanOutTests.TickingRoutineList_OnlyFlipsTodaysPlannerTask`,
added with `TEST-21`; both are commented as the UTC reading to flip, and this finding is otherwise
untouched by that work.

**Fixed 2026-09-08.** All three write paths now derive `today` from the user's zone. The conversion itself went
into `TaskPlannerHelper.TodayIn(TimeZoneInfo)` rather than being written out a third time — it is the same
expression the two readers had inline, and both were moved onto the helper too, so the slice now has exactly
one definition of "today" (the `CQ-12` lesson, applied before the drift rather than after it). The handlers
resolve `IUserTimeZoneResolver` from the same fresh scope they already resolve `DbContext` and
`IReminderRegistrationService` from, keyed on the **event's** `UserId`: that is the only id available under a
background dispatch, and it is the same id `SEC-2` scopes the query on, so the zone and the rows can never
belong to different users. `PatchPlannerTaskStatusEndpoint` takes the resolver as a fourth constructor
parameter and keys it on the loaded entity's `UserId`.

Three new pins, each moving the test user onto a zone that is **currently** on a different calendar date than
UTC (chosen at run time by `PlanningTestSeedHelper.UseZoneOffsetFromUtcTodayAsync` scanning the system zones —
no fixed zone is across midnight at every hour, so a hard-coded one would make the test a no-op for most of
the day):

- `CompletionFanOutTests.TogglingTodoItem_UsesTheUsersOwnDayBoundary` — the to-do handler, with a task on
  UTC-today linked to the same item as the control, so "flip both days" fails as loudly as "flip the wrong one";
- `CompletionFanOutTests.PatchingStatus_PublishesOnTheUsersToday` — the endpoint's publish gate, asserted on
  the linked to-do item rather than on the event, since the silence being fixed is the fan-out not happening;
- `RoutineCompletionFanOutTests.TickingRoutineList_UsesTheUsersOwnDayBoundary` — the routine handler, same shape.

The two existing "only today's task flips" pins keep their UTC-seeded user and were **not** rewritten: with the
fixture user on `TimeZoneInfo.Utc` both readings agree, so they still pin the single-day scoping and the new
tests own the zone question. Only their comments — which named the UTC reading as the thing to flip — were
corrected, along with the class-doc bullet in `RoutineCompletionFanOutTests` that recorded `CQ-5` as open.

`Doc impact: domain-map.md → Business rules — done, as a new § *Which "today" — the user's, everywhere* rather than a line per path: the rule is one rule and had already been restated three times. Both handler rows gained a shared navigation row, and the TaskPlannerHelper row names TodayIn. The day-plan section was left alone — that endpoint takes its date from the route and never asks what day it is`

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

### `CQ-14` 🟠 ✅ FIXED 2026-09-08 · Three of the six planner-task validators had no `StartTime < EndTime` rule

> Not this slice's id — see the numbering note at the top. Recorded here because `CQ-7` and `DOC-1` are
> its consequences and the three were fixed in one change.

`application/validator/PlannerTaskValidator.cs` · `RepeatingPlannerTaskValidator.cs` ·
`TemplatePlannerTaskValidator.cs`

**Fixed 2026-09-08.** All three now carry the rule `PlannerTaskChangeSpanValidator:19` has always had,
written with `TimeDtoRules.IsBefore` — minute-of-day arithmetic rather than `ToTimeOnly()`, because
FluentValidation runs every chain and a `ToTimeOnly()` on an out-of-range hour throws out of the
validator, turning the 400 it was about to produce into a 500. That is `TimeDtoRules`' own documented
reason for the extension existing.

The probe that pinned the old behaviour was flipped rather than deleted:
`PlannerTaskStatusAndSpanTests.CreatingAnOvernightTask_IsRejected` now asserts 400 on **create**, on
`EndTime == StartTime` (the rule is strictly-before), and on the **`PUT`** path — which the old probe did
not cover and which was the hole the span-patch endpoint's own rule could not close — plus that the
rejected update left the row untouched.

> ### ⚠️ `MIG-3` is deliberately **not** done — the rule guards the door, not the table
>
> There is **no CHECK constraint**, so every `planner_task` / `repeating_planner_task` /
> `template_planner_task` row created *before* this change can still carry `end_time <= start_time`, and
> such a row now cannot round-trip through **any** write path (create, update and patch-span all refuse
> it). The normalising data migration `MIG-3` prescribes — survey with
> `SELECT count(*) FROM planning.planner_task WHERE end_time <= start_time` across all three tables, then
> clamp — **is still outstanding and is being handled separately, after the rest of the findings are
> fixed.** Until it lands:
>
> - `TaskPlannerHelper.TasksOverlap` reads such a row as a same-day negative span and reports no overlap;
> - `PlannerTemplateApplier` screens for it (`IsCarvable`) and leaves it whole rather than deleting it —
>   that is the `CQ-7` fix below, and it is what makes the outstanding migration survivable;
> - `docs/domain-map.md`'s invariant row says exactly this rather than claiming enforcement (`DOC-1`).

`Doc impact: domain-map.md → Invariants — done (see DOC-1)`

### `CQ-7` 🟠 ✅ FIXED 2026-09-08 · Carving an overnight task returns zero segments — a silent delete

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

**Fixed 2026-09-08.** Both halves, since either alone leaves the other's failure reachable — `CQ-14`
closes the door, this closes the table. `PlannerTemplateApplier` gained `IsCarvable(task)` —
`task.StartTime < task.EndTime`, the precondition the whole walk assumes — and consults it in two places:
`CarveTaskAroundBlockers` returns `[task]` up front for a span it cannot reason about, and
`MergeCarveExistingTasksAroundNew` skips such a task **before** staging its removal. The order matters:
returning the original from the carve alone would have handed `RemoveRange`/`AddRange` the same tracked
instance, flipping it to `Added` and attempting an insert on an id that already exists. The new task is
added over the survivor instead — an overlap the day view can show is recoverable; a silent delete is not.

Pinned by `ApplyTemplatePlannerTaskConflictResolutionTests.MergeOverwrite_LeavesANegativeDurationTaskWhole_InsteadOfDeletingIt`
(`TEST-17`), which seeds the overnight row directly — `CQ-14` now rejects it at the endpoint — and carries
an ordinary 09:00→12:00 task through the same blocker as the control, so the guard cannot regress into
"never carve anything". It asserts on the **reminder rows**: the survivor's `ReminderDefinition` is still
`Active`, the consumed control's is `Cancelled`. That is where the silence lived — the old delete cancelled
its reminders perfectly correctly.

`Doc impact: domain-map.md → Invariants — done (see DOC-1)`

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

### `CQ-11` 🟡 ✅ FIXED 2026-09-08 · Free-text `Tags` are comma-joined into one column

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

**Fixed 2026-09-08.** As prescribed: `HasColumnType("jsonb").IsRequired()`, and the `HasConversion` **and** its
hand-written `ValueComparer` both deleted — Npgsql supplies the comparer for a jsonb-mapped `List<string>`, which
is why the two nearby columns never needed one either.

`ScheduledDays` on the same entity deliberately keeps its join, with a comment saying why: its elements are
`DayOfWeek` names, so no element can contain the delimiter. That is the property the free-text column never had,
and stating it is what stops the next reader "finishing the job" by converting a column that is already safe.

> **`MIG-4` is now required, not optional, and is not a plain `ALTER … TYPE jsonb`.** The model says jsonb; a
> migrated database still has `text`. Postgres cannot cast `productive,focused` to jsonb, so the migration needs
> `USING to_jsonb(string_to_array(tags, ','))` — which is also the only thing that preserves the existing rows'
> tags rather than erroring the deploy. The integration tests do not catch this: the fixture builds its schema
> with `EnsureCreated`, so they have never seen the `text` column.

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

**`PERF-5` ✅ FIXED 2026-09-08 — three indexes that should *not* exist.** Each is a single low-cardinality
column with no user prefix, so no query in the slice can use it; every one is pure write amplification:

| Index | Declared at | Why it is dead |
|---|---|---|
| `planner_task (status)` | `PlannerTaskConfiguration.cs:45` | Five distinct values; every read filters by user and calendar first |
| `calendar (day_type)` | `CalendarConfiguration.cs:27` | Five distinct values; ditto |
| `task_importance (importance)` | `TaskImportanceConfiguration.cs:17` | Redundant with the unique `(user_id, importance)`, which already serves ordering within a user. Its comment says "for sorting performance" — the composite does that |

Dropping them is a cheap migration with no read-path risk (`MIG-5`).

**Fixed 2026-09-08.** All three `HasIndex` calls removed. Each is **replaced in place by a comment** naming
what serves the reads instead — the user-prefixed composite in all three cases — rather than deleted silently:
a bare `HasIndex(Status)` is the single most re-addable line in a configuration file, and the deleted
`task_importance` one came with a comment ("Add index for sorting performance") that argued for itself.

Nothing needed a test: no query changes, and `PlanningCrudAuthMatrixTests` plus the filter/sort suites already
cover every read that touches these three columns. The full suite is green (2203 passed, 0 failed).

> **`MIG-5` is outstanding**, deliberately — same reason as `MIG-4` above. The model no longer declares the
> three indexes; a migrated database still has them, and until the migration lands they keep costing writes
> without the model knowing they exist. Unlike `MIG-4` this one is a pure `DROP INDEX` with no data step and
> no failure mode.
>
> One index of the same shape is **left alone on purpose**: `task_planner_day_template (suggested_for_day_type)`
> (`TaskPlannerDayTemplateConfiguration.cs:55`). It is not in `PERF-5`'s list, so it was not in this fix's
> scope — but it is the same single low-cardinality column with no user prefix, and it should be looked at.

---

## Code/doc drift

### `DOC-1` 🟠 ✅ FIXED 2026-09-08 · The `EndTime > StartTime` invariant claims an enforcement that does not exist

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

**Fixed 2026-09-08.** The code side went first (`CQ-14`), so the row no longer needs to name a split — all
six validators do enforce it now. What it states instead is the thing that is *still* true and was the
more dangerous half of the original claim: **there is no CHECK constraint**, so rows created before
2026-09-08 can still violate the invariant (`MIG-3`, outstanding), and it names the two places that cope
with one — `TasksOverlap` reading it as a same-day negative span, and
`PlannerTemplateApplier.IsCarvable` refusing to carve it. `TaskPlannerHelper`'s own source comment made
the same false claim one line above `TasksOverlap`, where an agent is far likelier to read it than the
doc, and was corrected to match.

`Doc impact: domain-map.md → Invariants, plus the TaskPlannerHelper.TasksOverlap source comment — done`

### `DOC-2` 🟡 ✅ FIXED 2026-09-08 · `Calendar` names a `Holiday` day type that the enum does not have

`domain/model/entity/Calendar.cs:12` — `public required DayType DayType { get; set; } // Workday, Weekend, Holiday, Vacation, SickDay, Special`

`AdhdTimeOrganizer.Core`'s `DayType` has five members and no `Holiday`. This is the *right* design —
`CalendarDayFactory.cs:35-37` documents that a holiday is a `Workday` row with a `HolidayName`, and
`RepeatingPlannerTaskValidator.ValidDayTypes` correctly lists the five real ones. Only the comment is
stale, and it is exactly the comment someone extending `RepeatingPlannerTask.ScheduledForDayTypes`
would read first.

**Fix the code comment**, not the enum.

**Fixed 2026-09-08.** The comment, not the enum, as prescribed. It now lists the five real members and — since a
trailing `// Workday, Weekend, …` on the property line is what invited the phantom sixth in the first place —
says *why* there is no `Holiday` among them: a public holiday is a `Workday` row carrying a `HolidayName`, so
"is it a holiday" and "what kind of day is it" stay separate questions. That is the sentence the reader who was
about to add the member needs, and `CalendarDayFactory` is named so they can check it.

`Doc impact: none (source comment only)`

### `DOC-3` 🟡 ✅ FIXED 2026-09-08 · Two delete-behaviour tests are named for a contradiction that no longer exists

`AdhdTimeOrganizer.IntegrationTests/Endpoints/PlannerDeleteBehaviorTests.cs` —
`"FINDING: deleting a Calendar with tasks is Restricted (blocked), not Cascaded, contradicting docs/domain-map.md"`
and `"FINDING: deleting an Activity referenced by a PlannerTask cascades the task away, not a clean 409"`.

The rewritten `docs/domain-map.md:85` now states *"A calendar with tasks cannot be deleted | **DB** —
`planner_task.calendar_id` is `Restrict`"*, which is exactly what the test asserts. The display names
are archaeology from a superseded doc and will send the next reader looking for a contradiction that was
resolved.

**Fix the test names** (the assertions are correct and should not change).

**Fixed 2026-09-08.** Both display names now state the behaviour — *"Deleting a Calendar that still has tasks is
blocked at the database, not cascaded"* and *"Deleting an Activity cascades away every PlannerTask that
referenced it, completed ones included"*. Assertions untouched, as prescribed.

Two more copies of the same stale claim were in scope by consequence and were corrected with them: the class doc
opened with *"These are the opposite of what `docs/domain-map.md` … claim"*, and the second test's `because`
message ended *"the way docs/domain-map.md's 'Activity -> PlannerTask Restrict' claims"*. Renaming the tests
while leaving those would have moved the archaeology rather than removed it.

One thing went beyond the prescription. `domain-map.md` records the calendar edge but had **no row at all** for
`Activity → PlannerTask`, so that test's old name was the only place in the repo stating that deleting an
activity takes already-completed tasks with it. Dropping the name would have lost the fact, so the invariants
table gained a row for it, next to the calendar edge it is the exact opposite of, and the class doc now points
there instead of arguing with it.

`Doc impact: domain-map.md → Invariants (new row for the Activity → PlannerTask cascade) — done`

### `DOC-4` 🟠 ✅ FIXED 2026-09-08 · `DayPlanResponse.HasPlan` documents the pre-lazy-creation world

`application/dto/response/taskPlanner/DayPlanResponse.cs:52-53`:

> Calendar rows are bulk-seeded per user for whole years by `CalendarSeeder` — **they are not created
> lazily on first task.** So inside the seeded horizon every date has a row … and outside it no date
> does, no matter how much the user plans.

Both sentences are now false. `CalendarProvisioner.EnsureForDateAsync` creates the row on first task,
and `domain-map.md` § *Lazy day creation* is entirely about that. The **conclusion** the doc draws
(branch on `HasPlan`, not on calendar presence) is still right, and still worth keeping — for the
seeded-horizon reason, not the outside-it one.

**Fix the XML doc**; the property is fine.

**Fixed 2026-09-08.** The XML doc only; the property is unchanged. It now states that a calendar row arrives
**two** ways — `CalendarSeeder` bulk-seeding whole years, and `CalendarProvisioner.EnsureForDateAsync` creating
one lazily on the first task for a date — and points at `domain-map.md` § *Lazy day creation* for the second.

The conclusion is kept, on the half of the reasoning that survives: calendar presence answers "does this date
have a row yet", which inside the seeded horizon is true for every untouched day and is therefore never the
question the client means to ask. The finding notes the outside-the-horizon clause is dead; it is gone rather
than rewritten, because with lazy creation there is no longer any date the client can reason about that way.

`Doc impact: none (source XML doc only — domain-map.md already describes lazy creation correctly)`

### `DOC-5` 🟡 ✅ FIXED 2026-09-08 · `testing.md` credits `CompletionFanOutTests` with covering a handler it never touches

`docs/testing.md:94` lists *"`CompletionFanOutTests` (the two `*IsDoneChangedEvent` handlers that live
in this project)"* among the load-bearing guards owned elsewhere. `CompletionFanOutTests`' own class
doc also names `RoutineTodoListIsDoneChangedEventHandler`. But of its nine test methods, four exercise
`PlannerTaskIsDoneChangedEventHandler` (host-side) and four exercise
`TodoListItemIsDoneChangedEventHandler`; **none** exercises the Routines one. That the four defects in
`CQ-1`…`CQ-4` all live in the untested handler and none in the tested one is not a coincidence.

**Fix both docs after `TEST-21` lands**; until then the claim is simply wrong.

**Fixed 2026-09-08, after `TEST-21`.** `docs/testing.md`'s owned-elsewhere paragraph now names the two
handlers `CompletionFanOutTests` actually drives and points at the new suite for the third; § *The guards*
gains a `RoutineCompletionFanOutTests` row. `CompletionFanOutTests`' own class doc no longer claims the
routine handler and links to the new file. No known-gaps entry was needed — the gap closed rather than
being recorded.

`Doc impact: testing.md → The guards and the owned-elsewhere paragraph, plus the CompletionFanOutTests class doc — done`
