# AdhdTimeOrganizer.History — Domain Map

Navigation index for the slice. Open only what you need.

## Entities

Three entities, three tables, all in the **`history`** schema (`ModuleSchemas.History`). `ActivityHistory`
is the ledger; `ActivityHistorySession` groups the rows one timer sitting wrote; `TimerBoundaryAlarm` is
short-lived scheduling state that self-drains.

```
ActivityHistory : BaseEntityWithActivity (→ BaseEntityWithUser → BaseTableEntity)
  UserId       long     NOT NULL, FK → user, cascade        (from BaseEntityWithUser)
  ActivityId   long     NOT NULL, FK → activity, cascade    (from BaseEntityWithActivity)
  StartTimestamp DateTime  NOT NULL
  EndTimestamp   DateTime  NOT NULL
  Length         IntTime   NOT NULL  (int seconds via IntTimeConverter)
  TodoListItemId    long?  NULL, FK → todo_list_item, SET NULL     (declared host-side)
  RoutineTodoListId long?  NULL, FK → routine_todo_list, SET NULL  (declared host-side)
  SessionId         long?  NULL, FK → activity_history_session, SET NULL
  Phase             TimerSessionPhase?  NULL  (string: Focus / ShortBreak / LongBreak)
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
- `(SessionId)` — same FK reason, plus the row delete's "was that the session's last row" probe.

**No inverse collections.** Neither `User.ActivityHistoryList` nor `Activity.ActivityHistoryList`
exists any more — they were removed so Core stops pointing into the slices. Query
`dbContext.Set<ActivityHistory>().Where(h => h.ActivityId == …)` instead.

### ActivityHistorySession

```
ActivityHistorySession : BaseEntityWithUser (→ BaseTableEntity)
  UserId                long   NOT NULL, FK → user, cascade
  Kind                  TimerSessionKind     NOT NULL  (string: Pomodoro / Timer)
  Outcome               TimerSessionOutcome  NOT NULL  (string: Completed / StoppedEarly / RanOutWhileAway)
  PlannedFocusPeriods   int?  NULL   — both set for a Pomodoro, both null for a Timer
  CompletedFocusPeriods int?  NULL   — never more than planned
  PlannedLength         IntTime?  NULL (int seconds) — the focus time the timer was set for: a countdown's
                        length, or a Pomodoro's focus length × PlannedFocusPeriods. Optional, positive when sent
  Records               → ActivityHistory (inverse of SessionId)
```

`domain/model/entity/ActivityHistorySession.cs` ·
`infrastructure/persistence/configuration/ActivityHistorySessionConfiguration.cs` (the FK itself is on
`ActivityHistoryConfiguration`) · enums in `domain/model/enum/TimerSession.cs`

One sitting with one timer: a Pomodoro writes one row per phase that ran, a countdown exactly one. That
includes timers embedded in a planner task or to-do item (the frontend's `TrackTimeDialog`), which save
through the session endpoint too since OP6. The stopwatch, manual entries and tracking write no session —
their rows have `SessionId = null`, as does every row written before sessions existed, and every reader
treats a null as a session of one.

- **Outcome, counts and plan are stored as told, never derived.** A focus period cut short by Stop and one
  that ran its length look the same once rounded to whole minutes. Editing a row leaves them alone.
- **Lifetime follows the rows.** Created with its rows in one `SaveChanges`; deleted by
  `DeleteActivityHistoryEndpoint` in the same save as its last row; cascaded with the account. Reads reach a
  session only through a row, so an empty one never appears. The one leak is a hard delete of an activity,
  which cascades the rows in the database and leaves the session behind — invisible, four columns, gone
  with the account.
- **`SET NULL` from row to session**, for the reason the item links use it: rows are the record of time,
  the session is a grouping over them.
- **`GET /activity-history/session/fit` is the one reader of `Outcome` and `CompletedFocusPeriods`** beyond the
  echo on every row and the GDPR export (F2-07). `domain/service/TimerLengthFit` turns the activity's newest 20
  sessions in 90 days into focus stretches. A countdown row is one stretch, and so is each Pomodoro *focus* row
  (told apart by `Phase`, since the rest activity may be the same activity). A stretch that ran its length is
  censored, meaning "at least this long", and only one Stop cut short counts against a longer length.
  `RanOutWhileAway` sessions are left out. The offer is the largest 5-minute length that at least 70% of the
  stretches bearing on it reached, with 5 of them over 5 sessions (`EstimateFitRule`), and otherwise nothing.
  The response carries no completion share, so the client cannot render one as a score.
- **`POST /activity-history/dashboard/summary/timer-plan` is the range reader of `PlannedLength`** (OP6's trend
  line). Sessions belong to the user's day their first row starts on. It counts them by outcome, names the ones
  with no plan (`withoutPlanCount`, pre-OP6) instead of dropping them, and sums `PlannedLength` against the focus
  rows (by `Phase` again) over the completed and stopped-early sessions only, so both sums cover the same
  sessions. `RanOutWhileAway` is counted and kept out of both sums. The shortfall is summed over stopped-early
  sessions only, since a completed countdown sits up to a minute under its plan from rounding alone. It shares the
  data with `session/fit` and not the question: one is per activity, for the timer setup, and the other is per
  range, for reflection. Don't merge them. Pinned by `HistoryTimerPlanSummaryTests`.

### TimerBoundaryAlarm

```
TimerBoundaryAlarm : BaseEntityWithUser (→ BaseTableEntity)
  UserId      long      NOT NULL, FK → user, cascade   (from BaseEntityWithUser)
  SessionId   Guid      NOT NULL   client-issued; groups one timer session's boundaries
  BoundaryAt  DateTime  NOT NULL   absolute UTC instant, scheduled verbatim
  Kind        TimerAlarmKind NOT NULL (int, default 0 = Boundary)   Boundary | StopwatchCheckIn
  Phase       TimerBoundaryPhase?  NULL   required on a Boundary, null on a check-in (validator)
  ElapsedMinutes int?   NULL       check-in only: the stopwatch's running time at BoundaryAt
  ActivityId  long?     NULL       plain column — no FK, no navigation, no index
  PhaseIndex / PhaseTotal / CycleIndex / CycleTotal  int?  NULL   boundaries only
  Url         string?   NULL, max 512, validated relative
