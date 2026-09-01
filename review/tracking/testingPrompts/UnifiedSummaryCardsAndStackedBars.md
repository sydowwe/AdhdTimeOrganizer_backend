# TEST-5 — `unified/summary-cards` and `unified/stacked-bars` have no test at all

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today:** `TrackingUnifiedDashboardTests` covers four of the six merged dashboards —
`sources`, `pie-chart`, `timeline`, `focus-metrics` — with twelve tests that assert on seconds. The
other two appear in **no** test file, not even the route smoke theory (which lists the fifteen
per-source routes and none of the six unified ones).

## The endpoints

| Route | Source | Request |
|---|---|---|
| `POST /api/activity-tracking/unified/summary-cards` | `endpoint/unified/query/UnifiedSummaryCardsEndpoint.cs` | `UnifiedSummaryCardsRequest` (span + `sources` + `topN` + `baseline`) |
| `POST /api/activity-tracking/unified/stacked-bars` | `endpoint/unified/query/UnifiedStackedBarsEndpoint.cs` | `UnifiedStackedBarsRequest` (span + `sources` + required `windowMinutes`) |

Both bind `UnifiedDashboardRequest`: the usual `dateFrom`/`dateTo` + `from`/`to` (`TimeDto`) plus
`sources`, a non-empty subset of the **string** literals `"webExtension"`, `"desktop"`, `"android"`
(case-sensitive on purpose — `TrackingSourceNames.TryParse:51`).

Both read off `UnifiedSpan` → `UnifiedLedger`, the single rounded ledger, which is what makes the
arithmetic identities below hold by construction rather than by coincidence.

## Scenarios — `unified/stacked-bars`

1. `StackedBars_SumOfABandsSegmentsEqualsTheBandsOwnHeight`
   `UnifiedStackedBarsEndpoint.BuildWindow:76-101` allocates each band's seconds out of **one** pool
   with `SecondsAllocator.Allocate`, precisely so the segments add up after rounding. Seed a minute
   split between two sources so the merge produces fractional seconds (the shape at
   `TrackingUnifiedDashboardTests.PieChart_SplitsAPartiallyOverlappedRunInsteadOfDroppingIt:74` does
   this), then assert `sum(items.activeSeconds + backgroundSeconds)` equals the total the same band
   contributes to the pie for the same request. **This is the identity worth pinning**; without it the
   allocator's negative-shortfall branch is unreached by any test in the repo.
2. `StackedBars_MergeItemsByLabelAcrossSources` — one application seen by the desktop agent
   (`productName: "Slack"`) and the phone (`appLabel: "Slack"`) must be **one** item in a band with
   `sources: ["desktop","android"]`, not two. The cross-source join is
   `UnifiedLabelResolver`; the pie's version of this is pinned at `:158`, the bars' is not.
3. `StackedBars_UseTheSameBandsAsThePerSourceDashboards` — post the same span/window/`windowMinutes`
   to `/unified/stacked-bars` and `/desktop/stacked-bars` with `sources: ["desktop"]` only, and assert
   the `windowStart`/`windowEnd` sequences are identical. The endpoint's own doc says the same client
   component draws both.
4. `StackedBars_OmitEmptyBands` — assert a band with no activity is absent rather than zero-filled
   (the client generates its own empty slots).
5. `StackedBars_RejectAnUnknownWindowMinutes_With400` — the accepted set is
   `15, 20, 30, 60, 90, 120, 240, 480, 1440, 4320, 10080`.

## Scenarios — `unified/summary-cards`

6. `SummaryCards_ReturnTheTopItemsOfTheMergedDay` — two sources overlapping on one minute; assert the
   card's `totalSeconds` for an item equals that item's total in `/unified/pie-chart` for the same
   request. Two surfaces, one ledger — they cannot disagree unless something recomputes.
7. `SummaryCards_MergeTheLookbackTheSameWayAsTheSpan`
   `BuildBaselineAsync:144-147` builds a `DailyWindowSet` for the lookback and re-merges it with the
   same sources — the endpoint's doc says anything else "would compare a de-overlapped figure against
   a double-counted one and report the merge itself as a drop in usage". Seed a lookback week in which
   two sources overlap heavily; assert `averageSeconds` reflects the **merged** total, not the sum of
   the two ledgers.
   (This one is also the control for `CQ-2`: the unified card gets the daily window right and the three
   per-source cards do not — see `SummaryCardsBaselineWindow.md`.)
8. `SummaryCards_ScaleTheMeanDayToTheLengthOfTheSpan` — a 5-day span against a 7-day lookback:
   `averageSeconds == lookbackTotal / 7 * 5`. Mirrors
   `TrackingDashboardDateRangeTests.SummaryCards_ScaleTheBaselineUpToTheLengthOfTheSpan:210`.
9. `SummaryCards_MarkAnItemWithNoLookbackAsNew` — `isNew: true`, `active.averageSeconds` null,
   `percentChange` null.
10. `SummaryCards_ReportNullBackgroundForAnAndroidOnlyItem` — android records no background time, and
    the endpoint returns `null` rather than a structurally-always-zero block (`:86-88`).
11. `SummaryCards_DefaultTopNIsFour` (`DefaultTopN` at `:33`).

## Shared

12. `Dashboards_RejectAnEmptySourceSelection` and `Dashboards_RejectAnUnknownSource` — extend the
    existing parameterised cases at `TrackingUnifiedDashboardTests:296-301` to include these two
    routes rather than writing new ones. Check first: they may already be `[Theory]`-shaped and only
    need two more `InlineData` rows.
13. `Dashboards_AcceptASpan` — neither of these two is single-day (only the timeline is), so a
    multi-day request must answer 200.

## Fixtures / helpers to reuse

- `TrackingUnifiedDashboardTests` — its seeding helpers already build overlapping minutes across the
  three ledgers, which is the expensive part. Add the new cases **to that file**; it owns the merged
  contract and its XML doc is the map.
- Watch the android side: `UnifiedActivityLoader.LoadAndroidAsync` files a session under the day its
  **start** falls in and spreads its whole duration over that day's window, so a session crossing the
  window edge does not lose its overhang. Seed inside the window unless the test is about that rule.
