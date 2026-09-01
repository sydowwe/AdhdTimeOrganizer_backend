# TEST-3 — the three summary-cards baselines: `AllTime` 500s, the window is ignored, android folds on UTC

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today:** `TrackingDashboardDateRangeTests.SummaryCards_ScaleTheBaselineUpToTheLengthOfTheSpan:210`
pins the *scaling* half of the baseline on the desktop route, and
`TrackingDashboardUserZoneTests.SummaryCards_MatchTheSameWeekdayBaselineOnTheUsersCalendar_NotUtcs:106`
pins the weekday fold, also on the desktop route. Three bugs sit outside both: `CQ-1` (`AllTime`),
`CQ-2` (the lookback ignores the daily window) and `CQ-3` (android folds on UTC).

## The endpoints

| Route | Source | Baseline code |
|---|---|---|
| `POST /api/activity-tracking/desktop/summary-cards` | `endpoint/desktop/query/dashboard/DesktopSummaryCardsEndpoint.cs` | `:89-149` |
| `POST /api/activity-tracking/web-extension/summary-cards` | `endpoint/webExtension/query/WebExtensionSummaryCardsEndpoint.cs` | `:99-190` |
| `POST /api/activity-tracking/android/summary-cards` | `endpoint/android/query/AndroidSummaryCardsEndpoint.cs` | `:89-138` |

All three bind `SummaryCardsRequest` (span + `topN` + `baseline`) and validate with
`TopDomainsValidator`. `BaselineType` is `Last7Days=0, Last30Days=1, SameWeekday=2, AllTime=3`; send
the integer (the suite's `JsonOpts` has no string-enum converter).

Response is a list of per-item cards, each with `active` / `background` blocks carrying
`seconds`, `averageSeconds` and `percentChange`, plus `isNew` when the item has no baseline.

## Scenarios

### 1. `AllTime` must not 500 — finding `CQ-1`

`SummaryCards_WithAllTimeBaseline_ReturnsOk`, parameterised over all three routes.

Seed a handful of rows, post with `baseline: 3`, assert `OK` and a sane `averageSeconds`.

⚠ **This test will pass in the suite whether or not the bug is fixed, and that is the finding.** All
three endpoints pass `DateTime.MinValue` (`Kind == Unspecified`) as the lower bound of the baseline
read; `window_start` / `session_start_utc` are `timestamp with time zone`, and Npgsql refuses an
`Unspecified` value there — **in production**. The test fixture sets
`AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)`
(`framework/Sydowwe.Framework.Testing/PostgresContainerFixture.cs:26`), which makes `Unspecified` mean
UTC, so the integration suite cannot see it.

So write the test **and** do one of:

- fix `CQ-1` (replace `DateTime.MinValue` with `currentFrom.AddDays(-DashboardDateRangeRules.MaxSpanDays)`,
  matching `BaseFocusMetricsEndpoint.BuildBaselineAsync:126`) — then the test guards the fix; **or**
- add a plain unit assertion next to it that the floor passed to the query has
  `Kind == DateTimeKind.Utc`, which is the only thing the suite *can* prove. State in the XML doc that
  the HTTP case is green for the wrong reason until `MIG-5` removes the legacy switch.

### 2. The baseline must honour the per-day window — finding `CQ-2`

`SummaryCards_MeasureTheBaselineOverTheSameTimeOfDayWindowAsTheSpan`, per source.

Shape (desktop; the same shape works for all three):

- User timezone `Europe/Bratislava` (copy `SeedAsync` from `TrackingDashboardUserZoneTests:40-45`).
- Request: a single day `D`, window `from = 09:00`, `to = 17:00`, `baseline: 0` (last 7 days).
- Seed, for each of the seven days before `D`: **600 s inside** the 09:00–17:00 window and **6000 s at
  02:00**, which the window excludes.
- Seed 600 s on `D` inside the window.
- Assert `percentChange == 0` (or `averageSeconds == 600`).

**This fails today** — the current period is filtered through `windows.Restrict(...)` while the
baseline read is a bare envelope predicate that never is, so `averageSeconds` comes back as 6600 and
the card reports roughly −91% on a week where nothing changed.

The comparison test that makes it undeniable: post the identical span and window to
`/focus-metrics`, whose `BaseFocusMetricsEndpoint.BuildBaselineAsync:146-150` **does** build a
`DailyWindowSet` for the lookback. `domain-map.md:138-140` states the two must agree; assert that they
do. That equality-between-two-surfaces is the strongest form available here.

### 3. Android must fold the weekday on the user's clock — finding `CQ-3`

`SummaryCards_MatchTheSameWeekdayBaselineOnTheUsersCalendar_NotUtcs` already exists for desktop at
`TrackingDashboardUserZoneTests:106`. **Extend it into a `[Theory]` over all three routes**, or add
the android case beside it.

`AndroidSummaryCardsEndpoint.cs:118,124-126` reads `from.DayOfWeek`, `x.SessionStartUtc.DayOfWeek` and
`.Date` straight off UTC while its two siblings route the same code through
`WallClockZone.FromUtc(..., timeZone)`. Seed a session at **00:30 Bratislava time** (22:30Z the
previous day) so the two readings disagree about which weekday it is, request `baseline: 2`
(`SameWeekday`), and assert the session is counted.

### 4. Worth adding while you are here

- `SummaryCards_DefaultTopN` — desktop and web-extension default to 5, android to 4
  (`AndroidSummaryCardsEndpoint.cs:51`). Pin whichever is intended; the inconsistency is `CQ-23`.
- `SummaryCards_MarkAnItemWithNoBaselineAsNew` — `isNew: true` and both `averageSeconds` /
  `percentChange` null.

## Fixtures / helpers to reuse

- `TrackingDashboardDateRangeTests` for the seeding shape and body construction; its
  `SummaryCards_ScaleTheBaselineUpToTheLengthOfTheSpan:210` is the closest existing case.
- `TrackingDashboardUserZoneTests:40-45` for the non-UTC user, and note its comment about picking a
  band width that does not divide evenly into the offset — the same trap applies to any instant you
  choose near a boundary.
- Seed `DesktopActivityEntry` / `WebExtensionActivityEntry` without setting `RecordDate` (private
  setter, derived from `WindowStart`), all instants `DateTimeKind.Utc`.
