# AdhdTimeOrganizer.History — Domain Map

Navigation index for the slice. Open only what you need.

## Entity

One entity, one table.

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

`domain/model/entity/activityHistory/ActivityHistory.cs` ·
`infrastructure/persistence/configuration/activityHistory/ActivityHistoryConfiguration.cs`

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

## Endpoints (14)

| Route | Verb | File |
|---|---|---|
| `/activity-history` | POST | `command/CreateActivityHistoryEndpoint` |
| `/activity-history/{id}` | PUT | `command/UpdateActivityHistoryEndpoint` |
| `/activity-history/{id}` | DELETE | `command/DeleteActivityHistoryEndpoint` |
| `/activity-history/{id}` | GET | `query/GetByIdActivityHistoryEndpoint` |
| `/activity-history/all-options` | GET | `query/FormSelectOptionsActivityHistoryEndpoint` |
| `/activity-history/filter` | POST | `query/FilterActivityHistoryEndpoint` |
| `/activity-history/gird` | POST | `query/GetFilteredTableActivityHistoryEndpoint` |
| `/activity-history/aggregate-by-activity` | POST | `query/AggregateByActivityActivityHistoryEndpoint` |
| `/activity-history/dashboard/detail/{pie-chart,stacked-bars,summary-cards}` | POST | `query/dashboard/detail/*` |
| `/activity-history/dashboard/summary/{pie-chart,stacked-bars,summary-cards,time-of-day}` | POST | `query/dashboard/summary/*` |

⚠ **`gird` is not a typo to fix.** It is the shipped path (`EndpointPath => "gird"`) and the SPA calls
it that way.

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

`/activity-history/dashboard/calendar` (`CalendarActivityEndpoint`) is **host-side**, not here — it
reads the `Calendar` entity, which belongs to Planning.

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

## Invariants

**Ownership.** `ActivityHistory` is `IEntityWithUser`, so `AppDbContext.OnModelCreating` applies the
global query filter (`!IsAuthenticated || e.UserId == currentUserId`) and every read through any
endpoint is scoped. The base endpoints' `ApplyUserScoping` is a **no-op virtual** and scopes nothing
— do not treat it as the guard. `GetFilteredTableActivityHistoryEndpoint` additionally calls
`FilteredByUser(User.GetId())` by hand.

**Write ordering.** Any save touching `ActivityHistory` triggers
`SuggestionPatternRefreshInterceptor` (host-side), which `REFRESH`es three materialized views. They
must already exist or Postgres answers 42P01 — installed by `SuggestionPatternViewInstaller` at boot
and by `AppDbContextFixture.OnSchemaCreatedAsync` in tests.

## Cross-slice edges

Outbound: **Core only.**

```
AdhdTimeOrganizer.History
  └── AdhdTimeOrganizer.Core          entities (Activity, User, base shims), DTO bases,
                                      shared enums, IActivityMembershipSource,
                                      ITodoListItemLoggedTimeSource
```

This slice **implements** two seams as well as consuming one: `ActivityHistoryTimeAttributionSink`
(`IActivityTimeAttributionSink`, for Tracking) and `TodoListItemLoggedTimeSource`
(`ITodoListItemLoggedTimeSource`, for TodoLists' daily recap) — both in `application/seam/`. The
second must key on `TodoListItemId` alone; matching on the activity instead would credit the same
seconds to two items that share one. See `summary.md` → Gotchas.

Inbound (host → slice, the correct direction): `SuggestionPatternRefreshInterceptor`,
`SuggestionPatternViewInstaller`, `CalendarActivityEndpoint`, `GetUserDataExportEndpoint`,
`DesktopActivityHeartbeatEndpoint`, and `GetSuggestionsRepeatingPlannerTaskEndpoint` (Planning, which
reads `ActivityHistory` — that becomes a real `Planning → History` project edge when Planning is
extracted).

### The membership seam

The four grid filters that used to reach into TodoLists and Routines:

| Filter field | Source key | Facet |
|---|---|---|
| `IsFromTodoList` | `ActivityMembershipSourceKeys.TodoList` | `TaskPriorityId` |
| `IsFromRoutineTodoList` | `ActivityMembershipSourceKeys.RoutineTodoList` | `RoutineTimePeriodId` |

```
History  ──consumes──►  IActivityMembershipSource  ◄──implements──  TodoLists
   (Core)                        (Core)                             (+ host, until
                                                                     Routines exists)
```

Neither side references the other. See `summary.md` → "The membership seam" before touching it, and
`AdhdTimeOrganizer.Core/application/seam/IActivityMembershipSource.cs` for the composability contract.
