# B3 — backend reply: `CustomRange` now works on all four `summary/` endpoints

Backend change is merged. Response shapes are unchanged; no field was added or removed. This is
what changed behind them, and what the frontend needs to do about it.

## Correction to the ticket's premise — check this first

`CustomRange` was **not a valid `rangeType`**. The backend enum had six members (`ThreeDays`, `Week`,
`TwoWeeks`, `Month`, `ThreeMonths`, `Year`) and no seventh. A request sending
`"rangeType": "CustomRange"` was rejected at JSON binding:

    400 {"statusCode":400,"errors":{"rangeType":["The JSON value could not be converted to ...
        ActivityDateRangeType"]}}

So the four endpoints were never silently answering a two-day range — they were refusing the request
outright. The two-day fallback described in the ticket was real code on an unreachable branch, and it
would have produced exactly the described bug the moment the enum member was added.

**Action: find out what `HistoryDateRangeSelector.vue` actually sends when a user picks a custom
range today, and what happens to that 400.** Either the picker sends something else (a named range?
a numeric enum?), or those requests have been failing and the failure is being swallowed somewhere.
Whichever it is, it is a second bug that this backend change does not fix. Report back what you find.

## The contract now

Endpoints (all `POST`, all under `/api`):

- `/activity-history/dashboard/summary/stacked-bars`
- `/activity-history/dashboard/summary/pie-chart`
- `/activity-history/dashboard/summary/summary-cards`
- `/activity-history/dashboard/summary/time-of-day`

Range fields, identical on all four:

    { "date": "2026-03-01", "rangeType": "CustomRange", "endDate": "2026-03-31" }

1. **`rangeType: "CustomRange"` is valid.** Serialized as the member name, exactly that spelling.
2. **`endDate` is inclusive.** Send the last day the user picked. Do **not** send `endDate + 1`.
   1–31 March is 31 days. `date == endDate` is one day, not two.
3. **Only `CustomRange` reads `endDate`.** Every other `rangeType` ignores it — it does not move the
   bounds and does not 400. Keeping a stale `endDate` in the URL across a range-type change is safe,
   and that is pinned by a backend test rather than being accidental.
4. **Dates are the user's own calendar days**, resolved in their `User.Timezone` server-side. Send
   plain `YYYY-MM-DD`; do not convert to UTC.
5. **Max span: 366 days, counted inclusively** (a full calendar year, leap day included). So
   `endDate <= date + 365 days`. Longer is a 400.

## The 400s, and what to guard before sending

Three rejections, all keyed on `endDate`:

    400 {"statusCode":400,"message":"One or more errors occurred!",
         "errors":{"endDate":["EndDate is required when RangeType is CustomRange"]}}
    400 {... "errors":{"endDate":["EndDate must be greater than or equal to Date"]}}
    400 {... "errors":{"endDate":["A custom range may span at most 366 days"]}}

Guard all three in the picker before sending, so the user gets a field-level message rather than a
raw error: `endDate` present, `endDate >= date`, and the inclusive span `<= 366` days. An inverted
range reports as inverted, never as "too long", so you can map each message to its own hint.

Errors are keyed on the wire field name (`endDate`, `date`, `rangeType`), so a generic
`errors[fieldName]` mapping works.

## What this turns on, with no frontend change

- **The time-of-day insight (H10) starts working on custom ranges.** Both reasons it stayed off were
  consequences of the bug: `daysInRange` read `2`, and the `daysWithActivity >= 4` threshold could
  not be met inside a two-day range. Both are now correct. Expect the insight to begin appearing on
  `CustomRange` — verify it reads sensibly rather than assuming it stays hidden.
- **`useHistoryInsights.test.ts` — "ignores daysInRange, which B3 leaves wrong on a custom range"**:
  the premise in that test's name is now stale. If the independence from `daysInRange` is a genuine
  design property, keep the assertion and rewrite the name and comment to say why it holds on its own
  merits. If it was only ever a workaround for this bug, delete it.
- **The two per-group insights and the period mean** are derived from `summary/pie-chart`, so they
  inherit the fix. They are correct on `CustomRange` for the first time — worth eyeballing against a
  known month of data rather than trusting them sight unseen.

## Please verify and report back

1. What the range selector sends for a custom range today, and where the current 400 goes.
2. That a bookmarked/shared custom-range URL round-trips: `endDate` survives, and the rendered range
   matches the header.
3. That the three shipped panels and the time-of-day panel agree with each other on the same custom
   range — `sum(hours[].totalSeconds)` from `time-of-day` equals `totals.totalSeconds` from
   `pie-chart` for the same range, and that equality is a backend guarantee you may assert on.
