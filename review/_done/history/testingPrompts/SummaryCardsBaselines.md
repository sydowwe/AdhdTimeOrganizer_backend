# TEST-2 — `summary-cards` baselines (both detail and summary)

> Read `_SHARED-CONTEXT.md` first. Findings: `CQ-4` (suspected 500 on `alltime`), `CQ-6`, `CQ-7`.

## Endpoints

| Route | File |
|---|---|
| `POST api/activity-history/dashboard/summary/summary-cards` | `application/endpoint/query/dashboard/summary/HistorySummarySummaryCardsEndpoint.cs` |
| `POST api/activity-history/dashboard/detail/summary-cards` | `application/endpoint/query/dashboard/detail/HistoryDetailSummaryCardsEndpoint.cs` |

The two files are byte-identical below their range resolution (`CQ-2`), so **every scenario here must be
run against both** — a `[Theory]` over the two routes is the right shape, and it is what makes the
duplication visible if one is fixed and the other is not. `HistoryRouteSmokeTests` already parameterises
routes this way.

## Request shape

`HistorySummarySummaryCardsRequest : HistorySummaryDateRangeRequest` (→ `DateRangeDto`):

```jsonc
{
  "date": "2026-03-10",            // DateOnly, required
  "rangeType": 6,                  // ActivityDateRangeType; CustomRange is the one that reads endDate
  "endDate": "2026-03-12",         // required only for CustomRange
  "groupBy": 0,                    // HistoryGroupBy: Activity | Role | Category
  "baseline": "last7days",         // ← the untested dimension
  "topN": 4
}
```

The detail request is `HistoryDetailSummaryCardsRequest : HistoryDetailDateRangeRequest`
(`DateAndTimeRangeDto` — a single `date` plus a `from`/`to` `TimeDto` window). Check the two request
files for the exact wire names before writing the bodies; the summary one is validated by
`HistorySummarySummaryCardsRequestValidator`, the detail one by nothing.

## Response shape

```jsonc
{
  "cards": [ { "groupId": 12, "name": "Reading", "color": "#aabbcc",
               "totalSeconds": 5400, "averageSeconds": 900,
               "percentChange": 12.5, "isNew": false } ],
  "periodComparison": { "previousPeriodTotalSeconds": 0, "currentPeriodTotalSeconds": 5400, "percentChange": null }
}
```

`averageSeconds` is the **baseline's** daily average (not the current period's), `percentChange` compares
the current daily average against it, and `isNew` is `baselineTotal == 0 && currentTotal > 0`.

## Scenarios

### 1. ⚠️ `baseline: "alltime"` — the finding

`ComputeBaselineRange` returns `DateTime.MinValue` for this arm
(`HistorySummarySummaryCardsEndpoint.cs:142` and the detail twin's line 142). `DateTime.MinValue.Kind`
is `Unspecified`; `activity_history.start_timestamp` is `timestamp with time zone`; the solution does not
set `Npgsql.EnableLegacyTimestampBehavior`. Npgsql is expected to throw *"Cannot write DateTime with
Kind=Unspecified to PostgreSQL type 'timestamp with time zone', only UTC is supported."*

Write the test that settles it: seed one activity with rows both inside the requested range and well
before it, POST with `baseline: "alltime"`, and assert **200 plus a correct `averageSeconds`**.

- If it 500s, the finding is confirmed. Fix the source (`DateTime.UnixEpoch`, or better: drop the lower
  bound entirely for `alltime` so Postgres can use `(UserId, StartTimestamp)` without a dead predicate)
  in **both** files, then keep this test green.
- If it passes as-is, record that in the PR — `CQ-4` is then void.

### 2. Every documented baseline value produces a defensible number

`[Theory]` over `"last7days"`, `"last30days"`, `"sameWeekday"`, `"alltime"`. Seed a fixed history and
assert `averageSeconds` per baseline against a hand-computed value. The divisors are hard-coded in
`ComputeBaselineDays`: 7, 30, `8 × (distinct weekdays in the range)`, and for `alltime` the whole-day
span from the earliest baseline row to the range start (minimum 1). Do the arithmetic in the test's
comment so a future reader can check it without re-reading the endpoint.

### 3. An unknown baseline value falls through to `last7days` silently (`CQ-7`)

POST `baseline: "not-a-baseline"` and assert the response equals the `"last7days"` response byte for
byte. This is the current behaviour and it is wrong — a typo answers 200 with a plausible wrong number.
Pin it with `[Trait("Status","KnownGap")]` and an XML comment naming `CQ-7`, **or** implement the
`HistoryBaseline` enum + `IsInEnum()` and assert a 400 instead. State which you did.

### 4. `sameWeekday` actually filters weekdays

Seed rows on Mondays and Tuesdays across the eight weeks before a range that covers a single **Monday**.
Assert the baseline total counts only the Monday rows. The weekday set comes from
`GetWeekdaysInRange(dateFrom, dateTo)`; note that the **detail** endpoint derives those dates from the
*UTC* instants (`HistoryDetailSummaryCardsEndpoint.cs:48-49`) rather than from the user's calendar, so
run this scenario with a non-UTC user and check whether the two endpoints agree. If they don't, that is a
new finding — file it.

### 5. `isNew` and `percentChange` are mutually exclusive

Seed a group with rows only in the current period. Assert `isNew: true` and `percentChange: null`. Then
seed the same group in the baseline period too and assert `isNew: false` with a computed
`percentChange`. `percentChange` is also null when `baselineDailyAvg` is 0 — cover that (a baseline
total smaller than `baselineDays` integer-divides to 0, which is a real and slightly surprising case).

### 6. `topN` truncates by total, not by name

Seed five groups with distinct totals, request `topN: 2`, assert the two largest by `totalSeconds` in
descending order. Then request `topN: 0` and assert the behaviour is deliberate (it is passed straight
to `Take`, so it yields an empty `cards` list while `periodComparison` still totals the range — decide
whether that is the contract and write it down).

### 7. `periodComparison` looks one whole period back

`previousFrom = from.AddDays(-dayCount)`. Seed a 3-day range with 3600s and the immediately preceding
3 days with 1800s; assert `previousPeriodTotalSeconds: 1800`, `currentPeriodTotalSeconds: 3600`,
`percentChange: 100`. Note this window is computed by *day arithmetic on a UTC instant*, so with a
non-UTC user it can straddle the user's midnight differently from `ToUtcRange` — worth one assertion
with a non-UTC user.

### 8. Auth / IDOR

Seed a second user with history in the same range and the same activity name; assert their seconds appear
in neither `cards` nor `periodComparison`. Both endpoints filter `ah.UserId == userId` by hand *and* sit
behind the global filter — this proves it end to end.

## Where to put it

New file `AdhdTimeOrganizer.IntegrationTests/Endpoints/HistorySummaryCardsBaselineTests.cs`. Do not add
to `HistorySummaryCustomRangeTests` — that suite is about the range dimension and is already at 345 lines.
