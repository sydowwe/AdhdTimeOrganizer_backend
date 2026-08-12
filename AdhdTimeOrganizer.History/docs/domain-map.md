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
| `/activity-history/dashboard/summary/{pie-chart,stacked-bars,summary-cards}` | POST | `query/dashboard/summary/*` |

⚠ **`gird` is not a typo to fix.** It is the shipped path (`EndpointPath => "gird"`) and the SPA calls
it that way.

⚠ **`aggregate-by-activity` exists because the pie chart groups by activity *name*.**
`HistoryPieChartItem` carries no `activityId`, and activity names are not unique, so a caller holding
an id (a rendered to-do item) cannot map a slice back to it. Do not "consolidate" the two: the
aggregate is keyed by id, spans all history with no date range, and **omits** ids with no logged rows
rather than returning zeros — which is what lets the caller divide by `entryCount` unguarded. Its
`(UserId, ActivityId)` predicate is served by the unique index's leading two columns; no new index.

`/activity-history/dashboard/calendar` (`CalendarActivityEndpoint`) is **host-side**, not here — it
reads the `Calendar` entity, which belongs to Planning.

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
