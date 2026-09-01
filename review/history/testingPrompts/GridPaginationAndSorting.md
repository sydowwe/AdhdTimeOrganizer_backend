# TEST-1 — the grid ignores paging and sorting

> Read `_SHARED-CONTEXT.md` first. Findings: `CQ-1`, `PERF-1`, `SEC-2`, `DOC-2` (UTC date grouping).

## Endpoint

`POST api/activity-history/gird` — `application/endpoint/query/GetFilteredTableActivityHistoryEndpoint.cs`.
**The route really is `gird`.** It is the shipped path (`EndpointPath => "gird"`) and the SPA calls it
that way; do not "fix" it.

## Request

`BaseFilterSortPaginateRequest<ActivityHistoryFilterRequest>`
(`framework/Sydowwe.Framework/application/dto/request/base/table/BaseFilterSortPaginateRequest.cs`):

```jsonc
{
  "useFilter": true,
  "filter": {
    "activityId": null, "roleId": null, "categoryId": null,
    "isFromTodoList": null, "taskPriorityId": null,
    "isFromRoutineTodoList": null, "routineTimePeriodId": null,
    "isUnavoidable": null,
    "dateFrom": "2026-03-01T00:00:00Z", "dateTo": "2026-03-31T00:00:00Z",
    "hoursBack": null,
    "minLength": { "hours": 0, "minutes": 5 }, "maxLength": null
  },
  "sortBy": [ { "property": "StartTimestamp", "ascending": false } ],
  "itemsPerPage": 2,          // required
  "page": 1                   // required
}
```

Check `BaseFilterSortRequest`/`SortByRequest` for the exact wire names of the sort entries before writing
the body — `HistoryCrudAuthMatrixTests`' `GridBody()` posts `sortBy: []` and is the nearest example.

## Response

`List<ActivityHistoryListGroupedByDateResponse>` — an array of `{ date, historyResponseList: [...] }`,
ordered by `date`, each inner list ordered by `startTimestamp`. **Not** the `Items`/`ItemsCount`/`PageCount`
shape the Framework grid base assumes, which is why `BaseGridEndpointTests` is deliberately not used here.

## The gap

`HandleAsync` reads `UseFilter` and `Filter` and nothing else. There is no `Skip`, no `Take`, and
`SortBy` is never applied — `AlwaysSortBy` does not exist on this endpoint, and the only ordering is
`OrderBy(h => h.StartTimestamp)` inside each group plus `OrderBy(r => r.Date)` over the groups. Every
call returns the caller's entire ledger with `Activity` + `Role` + `Category` joined per row. The one
existing test seeds a single row and asserts a count of 1, so it cannot notice.

## Scenarios

### 1. Paging — the headline test

Seed **five** history rows across three distinct dates for the signed-in user. POST with
`itemsPerPage: 2, page: 1`. Count the rows across every group in the response.

Today this returns 5. Write the assertion for what the contract *should* be and expect it to fail.

**Before writing it, settle the contract** — `CQ-1` says the honest options are:

- **(a) page the date groups:** `itemsPerPage: 2, page: 1` returns the first 2 *dates* with all of their
  rows; `page: 2` returns the third date. Then assert group counts, and assert `page: 2` returns the
  complement of `page: 1` with no overlap.
- **(b) drop paging from the request** and bound the range instead: then the correct test is that a
  request with no date bounds is rejected (400) rather than answered with everything.

If the fix is not landing in this change, pin the current behaviour with
`[Trait("Status","KnownGap")]`, assert the 5, and name `CQ-1` in the XML comment. **A pinned wrong
answer is much better than the silence there is now.** Note in the PR that the SPA must ship with
whichever option wins (`MIG-2`).

### 2. Sorting

Seed three rows on one date with known start times. POST `sortBy: [{ property: "StartTimestamp",
ascending: false }]` and assert the inner list is descending. Today it is always ascending. Same
KnownGap treatment if unfixed.

### 3. Filters that *are* wired — lock them in before refactoring

None of these have a value-asserting test today, and the paging fix will touch this method. Each is one
seeded pair and one assertion:

- `activityId`, `roleId`, `categoryId` — narrow to the matching row only.
- `isUnavoidable` — reads through `Activity.IsUnavoidable`.
- `dateFrom` / `dateTo` — both bound on `StartTimestamp`.
- `hoursBack` — `DateTime.UtcNow.AddHours(-n)`; seed one row inside and one outside.
- `minLength` / `maxLength` — `IntTime` comparison, posted as `{ "hours": h, "minutes": m }`.
- `useFilter: false` with a populated `filter` — asserts the filter is genuinely skipped.

`isFromTodoList` / `isFromRoutineTodoList` and their facets already have
`HistoryRouteSmokeTests.Grid_MembershipFilter_NarrowsThroughTheSeam`; don't duplicate it, but do add the
**negative** case (`isFromTodoList: false`) if it isn't there, since it takes a different branch
(`!allIds.Contains(...)`).

### 4. The date grouping is UTC, not the user's zone (`DOC-2`)

`GroupBy(hr => hr.StartTimestamp.ToUniversalTime().Date)` at line 49. Create a user in
`Pacific/Auckland` (UTC+12/13) with a row at `2026-03-10T20:00:00Z` — 09:00 on **11 March** local. Assert
the response groups it under `2026-03-11`. It will come back as `2026-03-10`. This is the same finding as
the calendar dashboard's; see `CalendarDashboardBounds.md`, and fix or pin both together.

### 5. Error handling leaks exception text (`SEC-2`)

Lines 61-65 catch everything and return `ex.Message` in the error body. Post a `dateFrom` **without a
`Z`** (`"2026-03-01T00:00:00"`, i.e. `Kind = Unspecified`) — against a `timestamptz` column this is
expected to throw inside Npgsql, and the endpoint will hand you its message. Assert the response body
does **not** contain `"activity_history"`, `"Npgsql"` or `"DateTime"`. That test fails today and passes
once the `catch` is narrowed to log-and-return-a-fixed-message.

### 6. Auth / IDOR

Already covered by `HistoryCrudAuthMatrixTests.GetFilteredTable_ExcludesOtherUsersHistory`. Do not
duplicate it — but if you seed a second user for scenario 1's volume, re-assert the exclusion holds with
more than one row present, which the existing test cannot show.

## Where to put it

New file `AdhdTimeOrganizer.IntegrationTests/Endpoints/HistoryGridContractTests.cs`. Leave
`ActivityHistoryGetFilteredTableTests` in `HistoryCrudAuthMatrixTests.cs` alone — it owns the scoping
question and says so.