```

`Kind` is per session (`ScheduleTimerAlarmsRequest.Kind`, default `Boundary`), and decides what the row raises:
`Boundary` → `NotificationType.TimerBoundary`, quiet-hours exempt; `StopwatchCheckIn` → `NotificationType.StopwatchCheckIn`
("still on X? 1 h 30 min", P3-03), which the handler **skips** — no notification row — when the recipient is inside
their quiet hours, and which `IRunningTimerLookup` does not count as a running timer.

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

## Endpoints (21)

| Route | Verb | File |
|---|---|---|
| `/activity-history` | POST | `command/CreateActivityHistoryEndpoint` |
| `/activity-history/session` | POST | `command/CreateActivityHistorySessionEndpoint` |
| `/activity-history/session/fit?activityId=` | GET | `query/GetFitActivityHistorySessionEndpoint` (F2-07) |
| `/activity-history/{id}` | PUT | `command/UpdateActivityHistoryEndpoint` |
| `/activity-history/{id}` | DELETE | `command/DeleteActivityHistoryEndpoint` |
| `/activity-history/{id}` | GET | `query/GetByIdActivityHistoryEndpoint` |
| `/activity-history/form-select-options?includeArchived=` | GET | `query/FormSelectOptionsActivityHistoryEndpoint` |
| `/activity-history/filter` | POST | `query/FilterActivityHistoryEndpoint` |
| `/activity-history/aggregate-by-activity` | POST | `query/AggregateByActivityActivityHistoryEndpoint`. **No client caller since 2026-10-12**: the SPA's last reader, the routine list's activity-keyed calibration, moved to the item-keyed `TypicalSeconds` (F2-06). A candidate for deletion |
| `/activity-history/dashboard/detail/{pie-chart,stacked-bars,summary-cards}` | POST | `query/dashboard/detail/*` |
| `/activity-history/dashboard/summary/{pie-chart,stacked-bars,summary-cards,time-of-day,timer-plan}` | POST | `query/dashboard/summary/*` |
| `/activity-history/dashboard/calendar` | POST | `CalendarActivityEndpoint` (directly under `endpoint/`) |
| `/activity-history/timer-alarm/schedule` | POST | `command/ScheduleTimerAlarmsEndpoint` |
| `/activity-history/timer-alarm/cancel` | POST | `command/CancelTimerAlarmsEndpoint` |

Both timer-alarm routes are POST, including cancel: it takes a session id in a body and is a **no-op
on an unknown session, never a 404** — a partially-registered set is the normal outcome of a tab dying
mid-flight. `schedule` is idempotent by `sessionId` and drops boundaries already in the past rather
than firing them immediately.

⚠ **`session` is all-or-nothing and answers every row id.** One graph, one `SaveChanges`, one
transaction: a refused record — an unowned activity (404 keyed `records[i].activityId`, checked for every
record before anything is written) or a unique-index collision (409) — leaves nothing behind. It answers
`201 { sessionId, recordIds }` with `recordIds` in request order. Its validator is stricter than the single
create in two ways, both deliberate: every `startTimestamp` must carry an offset, and records must be in
strictly increasing start order (which also keeps two records of one activity off the unique index).
Overlap under a minute is allowed — whole-minute rounding produces it.

⚠ **There is no grid route.** `/activity-history/grid` was deleted with `CQ-1` — it applied neither
paging nor sorting, and nothing in the frontend ever called it (the claim that "the SPA calls it that
way" was never true). `/activity-history/filter` is the only list read on this entity.

⚠ **`filter` bounds its window on `StartTimestamp`, half-open `[from, to)`** — the same predicate as
every dashboard, the calendar and both seams, so the detail list and the charts above it always agree
on which records are in the period. A record starting inside the window contributes its whole tail,
however far past `to` it ends; a record that started before `from` is out even if it is still running.
Do not "tighten" the upper bound onto `EndTimestamp`: that drops long sessions from the list while
they still count in every chart, and it defeats the `(UserId, StartTimestamp)` index.

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

### Entry counts are sittings, not rows

Every count a client reads as "how many times" goes through `HistorySittings.CountSittings`: a session is
one entry however many rows it wrote, a session-less row is one entry. That covers
`aggregate-by-activity`'s `entryCount` (the client divides `totalSeconds` by it — rows would quarter a
four-period Pomodoro's typical time), both pie charts' `entries` / `totals.totalEntries`, and the
calendar's `sessionCount` (a session crossing the user's midnight counts on each day it has a row, like its
seconds). On a subset of rows a session counts once if any of its rows are in it — so a Pomodoro whose focus
and rest land in two pie groups is one entry in each, `Σ items[].entries` can exceed `totalEntries`, and the
`_other` roll-up counts over its rows rather than summing its groups.

Not counted this way, on purpose: `time-of-day`'s per-hour `entries` (row-hour overlaps, documented as not
a record count), and the host's account-deletion summary, whose `TrackedSessionCount` is documented as
`ActivityHistory` rows. The summary cards and both stacked-bars endpoints only sum.

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

`days` (frontend OP21) carries one 24-bit `hourMask` per day of the range that touched any hour, so the
client can count the days its chosen band was used ("on 12 of 30 days"). The band is found client-side, so
the server cannot count those days itself, and the 24 totals cannot: they fold three long days and twelve
short ones into the same buckets. A bit lands on the day the *hour* fell on, so a record crossing midnight
marks two days, and days past the range's last midnight are dropped even though the hour totals keep their
tail. `daysWithActivity` still counts by start, so it can be less than `days.length`.

## Navigation index — everything that is not an endpoint

| Name | Kind | Responsibility | Path |
|---|---|---|---|
| `HistoryGrouping` | Helper | The one place a dashboard group is resolved to `(Id, Name, Color)` — `HistoryGroupKey`. Every group-shaped response keys off this | `application/dashboard/HistoryGrouping.cs` |
| `HistoryPieChartBuilder` | Helper | The whole pie-chart response after the query: grouping, the 1% threshold, the `_other` roll-up, the totals. Both `detail/` and `summary/` pie-chart endpoints call it | `application/dashboard/HistoryPieChartBuilder.cs` |
| `HistorySummaryCardsBuilder` | Helper | The whole summary-cards response after the queries: top-N cards, baseline comparison, period-over-period total. Both `detail/` and `summary/` summary-cards endpoints call it | `application/dashboard/HistorySummaryCardsBuilder.cs` |
| `HistorySittings` | Helper | `CountSittings` — the one rule for "how many entries": sessions count once, session-less rows once each. See *Entry counts are sittings, not rows* | `application/dashboard/HistorySittings.cs` |
| `HistoryBaselineWindow` | Helper | Resolves the request's `HistoryBaseline` to the range the baseline query runs over, plus the `sameWeekday` filter and the day count the average divides by. Both switches are **exhaustive and throw** on an unhandled member — there is no fallback-to-`last7days` arm | `application/dashboard/HistoryBaselineWindow.cs` |
| `TimerBoundaryAlarmService` | Service | `ReplaceSessionAsync` / cancel-by-session / fire; owns the Scheduler `RunOnceAt` registration. Implements `ITimerBoundaryAlarmService` | `application/service/TimerBoundaryAlarmService.cs` |
| `ITimerBoundaryAlarmService` | Contract | The service's port, so the endpoints and the registrar depend on the shape rather than the class | `domain/serviceContract/ITimerBoundaryAlarmService.cs` |
| `TimerBoundaryAlarmJobHandler` | Job handler | Fires one alarm: resolves the activity name, pushes a `TimerBoundary` or `StopwatchCheckIn` by the row's `Kind` (a check-in inside quiet hours is skipped, not sent), then **deletes the row** (self-drain). No-op if the row is gone | `application/job/TimerBoundaryAlarmJobHandler.cs` |
| `TimerBoundaryAlarmRegistrar` | `IHostedService` | Re-registers every pending alarm on boot and drops stale ones. **Not optional** — the Quartz store is in-memory; see `summary.md` | `infrastructure/scheduling/TimerBoundaryAlarmRegistrar.cs` |
| `ActivityHistoryTimeAttributionSink` | Seam impl | Core's `IActivityTimeAttributionSink`: records N seconds against an activity for Tracking. Mutates the caller's `DbContext`, **never** saves; extends a contiguous window instead of inserting — but never a timer-session row, which stays what the timer measured. `TotalSecondsBetweenAsync` sums a half-open `[fromUtc, toUtc)` range the **caller** supplies — the heartbeat passes the user's own midnights, so "the day" is theirs, not UTC's (it used to take a `DateOnly` bounded on UTC midnights, which drove yesterday's planner task for a Bratislava user's first hours) | `application/seam/ActivityHistoryTimeAttributionSink.cs` |
| `TodoListItemLoggedTimeSource` | Seam impl | Core's `ITodoListItemLoggedTimeSource` for TodoLists' daily recap. Keys on `TodoListItemId` **alone** | `application/seam/TodoListItemLoggedTimeSource.cs` |
| `RoutineItemLoggedTimeSource` | Seam impl | Core's `IRoutineItemLoggedTimeSource` for Routines' typical item length (F2-06): the recordings per routine item in a UTC range, one entry per row. Keys on `RoutineTodoListId` **alone** | `application/seam/RoutineItemLoggedTimeSource.cs` |
| `ActivityHistoryActivityReferenceSource` | Seam impl | Core's `IActivityReferenceSource`: reports/repoints this slice's references to an activity for merge and delete | `application/seam/ActivityHistoryActivityReferenceSource.cs` |
| `ActivityHistoryReferencedIds` | Helper | The create/update ownership check over the three client-supplied ids. Needs the request scope and the principal, which is why it is not in the validator | `application/endpoint/command/ActivityHistoryReferencedIds.cs` |
| `ActivityHistoryValidator` | Validator | Create/update rules, incl. the mutual exclusion of `TodoListItemId` and `RoutineTodoListId` | `application/validator/ActivityHistoryValidator.cs` |
| `HistorySummaryDateRangeValidators` | Validators | One concrete class per `summary/` request type. FastEndpoints resolves validators by **exact type** and skips validation silently without one — that is why these are four classes, not one generic | `application/validator/HistorySummaryDateRangeValidators.cs` |
| `HistoryDetailSummaryCardsRequestValidator` | Validator | The only validator on a `detail/` request: `Baseline` must be a defined `HistoryBaseline`. It deliberately does not restate the range rules — the `detail/` requests carry a `DateAndTimeRangeDto` and are otherwise unvalidated | `application/validator/HistoryDetailSummaryCardsRequestValidator.cs` |
| `CalendarActivityRequestValidator` | Validator | The calendar dashboard's range rules. `CalendarActivityRequest` carries no `DateRangeDto`, so it restates ordered ends, the `MaxCustomRangeDays` span cap and a bounded `TopN` itself | `application/validator/CalendarActivityRequestValidator.cs` |
| `ActivityHistoryAggregateByActivityValidator` · `ScheduleTimerAlarmsValidator` | Validators | The aggregate request; the alarm batch (relative `Url`, boundary sanity) | `application/validator/` |
| `CreateActivityHistorySessionValidator` | Validator | The session shape: kind/outcome/count consistency, 1..64 records (exactly one for a Timer), phase present iff Pomodoro, strict start order, offset-carrying starts, B17's positive length per record | `application/validator/CreateActivityHistorySessionValidator.cs` |
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
                                      IRoutineItemLoggedTimeSource,
                                      IReferencedIdOwnershipService (over
                                      IUserOwnedReferenceVerifier)
```

This slice **implements** seams as well as consuming them. Three of them are
`ActivityHistoryTimeAttributionSink` (`IActivityTimeAttributionSink`, for Tracking),
`TodoListItemLoggedTimeSource` (`ITodoListItemLoggedTimeSource`, for TodoLists' daily recap and estimate
fit) and `RoutineItemLoggedTimeSource` (`IRoutineItemLoggedTimeSource`, for Routines' typical item length),
all in `application/seam/`. The two item sources must key on their own item link alone. Matching on the
activity instead would credit the same seconds to two items that share one. See `summary.md` → Gotchas.

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

The four filters that used to reach into TodoLists and Routines. They lived on the `grid` grid, which
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