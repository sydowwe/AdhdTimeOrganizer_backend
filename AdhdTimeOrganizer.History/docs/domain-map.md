# AdhdTimeOrganizer.History — Domain Map

Navigation index for the slice. Open only what you need.

## Entities

Two entities, two tables, both in the **`history`** schema (`ModuleSchemas.History`). `ActivityHistory`
is the ledger; `TimerBoundaryAlarm` is short-lived scheduling state that self-drains.

```
ActivityHistory : BaseEntityWithActivity (→ BaseEntityWithUser → BaseTableEntity)
  UserId       long     NOT NULL, FK → user, cascade        (from BaseEntityWithUser)
  ActivityId   long     NOT NULL, FK → activity, cascade    (from BaseEntityWithActivity)
  StartTimestamp DateTime  NOT NULL
  EndTimestamp   DateTime  NOT NULL
  Length         IntTime   NOT NULL  (int seconds via IntTimeConverter)
  TodoListItemId    long?  NULL, FK → todo_list_item, SET NULL     (declared host-side)
  RoutineTodoListId long?  NULL, FK → routine_todo_list, SET NULL  (declared host-side)
```

`domain/model/entity/ActivityHistory.cs` ·
`infrastructure/persistence/configuration/ActivityHistoryConfiguration.cs`

The two item columns record **which task a recording was saved from** — stamped when the user accepts
the save-to-history prompt raised on completing a to-do item, a step (which sends its *parent item's*
id) or a routine item. They are null on everything the tracking heartbeat attributes, which is most
rows, and on everything recorded before they existed. They are a link, not a copy: durations live
here and nowhere else. Both are navigation-free and configured in
`AppDbContext.ConfigureCrossSliceRelationships`, because this slice can see neither TodoLists nor
Routines; the constraint names are pinned there for the same assembly-ordering reason as
`PlannerTaskConfiguration`'s. `SET NULL`, never cascade — deleting a task must not delete the record
that time was spent on it.

**Indexes** — all four load-bearing:

- `(UserId, ActivityId, StartTimestamp)` UNIQUE — one row per activity per start instant.
- `(UserId, StartTimestamp)` — the dashboards and `mv_activity_history_pattern` scan user + date
  range; the unique index cannot serve that shape because `ActivityId` sits between the two columns
  the scan filters on.
- `(TodoListItemId)` / `(RoutineTodoListId)` — Postgres does not index a FK for you, and `SET NULL`
  has to find these rows on every task delete. The daily recap reads through the first one too.

**No inverse collections.** Neither `User.ActivityHistoryList` nor `Activity.ActivityHistoryList`
exists any more — they were removed so Core stops pointing into the slices. Query
`dbContext.Set<ActivityHistory>().Where(h => h.ActivityId == …)` instead.

### TimerBoundaryAlarm

```
TimerBoundaryAlarm : BaseEntityWithUser (→ BaseTableEntity)
  UserId      long      NOT NULL, FK → user, cascade   (from BaseEntityWithUser)
  SessionId   Guid      NOT NULL   client-issued; groups one timer session's boundaries
  BoundaryAt  DateTime  NOT NULL   absolute UTC instant, scheduled verbatim
  Phase       TimerBoundaryPhase NOT NULL
  ActivityId  long?     NULL       plain column — no FK, no navigation, no index
  PhaseIndex / PhaseTotal / CycleIndex / CycleTotal  int?  NULL
  Url         string?   NULL, max 512, validated relative
```

`domain/model/entity/timerAlarm/TimerBoundaryAlarm.cs` ·
`infrastructure/persistence/configuration/timerAlarm/TimerBoundaryAlarmConfiguration.cs`

