# TEST-7 — `detail/stacked-bars` emits 24 windows regardless of the picked range

> Read `_SHARED-CONTEXT.md` first. Finding: `CQ-3`.

## Endpoint

`POST api/activity-history/dashboard/detail/stacked-bars` —
`application/endpoint/query/dashboard/detail/HistoryDetailStackedBarsEndpoint.cs`.

## The gap

```csharp
private static List<(DateTime Start, DateTime End)> GenerateWindows(DateTime from, DateTime to)
{
    for (var h = 0; h < 24; h++) { var start = from.AddHours(h); windows.Add((start, start.AddHours(1))); }
}
```

`to` is accepted and never read. A detail request picks a date **and** a time window
(`DateAndTimeRangeDto` → `from`/`to` as `TimeDto`), so a user asking for 09:00–17:00 gets 24 bars, 16 of
which sit outside the window they picked and are always empty.

The summary sibling (`HistorySummaryStackedBarsEndpoint.GenerateFixedWindows`) does this correctly and
says why in a comment: it walks calendar dates and clips each band at `dayEnd`, resolving every boundary
through the user's zone rather than adding hours to an instant — because on a DST transition day the
user's 08:00 is a different number of hours after the previous 08:00. The detail side does the thing that
comment describes having fixed.

`HistoryDashboardUserZoneTests.SummaryAndDetailStackedBars_AgreeOnTheInstantsAPickedWindowNames` passes
today because it only compares the windows that overlap.

## Request / response

```jsonc
// HistoryDetailStackedBarsRequest : HistoryDetailDateRangeRequest (DateAndTimeRangeDto)
{ "date": "2026-03-10",
  "from": { "hours": 9,  "minutes": 0 },
  "to":   { "hours": 17, "minutes": 0 },
  "groupBy": 0 }
```

```jsonc
// HistoryStackedBarsResponse
{ "windows": [ { "windowStart": "...", "windowEnd": "...",
                 "items": [ { "groupId": 12, "name": "Reading", "totalSeconds": 1800, "color": "#aabbcc" } ] } ] }
```

Confirm the exact wire names of `DateAndTimeRangeDto`'s time fields before writing the body —
`ActivityHistoryFilterTests.EmptyFilterPayload()` in `HistoryCrudAuthMatrixTests.cs` posts this shape and
is the nearest working example.

## Scenarios

### 1. The window count matches the picked range

Request 09:00–17:00 on one date. Assert `windows.Count == 8` and that
`windows.First().windowStart` is the user's 09:00 and `windows.Last().windowEnd` is the user's 17:00.
Today you get 24 windows running to 09:00 the next day.

Then request a full day (00:00–23:59) and assert 24 windows — so the fix does not simply cap at the
wrong place.

### 2. No bar lies outside the range

For any request, assert every `windowStart >= from` and `windowEnd <= to`. This is the invariant-shaped
version of scenario 1 and it survives whatever the fix does to the edge arithmetic.

### 3. Over-midnight windows

`DateAndTimeRangeDto.ToDateTimeRange` treats `to <= from` as a band running past midnight into the next
date — the summary side mirrors this in `GenerateFixedWindows` (`endTime <= startTime` → the next day's
`endTime`). Request 22:00–06:00 and assert 8 windows spanning the user's 22:00 to 06:00 the following
day, with a row seeded at 23:30 landing in the right one.

### 4. Detail and summary agree, at full resolution

Extend the existing agreement test rather than replacing it: request the same picked window from
`detail/stacked-bars` and from `summary/stacked-bars` (with `windowMinutes: 60` and the same
`windowStartTime`/`windowEndTime`), and assert the two window lists are **equal** — same count, same
instants — not merely that the overlapping ones match. That is the assertion that could not have passed
while `CQ-3` was live.

### 5. DST

Use a `Europe/Bratislava` user and the spring-forward date (2026-03-29, 02:00→03:00 local). Request
00:00–06:00. Assert the bars line up with the user's wall clock — the local 05:00 bar covers the same
wall-clock hour it does on any other day — rather than drifting by the offset change. The summary side
already has a DST test (`DetailWindow_StartingInsideTheSpringForwardGap_ResolvesToTheEndOfTheGap`); this
is its detail-side counterpart.

### 6. Values, not just shape

Seed two activities with known durations inside two different hours and assert the per-window
`items[].totalSeconds`, so the test also covers that records are bucketed by `StartTimestamp` (`>= w.Start
&& < w.End`) — the same half-open rule as everywhere else in the slice.

## Where to put it

Add to `AdhdTimeOrganizer.IntegrationTests/Endpoints/HistoryDashboardUserZoneTests.cs` — scenarios 4 and
5 belong beside its existing agreement and DST tests, and that suite already builds non-UTC users. If it
grows past ~350 lines, split the window-shape scenarios (1-3) into
`HistoryStackedBarsWindowTests.cs` instead.
