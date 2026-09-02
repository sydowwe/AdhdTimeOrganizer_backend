# History — 02 · Findings

Point-in-time snapshot, 2026-09-01. Delta review against
`AdhdTimeOrganizer.History/docs/{summary,domain-map,testing}.md`, which are treated as the spec.
Severity: 🔴 blocker (legal / security / data-corruption) · 🟠 important (wrong behaviour or a
maintainability hole) · 🟡 polish.

Nothing here re-flags a previously known item. The three standing caveats in `docs/testing.md`
(registrar boot re-registration untested, `ActivityHistorySeeder` untested, the accepted residual cancel
race) are acknowledged as-is and are **not** findings.

Two findings are marked **PLAUSIBLE** rather than CONFIRMED: `CQ-4` and `CQ-5` rest on Npgsql's
strict-UTC rule for `timestamp with time zone` parameters. That rule is in force here — the snapshot
confirms `activity_history.start_timestamp` is `timestamp with time zone`, and nothing in the solution
sets `Npgsql.EnableLegacyTimestampBehavior`. Each has a one-line test in its prompt file that settles it.

---

## 5. Security & authorization

### `SEC-1` 🟠 ✅ FIXED 2026-09-01 · A timer alarm accepts any `activityId`, and the job handler resolves the name unscoped

`AdhdTimeOrganizer.History/application/validator/ScheduleTimerAlarmsValidator.cs:35` ·
`application/job/TimerBoundaryAlarmJobHandler.cs:52`

`ScheduleTimerAlarmsRequest.ActivityId` is validated only as `GreaterThan(0)` and is copied onto every
`TimerBoundaryAlarm` row. `TimerBoundaryAlarm.ActivityId` deliberately carries **no FK** (correct — the
row lives minutes), so the database does not check it either. At fire time the handler resolves it:

```csharp
await dbContext.Set<Activity>().Where(a => a.Id == activityId).Select(a => (string?)a.Name)
```

with no `UserId` predicate — and the handler runs in a **background, unauthenticated** context, which
the class's own remarks note means the global user filter (`ScopeUserId == null || …`) lets *every*
row through. That is the right design for reading the alarm's own row off its `UserId`; it is the wrong
one for a client-supplied id. A user who posts another user's `activityId` (ids are sequential `long`s)
receives a push whose body carries that user's activity name.

**Fix:** validate ownership at the endpoint — the schedule endpoint already has `User.GetId()` in hand,
so `if (req.ActivityId is { } id && !await db.Set<Activity>().AnyAsync(a => a.Id == id && a.UserId ==
userId, ct)) ThrowError(...)`. Belt-and-braces: scope the handler's lookup with
`a.UserId == alarm.UserId`, which costs nothing and makes the leak unreachable even if a bad row exists.