Two computed, non-mapped members carry contract weight: `JobKey` (`history.timer-boundary.{id}`,
derived from the row id so the boot registrar reschedules in place instead of duplicating) and `Tag`
(derived from the boundary *instant*, so the client rebuilds the identical tag with no round trip and
the push replaces the open tab's own notification rather than stacking a second one).

**Indexes** — two, both for a specific query:

- `(UserId, SessionId)` — cancel-by-session, the hot path, always user-scoped.
- `(BoundaryAt)` — the boot registrar's only query, which runs **unscoped** across all users and splits
  on this column into "re-register" and "too stale, delete". Hence `UserId` is not the leading column.

Cascade from the user, unlike `ActivityHistory`'s `SetNull` links: an alarm is scheduling state, not
recorded time, so a deleted account's pending alarms go with it rather than surviving to fire at
nobody. See `summary.md` → *Timer-boundary alarms* for the design constraints; the entity's own XML
remarks carry the rest.

## Endpoints (18)

| Route | Verb | File |
|---|---|---|
| `/activity-history` | POST | `command/CreateActivityHistoryEndpoint` |
| `/activity-history/{id}` | PUT | `command/UpdateActivityHistoryEndpoint` |
| `/activity-history/{id}` | DELETE | `command/DeleteActivityHistoryEndpoint` |
| `/activity-history/{id}` | GET | `query/GetByIdActivityHistoryEndpoint` |
| `/activity-history/form-select-options?includeArchived=` | GET | `query/FormSelectOptionsActivityHistoryEndpoint` |
| `/activity-history/filter` | POST | `query/FilterActivityHistoryEndpoint` |
| `/activity-history/aggregate-by-activity` | POST | `query/AggregateByActivityActivityHistoryEndpoint` |
| `/activity-history/dashboard/detail/{pie-chart,stacked-bars,summary-cards}` | POST | `query/dashboard/detail/*` |
| `/activity-history/dashboard/summary/{pie-chart,stacked-bars,summary-cards,time-of-day}` | POST | `query/dashboard/summary/*` |
| `/activity-history/dashboard/calendar` | POST | `CalendarActivityEndpoint` (directly under `endpoint/`) |
| `/activity-history/timer-alarm/schedule` | POST | `command/ScheduleTimerAlarmsEndpoint` |
| `/activity-history/timer-alarm/cancel` | POST | `command/CancelTimerAlarmsEndpoint` |

Both timer-alarm routes are POST, including cancel: it takes a session id in a body and is a **no-op
on an unknown session, never a 404** — a partially-registered set is the normal outcome of a tab dying
mid-flight. `schedule` is idempotent by `sessionId` and drops boundaries already in the past rather
than firing them immediately.

⚠ **There is no grid route.** `/activity-history/gird` was deleted with `CQ-1` — it applied neither
paging nor sorting, and nothing in the frontend ever called it (the claim that "the SPA calls it that
way" was never true). `/activity-history/filter` is the only list read on this entity.

⚠ **The select-options route is `form-select-options`, not `all-options`.** It comes from
`Core/application/endpoint/base/BaseActivityFormSelectOptionsEndpoint`, whose own remarks name
`/activity-history/form-select-options`; `all-options` is `BaseGetSelectOptionsEndpoint`'s route and
this endpoint does not derive from it. `HistoryRouteSmokeTests.HistorySelectOptionsRoute_IsRegistered`
pins the distinction. The route takes `?includeArchived=true` — the reason that base carries a flag at
all: an edit form still has to list the archived activity a stored row points at.

⚠ **`aggregate-by-activity` is still not the pie chart, now that the pie chart is id-keyed too.**
It once existed because `HistoryPieChartItem` carried nothing but a display name; every group-shaped
dashboard response now carries a `groupId` beside its `name` (see *Group identity* below). The
aggregate stays separate for the reasons that have nothing to do with keying: it spans all history
with no date range, and it **omits** ids with no logged rows rather than returning zeros — which is
what lets the caller divide by `entryCount` unguarded. Its `(UserId, ActivityId)` predicate is served
by the unique index's leading two columns; no new index.

### Group identity

Every group-shaped dashboard response identifies its group by the **id of the entity it was grouped
by** — `groupId` on `HistoryPieChartItem` / `HistorySummaryCard` / `HistoryGroupItem`, `roleId` on
`CalendarTopRoleItem` — resolved in one place, `application/dashboard/HistoryGrouping.cs`. `name` is
unchanged and is still the only thing rendered.

`groupId` is **nullable**, and null on exactly two rows, both synthetic: the `Uncategorized` bucket
(`groupBy: Category`, activities with no category) and the pie chart's `_other` roll-up. `roleId`
is never null — a role is required on every activity.

These endpoints group by that id, not by `(Name, Color)` as they used to. The old key did not
actually collide, but only because `Activity`, `ActivityRole` and `ActivityCategory` each carry an
unfiltered unique index on `(UserId, Name)` and every dashboard is user-scoped — a constraint the
endpoints neither state nor can see, and one archiving has a standing reason to want relaxed. What
the id fixes today is identity across a rename (`percentChange` / `isNew`, and any client holding an
earlier response) and telling a real group named `_other` apart from the roll-up.
`HistoryDashboardGroupIdentityTests` pins all of it, on rows rather than routes: a name-keyed
response is well-formed and its numbers add up.

`/activity-history/dashboard/calendar` (`CalendarActivityEndpoint`) lives here, at
`application/endpoint/CalendarActivityEndpoint.cs` — outside both `command/` and `query/`, because it
is the one dashboard that reads another slice's entity. It gets the calendar's days through Core's
**`ICalendarDayLookup`** seam, never by naming Planning's `Calendar`.

### `summary/time-of-day` is not group-shaped

It folds the range into **24 hour-of-day buckets** and carries no group at all, so nothing above
applies to it. It exists because no other response can answer *when in the day* this user's time sits:
stacked-bars tiles the range sequentially in `windowMinutes`-wide buckets and clips every day to a
picked `windowStartTime`/`windowEndTime`, so folding it by hour reads the chart controls rather than
the history; pie-chart and summary-cards carry no time dimension; `filter` takes a single date.
Its request is therefore a bare `DateRangeDto` — no `groupBy`, no `topN`, no window.

Three things about it are contract, not implementation detail, and each fails as a plausible number
rather than an error (`HistoryTimeOfDayDashboardTests` pins all three on values):

- `hours` is **always 24 entries in order 0…23**, zeros included — the client indexes it.
- The fold is in `User.Timezone`. A UTC fold shifts every user's answer by their offset.
- A record spanning an hour boundary is **split by elapsed time** across the hours it covers and
  counted in `entries` once per hour it touches — so `sum(entries)` is not the record count, while
  `sum(totalSeconds)` **is** the period total and equals `summary/pie-chart`'s `totals.totalSeconds`
  for the same range. That equality is why rows are selected by `StartTimestamp` and distributed
  whole: a record starting inside the range contributes its tail even past the range's last midnight.

## Navigation index — everything that is not an endpoint

| Name | Kind | Responsibility | Path |
|---|---|---|---|
| `HistoryGrouping` | Helper | The one place a dashboard group is resolved to `(Id, Name, Color)` — `HistoryGroupKey`. Every group-shaped response keys off this | `application/dashboard/HistoryGrouping.cs` |
| `HistoryPieChartBuilder` | Helper | The whole pie-chart response after the query: grouping, the 1% threshold, the `_other` roll-up, the totals. Both `detail/` and `summary/` pie-chart endpoints call it | `application/dashboard/HistoryPieChartBuilder.cs` |
| `HistorySummaryCardsBuilder` | Helper | The whole summary-cards response after the queries: top-N cards, baseline comparison, period-over-period total. Both `detail/` and `summary/` summary-cards endpoints call it | `application/dashboard/HistorySummaryCardsBuilder.cs` |
| `HistoryBaselineWindow` | Helper | Resolves the request's `HistoryBaseline` to the range the baseline query runs over, plus the `sameWeekday` filter and the day count the average divides by. Both switches are **exhaustive and throw** on an unhandled member — there is no fallback-to-`last7days` arm | `application/dashboard/HistoryBaselineWindow.cs` |
| `TimerBoundaryAlarmService` | Service | `ReplaceSessionAsync` / cancel-by-session / fire; owns the Scheduler `RunOnceAt` registration. Implements `ITimerBoundaryAlarmService` | `application/service/TimerBoundaryAlarmService.cs` |
| `ITimerBoundaryAlarmService` | Contract | The service's port, so the endpoints and the registrar depend on the shape rather than the class | `domain/serviceContract/ITimerBoundaryAlarmService.cs` |
| `TimerBoundaryAlarmJobHandler` | Job handler | Fires one alarm: resolves the activity name, pushes the notification, then **deletes the row** (self-drain). No-op if the row is gone | `application/job/TimerBoundaryAlarmJobHandler.cs` |
| `TimerBoundaryAlarmRegistrar` | `IHostedService` | Re-registers every pending alarm on boot and drops stale ones. **Not optional** — the Quartz store is in-memory; see `summary.md` | `infrastructure/scheduling/TimerBoundaryAlarmRegistrar.cs` |
| `ActivityHistoryTimeAttributionSink` | Seam impl | Core's `IActivityTimeAttributionSink`: records N seconds against an activity for Tracking. Mutates the caller's `DbContext`, **never** saves; extends a contiguous window instead of inserting. `TotalSecondsOnDayAsync` bounds the day half-open on **UTC** midnights (`[day 00:00Z, day+1 00:00Z)`), like every other day window in this slice — not the user's local day | `application/seam/ActivityHistoryTimeAttributionSink.cs` |
| `TodoListItemLoggedTimeSource` | Seam impl | Core's `ITodoListItemLoggedTimeSource` for TodoLists' daily recap. Keys on `TodoListItemId` **alone** | `application/seam/TodoListItemLoggedTimeSource.cs` |
| `ActivityHistoryActivityReferenceSource` | Seam impl | Core's `IActivityReferenceSource`: reports/repoints this slice's references to an activity for merge and delete | `application/seam/ActivityHistoryActivityReferenceSource.cs` |
| `ActivityHistoryReferencedIds` | Helper | The create/update ownership check over the three client-supplied ids. Needs the request scope and the principal, which is why it is not in the validator | `application/endpoint/command/ActivityHistoryReferencedIds.cs` |
| `ActivityHistoryValidator` | Validator | Create/update rules, incl. the mutual exclusion of `TodoListItemId` and `RoutineTodoListId` | `application/validator/ActivityHistoryValidator.cs` |
| `HistorySummaryDateRangeValidators` | Validators | One concrete class per `summary/` request type. FastEndpoints resolves validators by **exact type** and skips validation silently without one — that is why these are four classes, not one generic | `application/validator/HistorySummaryDateRangeValidators.cs` |
| `HistoryDetailSummaryCardsRequestValidator` | Validator | The only validator on a `detail/` request: `Baseline` must be a defined `HistoryBaseline`. It deliberately does not restate the range rules — the `detail/` requests carry a `DateAndTimeRangeDto` and are otherwise unvalidated | `application/validator/HistoryDetailSummaryCardsRequestValidator.cs` |
| `CalendarActivityRequestValidator` | Validator | The calendar dashboard's range rules. `CalendarActivityRequest` carries no `DateRangeDto`, so it restates ordered ends, the `MaxCustomRangeDays` span cap and a bounded `TopN` itself | `application/validator/CalendarActivityRequestValidator.cs` |
| `ActivityHistoryAggregateByActivityValidator` · `ScheduleTimerAlarmsValidator` | Validators | The aggregate request; the alarm batch (relative `Url`, boundary sanity) | `application/validator/` |
| `ActivityHistorySeeder` | Dev seeder | Per-user dev fixture, Order **300**. Seeds 30 days of history; truncates (dev seeders only) | `infrastructure/persistence/seeder/dev/ActivityHistorySeeder.cs` |
| `HistoryGroupBy` | Enum | The dashboard grouping dimension (activity / role / category) | `application/dto/enum/HistoryGroupBy.cs` |
| `HistoryBaseline` | Enum | What a summary card is compared against (`Last7Days` / `Last30Days` / `SameWeekday` / `AllTime`). Was a `string` with a silent fallback; an unknown value is now a 400, not a wrong-but-plausible 200 | `application/dto/enum/HistoryBaseline.cs` |

DTOs are not listed individually — `application/dto/` mirrors the endpoint tree
(`request/dashboard/{detail,summary}/`, `response/dashboard/`, `filter/`), so the endpoint's file path
tells you where its request and response live.

## Invariants

**Ownership.** `ActivityHistory` is `IEntityWithUser`, so `AppDbContext.OnModelCreating` applies the
global query filter (`!IsAuthenticated || e.UserId == currentUserId`) and every read through any
endpoint is scoped. The base endpoints' `ApplyUserScoping` is a **no-op virtual** and scopes nothing
— do not treat it as the guard. `GetFilteredTableActivityHistoryEndpoint` additionally calls
`FilteredByUser(User.GetId())` by hand.

**Ownership of referenced ids.** The Ownership invariant above covers *reads*. On a **write**, all
three ids on `ActivityHistoryRequest` — `ActivityId`, `TodoListItemId`, `RoutineTodoListId` — arrive
from the client and must belong to the caller. Nothing but the endpoint can check that: the row's own
`UserId` is stamped server-side, so the insert is a legitimate row of the caller's pointing somewhere
it should not; the `Activity` FK enforces existence, not ownership; and the two item columns are
navigation-free. `Create`/`UpdateActivityHistoryEndpoint` check all three in `BeforeMapping` through
Core's `IReferencedIdOwnershipService` and answer **404 on the offending field** — a foreign id and a
nonexistent one are the same answer, so the endpoint is not an existence oracle. Archived activities
still pass: archival is not ownership. `ActivityHistoryReferencedIdOwnershipTests` is the guard.

**Write ordering.** Any save touching `ActivityHistory` triggers
`SuggestionPatternRefreshInterceptor` (host-side), which `REFRESH`es three materialized views. They
must already exist or Postgres answers 42P01 — installed by `SuggestionPatternViewInstaller` at boot
and by `AppDbContextFixture.OnSchemaCreatedAsync` in tests.

**Timezone.** Every dashboard folds and bounds its range in `User.Timezone`, never UTC — the summary
range stops at the user's midnights, and `time-of-day` folds into the user's hours. *App-enforced
only*: nothing in the schema or the request shape stops a UTC fold, and the wrong answer is a
plausible number rather than an error. `HistoryDashboardUserZoneTests` is the guard, and
`HistoryCalendarDashboardTests` guards the calendar — the one dashboard that had drifted from this
(`DOC-2`), and the one where it matters twice over: its day cells come from `ICalendarDayLookup` keyed
by a bare `DateOnly`, which is already a *user's* day, so a UTC fold on the history side mismatches a
user-day calendar on the other.

**Alarm liveness.** A `TimerBoundaryAlarm` row is meaningful only while its Scheduler trigger exists,
and the trigger does not survive a restart — `TimerBoundaryAlarmRegistrar` re-pushes it on boot.
*Not enforced anywhere*: drop the hosted service and the rows still look correct while nothing ever
fires. Correspondingly, the table has no retention job and no `FiredAt` column because it self-drains
on fire, on cancel and on the registrar's stale sweep — a change that stops any of those three
draining turns it into an unbounded table with no other symptom.

## Cross-slice edges

Outbound: **Core only.**

```
AdhdTimeOrganizer.History
  └── AdhdTimeOrganizer.Core          entities (Activity, User, base shims), DTO bases,
                                      shared enums, IActivityMembershipSource,
                                      ICalendarDayLookup, IActivityTimeAttributionSink,
                                      IActivityReferenceSource, ITodoListItemLoggedTimeSource,
                                      IReferencedIdOwnershipService (over
                                      IUserOwnedReferenceVerifier)
```

This slice **implements** two seams as well as consuming one: `ActivityHistoryTimeAttributionSink`
(`IActivityTimeAttributionSink`, for Tracking) and `TodoListItemLoggedTimeSource`
(`ITodoListItemLoggedTimeSource`, for TodoLists' daily recap) — both in `application/seam/`. The
second must key on `TodoListItemId` alone; matching on the activity instead would credit the same
seconds to two items that share one. See `summary.md` → Gotchas.

Inbound (host → slice, the correct direction) — and **only** the host: `Program.cs` (the FastEndpoints
assembly scan and `AddHostedService<TimerBoundaryAlarmRegistrar>()`), `ModuleServiceExtensions`
(`ModuleAssemblies`), `AppDbContext` (`ApplyHostConfigurations` and the two cross-slice FK
declarations), `ModuleSchemas`, and `SuggestionPatternRefreshInterceptor`.

No slice references this one. The two that read recorded time do it without an edge, which is the
point: **Tracking** writes through Core's `IActivityTimeAttributionSink` (implemented here), and
**Planning**'s `GetSuggestionsRepeatingPlannerTaskEndpoint` reads
`PlannerSuggestionFromActivityHistory` — the keyless entity over the materialized view, which Planning
owns. Neither names `ActivityHistory`.

### The membership seam — declared, implemented, unconsumed

The four filters that used to reach into TodoLists and Routines. They lived on the `gird` grid, which
`CQ-1` deleted, so **nothing consumes this seam today**; the sources stay registered and guarded for
the next consumer:

| Filter field | Source key | Facet |
|---|---|---|
| `IsFromTodoList` | `ActivityMembershipSourceKeys.TodoList` | `TaskPriorityId` |
| `IsFromRoutineTodoList` | `ActivityMembershipSourceKeys.RoutineTodoList` | `RoutineTimePeriodId` |

```
(no consumer)  ──►  IActivityMembershipSource  ◄──implements──  TodoLists
                            (Core)                              Routines
```

Neither side references the other. See `summary.md` → "The membership seam" before touching it, and
`AdhdTimeOrganizer.Core/application/seam/IActivityMembershipSource.cs` for the composability contract.
