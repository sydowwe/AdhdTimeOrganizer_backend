# TEST-10 — the two details endpoints: 404 shape, cross-user, window mask, missing range cap

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today:**
`TrackingDashboardDateRangeTests.ProcessDetails_ApplyTheDailyWindowToTheEnvelopeItIsOpenedWith:294`
covers the window mask on the desktop endpoint — the hardest part, and it is done. Nothing else about
either endpoint is covered, and `GET /web-extension/domain-details` has **no test at all**.

## The endpoints

| Route | Source | Validator |
|---|---|---|
| `GET /api/activity-tracking/desktop/process-details` | `endpoint/desktop/query/dashboard/DesktopProcessDetailsEndpoint.cs` | `validator/desktop/DesktopProcessDetailsValidator.cs` |
| `GET /api/activity-tracking/web-extension/domain-details` | `endpoint/webExtension/query/WebDomainDetailsEndpoint.cs` | `validator/DomainDetailsValidator.cs` |

Both are **GET with query parameters** (`[QueryParam]` on `DailyWindowMaskRequest`), unlike every
dashboard in the module:

```
?from=2026-06-15T00:00:00Z&to=2026-06-16T00:00:00Z&processName=code.exe
   [&windowStartMinutes=540&windowEndMinutes=1020]
```

`from`/`to` are an **instant envelope**, not a day span — the caller opens the panel from a chart and
hands it the outer bounds of whatever span is on screen. `windowStartMinutes`/`windowEndMinutes` carry
the per-day time-of-day window across, as minutes past midnight on the user's clock; they are optional
but **both or neither** (`DashboardDateRangeRules.ApplyDailyWindowMaskRules`).

Responses: `DesktopProcessDetailsResponse` (totals + `monitorBreakdown` + `windowTitles`) and
`DomainDetailsResponse` (totals + `pages`, each with a `path` extracted from the URL). Both answer
**404** with `AddError("… not found.")` when the filtered set is empty.

## Scenarios

Mirror each case across both endpoints — they are the same endpoint twice with different nouns, and
the web-extension one has drifted (it has a 31-day cap; the desktop one has none).

1. `Details_ReturnTheBreakdownForTheNamedItem` — seed rows for two processes/domains, request one,
   assert the totals are that one's only, and that `entries` equals the row count.
2. `Details_GroupTheSecondaryList` — desktop: three rows across two window titles → two
   `windowTitles` entries ordered by seconds descending, with `fullscreenSeconds` / `soundSeconds`
   populated only from rows carrying those flags. Web-extension: the same over `pages`.
3. `DomainDetails_ExtractThePath` — seed `https://github.com/user/repo?tab=x`; assert `path` is
   `/user/repo?tab=x`. Then a >100-character path truncated to 97 + `"..."`, and a malformed URL
   (`"not a url"`) falling back to the raw string rather than throwing (`ExtractPath:87-108`).
4. `Details_ReturnNotFoundForAnItemWithNoRows` — assert **404** and that the body carries the error,
   not an empty 200. Both endpoints document a 404 in their `Summary` and nothing pins it.
5. `Details_DoNotSeeAnotherUsersRows` — seed the *same* process name / domain for a second user with
   plenty of seconds, and assert either a 404 (if the caller has none) or totals that exclude them.
   This is the only IDOR-shaped assertion available on a read endpoint in this module, and these two
   are the ones that take an arbitrary string identifier from the caller.
6. `Details_ApplyTheDailyWindowMask` — already done for desktop at
   `TrackingDashboardDateRangeTests:294`; **add the web-extension twin**. Seed 600 s inside a
   09:00–17:00 window and 6000 s at 02:00 across a multi-day envelope, pass
   `windowStartMinutes=540&windowEndMinutes=1020`, assert 600.
7. `Details_RejectHalfAWindowMask_With400` — `windowStartMinutes` without `windowEndMinutes`. The rule
   is explicit ("both or neither — one bound without the other has no reading") and untested.
8. `Details_TakeTheWholeEnvelopeWhenNoMaskIsSent` — the same seed as 6 without the mask returns 6600.
   This is the case the mask exists to fix; asserting both directions is what makes 6 meaningful.
9. `DomainDetails_RejectARangeLongerThan31Days_With400` — `DomainDetailsValidator.cs:13`.
10. `ProcessDetails_RejectAnUnboundedRange_With400` — **finding `PERF-3`, fails today**:
    `DesktopProcessDetailsValidator` checks only `To > From`, so a ten-year envelope is accepted and
    reads the partitioned desktop ledger unbounded, filtered only by process name (with no index for
    it — see the index list in `02-findings.md`). Either add the same 31-day cap and let the test pass,
    or pin the current acceptance under `[Trait("Status", "KnownGap")]` naming `PERF-3`.
11. `Details_RejectAnEmptyIdentifier_With400` — empty `processName` / `domain`.

## Fixtures / helpers to reuse

- `TrackingDashboardDateRangeTests:294` — the closest existing case, and it already builds the query
  string correctly. Copy its URL construction; these are the only two GET endpoints among the 21
  dashboards and the binding is easy to get wrong.
- Non-UTC user (`TrackingDashboardUserZoneTests:40-45`) for anything touching the mask — the minutes
  are on the user's clock, and with a UTC user a correct fold is indistinguishable from a missing one.
- Seed instants `DateTimeKind.Utc`, minute-aligned; never set `RecordDate`.
- Keep web-extension seeds inside the last two years — `WebExtensionActivityEntry` carries a
  `RecordDate >= CurrentPartitionDate` (UtcNow − 2y) filter that applies to fixture-built contexts too.