`Doc impact: summary.md → Timer-boundary alarms` (the "the recipient is always the caller, so there is
nothing to spoof" note is true of the recipient and not of `ActivityId`; state the ownership check).

### `SEC-2` 🟠 ✅ FIXED 2026-09-01 · The grid returns raw exception text to the client

`application/endpoint/query/GetFilteredTableActivityHistoryEndpoint.cs:61-65`

```csharp
catch (Exception ex)
{
    AddError($"An error occurred while retrieving filtered data: {ex.Message}");
    await Send.ErrorsAsync(500, ct);
}
```

`ex.Message` from EF/Npgsql carries table and column names, the failing SQL fragment and sometimes
parameter values — handed to any authenticated caller. It is also the only `catch`-all in the slice, and
it swallows `OperationCanceledException` into a 500 (a client disconnect logged as a server fault) and
suppresses everything from the logging pipeline: the message goes to the *client*, not to `ILogger`.
This is also what turns `CQ-11`-class input problems (a `DateFrom` posted without a `Z`) into a leak
rather than a 400.

**Fix:** delete the `catch`, or narrow it: log via `ILogger` with the correlation id, rethrow
`OperationCanceledException`, and return a fixed message. No other endpoint in the slice does this.

**Fixed:** the `catch` is gone. `Program.cs`'s `UseUnhandledExceptionHandler` already logs the
exception and returns a 500 carrying only an error id, and the pipeline above it already maps a
client-aborted `OperationCanceledException` away from a 500 — so the handler now matches every other
endpoint in the slice.

`Doc impact: none`

### `SEC-3` 🟡 ✅ FIXED 2026-09-01 · The calendar dashboard is the one unbounded, unvalidated range in the slice

`application/endpoint/CalendarActivityEndpoint.cs:14-32` · `application/dto/request/CalendarActivityRequest.cs`

Every `summary/` route runs through `DateRangeDto.ApplyValidationRules` and its `MaxCustomRangeDays`
cap, enforced by a concrete validator per request type. `CalendarActivityRequest` has **no validator at
all**: `StartDate`/`EndDate` are unbounded and unordered (an inverted range yields an empty answer
rather than a 400), and `TopN` is unbounded and unchecked. A single request for a decade loads every
`ActivityHistory` row the caller owns, with `Activity` and `ActivityRole` joined, into memory
(see `PERF-4`). Authenticated-user resource exhaustion, cheap to trigger, no cap anywhere in the path.

**Fix:** add `CalendarActivityRequestValidator` beside the four `summary/` ones — `EndDate >= StartDate`,
span within `DateRangeDto.MaxCustomRangeDays`, `TopN` in `1..10`. Note the same silent-resolution trap
applies: FastEndpoints binds a validator by the request's exact type, so it must be a concrete class.

**Fixed:** `application/validator/CalendarActivityRequestValidator.cs` — a concrete
`Validator<CalendarActivityRequest>`. `CalendarActivityRequest` carries no `DateRangeDto`, so it cannot
reuse `ApplyValidationRules` and restates the same three constraints: both dates non-empty, `EndDate >=
StartDate`, an inclusive span under `DateRangeDto.MaxCustomRangeDays` (guarded on the inverted case so a
negative span is reported as inverted rather than as "too long"), and `TopN` in `1..10`
(`MaxTopN`). No frontend caller sends `TopN` today, so the `1..10` bound narrows nothing in use.

`Doc impact: domain-map.md → Navigation index` (add the validator row beside
`HistorySummaryDateRangeValidators`).

### `SEC-4` 🟠 ✅ FIXED 2026-09-02 · Nothing checks that `ActivityId` / `TodoListItemId` / `RoutineTodoListId` belong to the caller

`application/validator/ActivityHistoryValidator.cs:11-28` ·
`application/dto/request/ActivityHistoryRequest.cs:29-38`

`ActivityHistoryRequest : ActivityIdRequest` carries a bare `long ActivityId` with no ownership guard —
`ActivityIdRequest` and `IActivityIdRequest` are plain id holders, `BaseCreateEndpoint` performs no
such check, and the validator only asserts `NotEmpty` / `GreaterThan(0)`. The row's own `UserId` is
stamped server-side (that part is right), so the insert succeeds and creates a row *owned by the caller*
that points at *another user's* activity. The `Activity` FK enforces existence, not ownership.

Two outcomes, both bad and both worth a 400 instead:

1. Every read path `Include`s `ah.Activity` and projects `e.Activity.Name` / `.Role` / `.Category`.
   `Activity` is `IEntityWithUser`, so the global filter applies to the include as well — the join
   yields null and `ActivityHistoryResponse.Projection`'s non-nullable `Id = e.Activity.Id` materializes
   from a null. The user has poisoned their own grid: every read of that date group fails, and via
   `SEC-2` answers 500 with the EF message.
2. Wherever a filter is *not* applied to the join, the foreign activity's name and colour project
   straight back to the caller.

The two item links have the same hole with milder consequences: a recording can be linked to another
user's `TodoListItem`. `TodoListItemLoggedTimeSource` filters on `UserId` **and** the item ids, so the
victim's recap is unaffected — the damage is confined to a dangling link and a `SET NULL` that fires on
someone else's delete.

**Fix:** one ownership check in the validator (or a small `IActivityIdRequest` guard in Core, since
several slices share the shape) covering all three ids. Prefer Core: this is not a History-specific rule.

**Fixed:** in Core, as suggested — but as a service rather than a validator rule, because the check
needs the request scope's `DbContext` and the principal, and FluentValidation validators here are
stateless rule declarations. `IReferencedIdOwnershipService` (`Core/domain/serviceContract/`,
implemented by `Core/application/service/ReferencedIdOwnershipService.cs`) answers both halves:
`Activity` directly — lifting `AppQueryFilters.ActivityArchived` only, since archival is not
ownership and an edit of pre-archival history must still save — and the two item ids through a new
keyed seam, `IUserOwnedReferenceVerifier`, implemented by `TodoListItemOwnedReferenceVerifier`
(TodoLists) and `RoutineTodoListOwnedReferenceVerifier` (Routines). A seam rather than a direct query
because History can see neither slice; both verifiers filter on the user id they are handed rather
than the ambient query filter, since this is a security answer and not a scoped read.

`Create`/`UpdateActivityHistoryEndpoint` call it from `BeforeMapping` via the shared
`ActivityHistoryReferencedIds` helper and answer **404 on the offending field** — a foreign id and a
nonexistent one stay indistinguishable, so the endpoint is not an existence oracle over another
account's ids. Update checks all three even though `UpdateEntity` deliberately does not write the item
links: one query beats two different rules for one DTO.

The keyed lookup **fails closed** — an unclaimed key rejects the write instead of skipping the check —
so a slice dropped from `ModuleAssemblies` degrades to a 404 rather than back to this hole.
`SeamWiringTests.UserOwnedReferenceVerifiers_CoverEveryKey_Exactly` pins the registration and
`ActivityHistoryReferencedIdOwnershipTests` asserts on rows in both directions (three foreign ids
refused with nothing written, the caller's own activity + item accepted and linked, an own *archived*
activity still accepted, and the update path refused without applying).

`Doc impact: domain-map.md → Invariants` (add an "ownership of referenced ids" line — the Ownership
invariant today covers reads only, and states nothing about ids arriving on a write).

---

## 6. Code quality

### `CQ-1` 🟠 · The grid ignores paging and sorting, both of which its request declares `required`

`application/endpoint/query/GetFilteredTableActivityHistoryEndpoint.cs:37-57`

The endpoint takes `BaseFilterSortPaginateRequest<ActivityHistoryFilterRequest>`, whose
`ItemsPerPage` and `Page` are `required` (framework:
`application/dto/request/base/table/BaseFilterSortPaginateRequest.cs:8-10`) and which carries `SortBy`
from `BaseFilterSortRequest`. `HandleAsync` reads `UseFilter` and `Filter` and **nothing else**: no
`Skip`/`Take`, no sort application. Every call returns the caller's entire ledger, grouped by date, with
`Activity` + `Role` + `Category` joined per row. The client is sending page numbers into a void.

This is both correctness (the response is not the page that was asked for, and the ordering is whatever
`OrderBy(response => response.Date)` gives, not `SortBy`) and the slice's worst scaling problem
(`PERF-1`). `HistoryCrudAuthMatrixTests` posts `itemsPerPage: 100, page: 1` and cannot notice, because
it seeds one row (`TEST-1`).

**Fix:** decide the contract first — a date-grouped response and a row-oriented page are not the same
shape, which is presumably why paging was never wired. The honest options are (a) page the *dates*
(group in SQL, `Skip`/`Take` on the date keys, return `PageCount`), or (b) drop the paging fields from
the request and cap the range instead. Either way apply `SortBy`, since the SPA sends it.

`Doc impact: domain-map.md → Endpoints` (the `gird` row should say what the response actually pages by,
and `summary.md`'s grid note should stop implying a paginated grid).

### `CQ-2` 🟠 ✅ FIXED 2026-09-02 · The detail and summary dashboards are duplicated, not shared

`application/endpoint/query/dashboard/detail/HistoryDetailSummaryCardsEndpoint.cs:34-182` vs
`…/dashboard/summary/HistorySummarySummaryCardsEndpoint.cs:36-182` ·
`…/detail/HistoryDetailPieChartEndpoint.cs:33-100` vs `…/summary/HistorySummaryPieChartEndpoint.cs:34-96`

The two summary-cards endpoints are ~150 lines of byte-identical logic (`ComputeBaselineRange`,
`GetWeekdaysInRange`, `ComputeBaselineDays`, the card assembly, the period comparison) differing only in
how `(from, to)` is derived — `ToDateTimeRange` vs `ToUtcRange`. The two pie-chart endpoints are ~85
identical lines differing in the same one line. The `_other` roll-up, the 1% threshold and the
`maxItems - 1` reservation are each written twice.

Everything in `CQ-3`, `CQ-4`, `CQ-6` and `CQ-7` below is a bug that exists in **two files**, and a fix
applied to one of them will pass review and ship half-done. That is the concrete cost, not aesthetics.

**Fix:** extract the shared bodies into `application/dashboard/` beside `HistoryGrouping` — e.g.
`HistoryPieChartBuilder.Build(records, groupBy, maxItems)` and
`HistorySummaryCardsBuilder.Build(records, baselineRecords, …)` — leaving each endpoint as range
resolution + query + one call. `HistoryGrouping` already sets the precedent for "the one place a
dashboard concept is resolved".

**Fixed:** three helpers in `application/dashboard/`, beside `HistoryGrouping`.
`HistoryPieChartBuilder.Build(records, groupBy, maxItems)` owns the grouping, the 1% threshold, the
`_other` roll-up and the totals; `HistorySummaryCardsBuilder.Build(...)` owns the top-N cards, the
baseline comparison and the period comparison; `HistoryBaselineWindow` owns the `baseline` string —
`Resolve` gives the range the baseline query runs over, `Filter` applies the `sameWeekday` filter and
`DayCount` gives the divisor. It is a separate type because the range has to be resolved *before* the
query the builder consumes. Each of the four endpoints is now range resolution + query + one call, and
each of the two `switch`es on `baseline` exists once — which is what `CQ-7` needs before it can turn
them into an enum.

`Doc impact: domain-map.md → Navigation index` (new helper rows). **Done** — three rows added.

### `CQ-3` 🟠 ✅ FIXED 2026-09-02 · `detail/stacked-bars` always emits 24 hourly windows, ignoring the picked range

`application/endpoint/query/dashboard/detail/HistoryDetailStackedBarsEndpoint.cs:60-72`

```csharp
private static List<(DateTime Start, DateTime End)> GenerateWindows(DateTime from, DateTime to)
{
    for (var h = 0; h < 24; h++) { var start = from.AddHours(h); windows.Add((start, start.AddHours(1))); }
}
```

`to` is accepted and never read. A detail request picks a date *and* a time window
(`DateAndTimeRangeDto`), so a user asking for 09:00–17:00 gets 24 bars, 16 of which lie outside the
window they picked and are always empty. The summary sibling does this correctly and deliberately —
`GenerateFixedWindows` walks calendar dates and clips each band at `dayEnd` — with a comment explaining
why. The two endpoints therefore disagree about what a picked window means, which is exactly what
`HistoryDashboardUserZoneTests.SummaryAndDetailStackedBars_AgreeOnTheInstantsAPickedWindowNames`
exists to prevent; it passes because it only compares the instants of the windows that *do* overlap.

Secondary: `from.AddHours(h)` is UTC-instant arithmetic, so on a DST transition day the bars drift
against the user's clock by the offset change — the exact bug the summary side's comment describes
having fixed.

**Fix:** `while (current < to)` with a final clip, and resolve each boundary through the zone the way
`GenerateFixedWindows` does. `TEST-7`.

**Fixed:** `GenerateWindows` now takes the picked `Date`, the picked start `TimeOnly` and the resolved
`to`, walks the user's **wall clock** an hour at a time and converts each boundary back with
`WallClockZone.ToUtc`, clipping the last band at `to`. `to` comes from
`DateAndTimeRangeDto.ToDateTimeRange`, which has already applied the over-midnight rule, so the loop
does not restate it. A band the spring-forward gap swallows has both boundaries resolve to the moment
the gap closes and is dropped rather than emitted zero-length — which is also what terminates the walk
past a transition.

`TEST-7` landed with it, in `HistoryDashboardUserZoneTests`: a theory over 09:00–17:00 (8 bars),
00:00–00:00 (24, so the fix does not simply cap in the wrong place) and 22:00–06:00 (8, over midnight)
asserting count, both outer bounds, containment and tiling; a spring-forward case pinning that the
user's 05:00 bar is still 03:00Z; the full-resolution `detail`/`summary` window-list **equality** the
old agreement test could not assert; and a per-band `totalSeconds` case for the half-open bucketing.

`Doc impact: none` (the docs already describe the intended behaviour; the code is wrong).

### `CQ-4` 🔴 (PLAUSIBLE) · `baseline: "alltime"` sends `DateTime.MinValue` at a `timestamptz` column

`…/summary/HistorySummarySummaryCardsEndpoint.cs:142` · `…/detail/HistoryDetailSummaryCardsEndpoint.cs:142`

```csharp
"alltime" => (DateTime.MinValue, from, null),
```

`DateTime.MinValue.Kind` is `Unspecified`. `activity_history.start_timestamp` is
`timestamp with time zone` (confirmed in `AppDbContextModelSnapshot.cs:1163-1165`) and the solution does
not enable `Npgsql.EnableLegacyTimestampBehavior`, so Npgsql refuses a non-UTC `DateTime` parameter for
that type: *"Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone',
only UTC is supported."* The request therefore throws — an unhandled 500 on a `baseline` value the
request DTO advertises. Every other arm derives from `from`, which is UTC-kinded by
`WallClockZone.ToUtc`, so only this one is affected.

No test drives any non-default baseline (`TEST-2`), which is why this has survived.

**Fix:** `DateTime.UnixEpoch` (UTC-kinded) — or better, drop the sentinel and omit the lower bound
entirely for `alltime`, which is both correct and lets Postgres use the `(UserId, StartTimestamp)` index
without a useless predicate. Fix in both files (`CQ-2`).

`Doc impact: none`

### `CQ-5` 🟠 (PLAUSIBLE) · The attribution sink bounds its day wrongly, three ways

`application/seam/ActivityHistoryTimeAttributionSink.cs:53-67`

```csharp
var dayStart = day.ToDateTime(TimeOnly.MinValue);
var dayEnd   = day.ToDateTime(TimeOnly.MaxValue);
… h.StartTimestamp >= dayStart && h.StartTimestamp <= dayEnd
```

1. **Kind.** `DateOnly.ToDateTime(TimeOnly)` yields `Unspecified` — the same Npgsql rejection as `CQ-4`,
   on a **live path**: `Tracking.DesktopActivityHeartbeatEndpoint:119` calls this after every heartbeat
   batch to publish `ActivityTimeRecordedEvent`. The sibling seam gets this right one file away
   (`TodoListItemLoggedTimeSource.cs:22` passes `DateTimeKind.Utc`).
2. **Closed range on `TimeOnly.MaxValue.`** `TodoListItemLoggedTimeSource`'s own comment states the rule
   this file breaks: *"A closed range on `TimeOnly.MaxValue` drops whatever lands in the final tick."*
   Two files in one folder, two conventions, one of which the other calls wrong.
3. **UTC day, not the user's day.** The day comes from `DateOnly.FromDateTime(windowStart)` in Tracking
   and is bounded here against UTC midnights, while the slice's Timezone invariant says a day is the
   user's day. For a user at UTC+2 the "today" total silently excludes their first two hours.

**Fix:** mirror `TodoListItemLoggedTimeSource` exactly — `day.ToDateTime(TimeOnly.MinValue,
DateTimeKind.Utc)`, half-open `>= from && < to`. Item 3 is a wider decision (the seam's signature takes
a bare `DateOnly` with no zone); at minimum record which day it means in
`Core/application/seam/IActivityTimeAttributionSink.cs`. `TEST-4`.

`Doc impact: domain-map.md → Navigation index` (the `ActivityHistoryTimeAttributionSink` row should say
which day boundary `TotalSecondsOnDayAsync` uses) + `Core`'s seam XML.

### `CQ-6` 🟡 ✅ FIXED 2026-09-02 · Dead computation in both summary-cards endpoints

Both files: `ComputeBaselineRange` returns a `BaselineTo` that no caller reads (line 50); its
`baselineFrom` parameter is unused (line 161); and `topGroups`'s anonymous type materializes
`Records = g.ToList()` (line 43) which is never touched — one extra list allocation per group per
request. Delete all three.

**Fixed:** all three are gone with the `CQ-2` extraction. `HistoryBaselineWindow.To` is read (it bounds
the baseline query and `allTime`'s day count), the unused `baselineFrom` parameter has no successor, and
`HistorySummaryCardsBuilder`'s `topGroups` carries only the key and the total.

`Doc impact: none`

### `CQ-7` 🟡 · `Baseline` is a magic string with a silent fallback

`application/dto/request/dashboard/summary/HistorySummarySummaryCardsRequest.cs:5` ·
both endpoints' `ComputeBaselineRange` / `ComputeBaselineDays` `switch`

`Baseline` is `string`, defaulted to `"last7days"`, lower-cased at two separate call sites, and matched
in two `switch`es whose `default` arms silently mean `last7days`. A typo (`"last30Days "`, `"allTime"`
after a client refactor) is answered 200 with a wrong-but-plausible baseline — the failure mode this
slice's docs say to design against. The parallel dimension is already an enum (`HistoryGroupBy`).

**Fix:** `HistoryBaseline` enum in `application/dto/enum/`, `IsInEnum()` in the concrete validator, and
the two `switch`es become exhaustive over it. Combine with `CQ-2`.

`Doc impact: domain-map.md → Navigation index` (`HistoryGroupBy` row becomes two enums).

### `CQ-8` 🟡 · No `AsNoTracking()` on any dashboard read

All seven dashboard endpoints plus `CalendarActivityEndpoint` materialize entities (with `Include`s)
into the change tracker for a pure read. The grid gets it right (`…gird:41`), and the two projecting
reads (`time-of-day`, `aggregate-by-activity`) avoid it by projecting. See `PERF-3`.

`Doc impact: none`

### `CQ-9` 🟡 · Sync database call inside an async seeder

`infrastructure/persistence/seeder/dev/ActivityHistorySeeder.cs:25-27` — `dbContext.Set<Activity>()…
.ToList()` inside `async Task SeedForUser`. Dev-only and harmless in practice; `ToListAsync` is one word.

`Doc impact: none`

### `CQ-10` 🟡 · `FilterActivityHistoryEndpoint` overrides `ApplyUserScoping` to do something else

`application/endpoint/query/FilterActivityHistoryEndpoint.cs:21-26`

The override resolves the caller's timezone into a mutable field and returns the query untouched. The
comment explains why (the base awaits this hook and `ApplyCustomFiltering` is synchronous) and the
mutable field is safe — FastEndpoints instantiates an endpoint per request. But a method named
`ApplyUserScoping` that applies no scoping is precisely the trap `domain-map.md` warns about under
*Ownership* ("`ApplyUserScoping` is a no-op virtual — do not treat it as the guard"), and here a reader
sees an override and reasonably concludes scoping *is* handled. Rename the hook in the framework base
(`PrepareAsync` / `OnBeforeFilterAsync`), or leave the override empty and resolve the zone in a
`PreProcessor`.

`Doc impact: none`

### `CQ-11` 🟠 · `filter` bounds on `EndTimestamp`; everything else bounds on `StartTimestamp`

`application/endpoint/query/FilterActivityHistoryEndpoint.cs:32`

```csharp
query.Where(ah => ah.StartTimestamp >= from && ah.EndTimestamp <= to);
```

Every other read in the slice — all seven dashboards, the calendar, the grid, both seams — selects on
`StartTimestamp` alone, and `time-of-day`'s XML documents that choice as contract (*"a record starting
inside the range contributes its tail"*, which is what makes its total equal the pie chart's). The
filter endpoint instead requires the record to *end* inside the range, so a session that runs past the
picked window's end vanishes from the detail list while still counting in every chart above it. It also
defeats the `(UserId, StartTimestamp)` index's usefulness for the upper bound.

**Fix:** `ah.StartTimestamp >= from && ah.StartTimestamp < to`, matching the rest. If the current
behaviour is wanted, it needs to be written down, because it contradicts the equality the dashboards
promise.

`Doc impact: domain-map.md → Endpoints` (state which timestamp `filter` bounds on, whichever way it
lands).

### `CQ-12` 🟡 · `ReplaceSessionAsync` is two transactions and a lost-update window

`application/service/TimerBoundaryAlarmService.cs:50-77`

`CancelSessionAsync` saves (deleting rows), then the new rows are added and saved again. Between the two
saves a concurrent `POST` for the same session — the documented "client re-sends a set it is unsure
about" case — can interleave and leave both sets registered, or delete the other's rows after they were
inserted. The blast radius is small (a duplicate push under the same `Tag`, which the client's own
tag-replacement absorbs) and the fix is not free (an explicit transaction across a scheduler call), so
this is a note rather than a demand. Worth one sentence in the service's remarks so the next reader
doesn't assume atomicity.

`Doc impact: summary.md → Timer-boundary alarms` (one line: replace is not atomic; the tag absorbs it).

---

## 9. Performance hotspots

| Where | Issue | Impact | Mitigation | ID |
|---|---|---|---|---|
| `GetFilteredTableActivityHistoryEndpoint:37-57` | Unpaginated: the caller's whole ledger, `Activity`+`Role`+`Category` joined, materialized and grouped per request | Grows without bound — a two-year user is tens of thousands of rows and a multi-MB JSON body on every grid open | Page it, or bound the range (`CQ-1`) | `PERF-1` |
| Five dashboards (`summary/pie-chart`, `summary/stacked-bars`, both `summary-cards`, `detail/*`) | Whole entity graphs materialized, then grouped in memory, because `IntTime` is behind a converter and `TotalSeconds` will not translate | A `Year` range × three roundtrips (current + baseline + previous) on summary-cards; the joins are read and thrown away except for name/colour | Project instead of `Include`: `Select(ah => new { ah.Length, ah.StartTimestamp, ah.ActivityId, ah.Activity.RoleId, ah.Activity.Role.Name, … })`. `time-of-day` and `aggregate-by-activity` already do exactly this and say why | `PERF-2` |
| All dashboards + calendar | No `AsNoTracking()` — every read builds change-tracker entries and identity-map fixup for entities that are discarded | Allocation and CPU proportional to rows; nothing is ever saved on these paths | Add `AsNoTracking()` (moot for rows that become projections under `PERF-2`) | `PERF-3` |
| `CalendarActivityEndpoint:28-32` | Unbounded date range (`SEC-3`) with two joins, materialized, then grouped and `Take(TopN)` in memory | A decade-wide request is a full table scan of the user's history | Validator cap + project only `(StartTimestamp, Length, Activity.RoleId, Role.Name, Role.Color)` | `PERF-4` |
| `HistorySummarySummaryCardsEndpoint:52,103` (and detail twin) | Three sequential full reads per request; `baseline: "alltime"` is unbounded below | 3× latency; `alltime` reads the entire ledger to divide by a day count | Once `CQ-4` drops the sentinel, `alltime` can be a single grouped aggregate; the `previous` read only needs `Length` (no includes — it already has none, good) | `PERF-5` |
| `TimerBoundaryAlarmRegistrar:43` | `Set<TimerBoundaryAlarm>().ToListAsync()` — every pending alarm for every user, unbatched, at boot | Fine today (the table self-drains, rows live minutes) and correctly indexed on `BoundaryAt`; would be a boot stall only if the self-drain ever broke | Note only. If a retention path is ever added, batch this | `PERF-6` |

### Indexes that should exist

None new. This is one of the better-indexed parts of the solution and the existing four are all
load-bearing, correctly ordered and correctly justified in `ActivityHistoryConfiguration`'s comments:

- `(UserId, ActivityId, StartTimestamp)` UNIQUE serves the aggregate's `(UserId, ActivityId)` predicate
  on its leading two columns, and the sink's contiguous-window probe
  (`UserId, ActivityId, EndTimestamp == windowStart`) on the same prefix — the residual `EndTimestamp`
  filter is over a handful of rows, so a fourth index would not pay for itself.
- `(UserId, StartTimestamp)` serves every dashboard and `mv_activity_history_pattern`; the comment
  correctly explains why the unique index cannot.
- `(TodoListItemId)` / `(RoutineTodoListId)` back the host-side `SET NULL` and the daily recap.
- `TimerBoundaryAlarm`: `(UserId, SessionId)` for cancel, `(BoundaryAt)` for the unscoped boot sweep.

The one thing worth *removing* eventually: the snapshot also shows a standalone
`ix_activity_history_user_id`, fully redundant against both composite indexes' leading column. It is
generated by the `IsManyWithOneUser()` helper, so it is a framework-wide decision, not a History one —
out of scope here, noted for whoever audits index bloat.

---

## Code/doc drift

### `DOC-1` 🟠 · The endpoint table names a route that does not exist

`docs/domain-map.md:87` lists `/activity-history/all-options` → `query/FormSelectOptionsActivityHistoryEndpoint`.

The endpoint derives from `BaseActivityFormSelectOptionsEndpoint`, whose route is
**`form-select-options`** — confirmed by the base's own remarks
(`Core/application/endpoint/base/BaseActivityFormSelectOptionsEndpoint.cs:31`, which names
`/activity-history/form-select-options` explicitly) and by
`HistoryRouteSmokeTests.HistorySelectOptionsRoute_IsRegistered`, whose XML comment already warns that
this is `form-select-options` and *not* the `all-options` of `BaseGetSelectOptionsEndpoint`. The test
suite has the correction written down and the domain map does not.

**The doc is wrong.** Also missing from the row: the `?includeArchived=true` query parameter, which is
the reason that base takes a flag at all and which this route is the motivating case for.

`Doc impact: domain-map.md → Endpoints (the all-options row → form-select-options, + includeArchived)`

### `DOC-2` 🟠 · The Timezone invariant says "every dashboard"; two surfaces fold in UTC

`docs/domain-map.md:194` ("Every dashboard folds and bounds its range in `User.Timezone`, never UTC")
vs `application/endpoint/CalendarActivityEndpoint.cs:23-35` and
`application/endpoint/query/GetFilteredTableActivityHistoryEndpoint.cs:49`.

`CalendarActivityEndpoint` is routed under `/activity-history/dashboard/calendar` and is listed in the
dashboard table, yet it bounds its range at **UTC** midnights (`ToDateTime(TimeOnly.MinValue,
DateTimeKind.Utc)`) and buckets rows by `DateOnly.FromDateTime(ah.StartTimestamp)` — the UTC date. For a
user at UTC+2, an 01:00 local session lands on the previous day's calendar cell; at UTC−5 an evening
session lands on the next one. It never resolves `IUserTimeZoneResolver` at all, and it is the only
dashboard that doesn't inject it. The grid has the same UTC-date grouping at line 49
(`hr.StartTimestamp.ToUniversalTime().Date`), so a user's day breaks in the list view too.

**The code is wrong, on both.** The invariant is right and the rest of the slice already honours it;
`CalendarActivityEndpoint` predates the zone work (it moved in from the host with the Planning
extraction) and was not swept. `HistoryDashboardUserZoneTests` covers four cases, none of them the
calendar or the grid, which is why this survived (`TEST-3`).

Note the knock-on: the days come from `ICalendarDayLookup` keyed by `DateOnly`, so the fix must fold
history into the *user's* date to line up with the calendar rows it is joined against — using UTC on one
side and the user's calendar on the other is what makes the mismatch invisible.

`Doc impact: domain-map.md → Invariants (Timezone)` — once fixed, add the calendar and the grid to
`HistoryDashboardUserZoneTests`' guard row in `docs/testing.md`.

### `DOC-3` 🟡 · The aggregate endpoint's summary describes a pie chart that no longer exists

`application/endpoint/query/AggregateByActivityActivityHistoryEndpoint.cs:10-14`: *"Exists because the
pie-chart dashboard endpoint groups by activity **name**, and names are not unique."*

`domain-map.md` → *Group identity* records that this stopped being true — every group-shaped response
now carries `groupId`, and the section explicitly re-states why the aggregate survives anyway (it spans
all history with no date range, and it omits ids with no rows rather than returning zeros, which is
what lets callers divide by `entryCount` unguarded). **The doc is right and the code comment is stale.**
Replace the class summary with the two real reasons; the current one invites deletion of the endpoint.

`Doc impact: none` (doc is already correct — fix the code comment).

### `DOC-4` 🟡 · `summary.md` implies the alarm endpoints are the only user-facing scheduling surface

`summary.md:79-80` contrasts this slice's endpoint with Reminders' Admin/Root-only register endpoint.
Accurate, but neither timer-alarm endpoint declares `AllowedRoles()` — they inherit the default
User+Admin+Root, which is correct per `CLAUDE.md` and worth one word in the doc so the contrast reads as
a deliberate choice rather than an omission.

`Doc impact: summary.md → Timer-boundary alarms` (one clause).

---

## 🟡 Nits appendix

- `GetFilteredTableActivityHistoryEndpoint:19` — `public virtual string EndpointPath => "gird"` is
  `virtual` with no subclass anywhere. Harmless, but it advertises an extension point that isn't one.
- `HistoryDetailPieChartEndpoint:51-52,61-67,87` retains scaffolding comments (`// Always use 1%
  threshold`, `// No data, return empty list`) that its summary twin already dropped — a small tell for
  which of the two duplicates was edited last, and an argument for `CQ-2`.
- `ActivityHistory.EndTimestamp` is a plain `DateTime` set from `StartTimestamp + Length` in
  `ToEntity` and left *unchanged* by `UpdateEntity` (which writes `StartTimestamp` and `Length`). So an
  edit that shortens a recording leaves a stale `EndTimestamp`, and the attribution sink's
  contiguous-window probe (`EndTimestamp == windowStart`) reads it. Small, real, and the only place the
  three fields can disagree — either recompute it in `UpdateEntity` or make it computed.
- `ScheduleTimerAlarmsValidator:49` — `at.ToUniversalTime()` on an `Unspecified`-kind `DateTime` treats it
  as local server time. The service later force-labels the same value UTC (`SpecifyKind`), so a client
  posting an unsuffixed instant is validated against one interpretation and stored under another. Both
  are wrong for a non-UTC server; require the `Z` (or `DateTimeOffset`) instead.
- `CalendarActivityEndpoint:53` — `Color = g.First().Activity.Role.Color ?? ""` silently converts a null
  colour to empty string here while every other group-shaped response passes `null` through. Minor
  client-side inconsistency.