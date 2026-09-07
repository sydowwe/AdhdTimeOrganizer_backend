# TEST-9 — the four hand-written read endpoints are covered by a route ping and nothing else

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today:** `TrackingRouteSmokeTests` posts an empty-filter body to `/desktop/gird` and
`/android/gird` and GETs `/desktop/distinct-processes`, asserting `OK`. That is the whole of it, and
`useFilter: false` means none of the three ever reaches its filtering code.
`/desktop/fetch-categories-and-roles-by-pattern` has **no test at all**.

## The endpoints

| Route | Source | Notes |
|---|---|---|
| `POST /api/activity-tracking/desktop/gird` | `endpoint/desktop/query/FetchTableDistinctDesktopEntry.cs` | `gird` is the shipped spelling. Groups the desktop ledger by `(ProductName, ProcessName, WindowTitle)` |
| `POST /api/activity-tracking/android/gird` | `endpoint/android/query/FetchTableDistinctAndroidEntry.cs` | Groups `AndroidSessionData` by `(PackageName, AppLabel)` |
| `GET /api/activity-tracking/desktop/distinct-processes` | `endpoint/desktop/query/DesktopDistinctProcessesEndpoint.cs` | `List<TitleValueObject>`, no paging |
| `POST /api/activity-tracking/desktop/fetch-categories-and-roles-by-pattern` | `endpoint/desktop/query/FetchCategoriesAndRolesByPattern.cs` | Distinct categories/roles across the caller's **mappings** |

The three `POST`s bind `BaseFilterSortPaginateRequest<TFilter>` — `useFilter`, `filter`, `sortBy`,
`itemsPerPage`, `page`. Copy `GridBody()` from `TrackingRouteSmokeTests:328-335` and set
`useFilter: true` with a real filter object for the filtering cases.

Filters (`TrackerDesktopDistinctEntriesFilter`, `AndroidDistinctEntriesFilter`) carry a value plus a
`PatternMatchType` per field; **both are all-nullable**, so `filter: {}` is legal here (unlike the two
mapping grids, whose `Type` is `required`).

## Scenarios

### User scoping — the one that matters

1. `Grid_ExcludesOtherUsersEntries` for both `gird` routes, and
   `DistinctProcesses_ExcludesOtherUsersProcesses`, and
   `CategoriesAndRoles_ExcludeOtherUsersMappings`.
   Seed rows for a second user, call as the test user, assert their values are absent. All four scope
   by hand — `query.FilteredByUser(User.GetId())` — over entities that are *also* covered by the global
   filter, so the assertion is cheap and the failure mode (an editor deletes the "redundant" line) is
   silent. Copy the shape from `TrackerDesktopMappingGridTests.Grid_ExcludesOtherUsersMappings:409`.

### Distinctness and shape

2. `Grid_CollapsesRepeatedEntriesIntoOneRow` — seed five desktop rows across different windows sharing
   `(productName, processName, windowTitle)`, plus one differing only in `windowTitle`. Assert
   `itemsCount == 2` and that the returned `id` is the **lowest** underlying id (`g.Min(e => e.Id)`),
   which is what the client uses as a row key.
3. `DistinctProcesses_UseTheFirstNonEmptyProductNameAsTheTitle` — seed one row with an empty
   `productName` and one with a real one for the same process. Assert the title is the real one, not
   `""` and not the process name (`DesktopDistinctProcessesEndpoint.cs:33`).
4. `CategoriesAndRoles_ReturnEachOptionOnce` — seed three mappings sharing one category and two roles;
   assert one category option and two role options, with names populated.

### Pagination — finding `CQ-14`

5. `Grid_PaginatesAndReportsPageCount` — 7 distinct entries, `itemsPerPage: 3`, `page: 2` → 3 items and
   `pageCount == 3`.
6. `Grid_RejectsAZeroItemsPerPage_With400`
   **Fails today**: these two endpoints have **no validator** (the two mapping grids validate
   `ItemsPerPage ∈ [1,200]` via `FetchTableTrackerDesktopMappingValidator`), so `itemsPerPage: 0`
   reaches `(int)Math.Ceiling(itemsCount / 0.0)` and returns a garbage `pageCount`. Assert the current
   behaviour under `[Trait("Status", "KnownGap")]` naming `CQ-14`, or add the validator and let the
   test pass.
7. `Grid_RejectsAnExcessiveItemsPerPage_With400` — same gap, upper end.

### Filter modes — finding `CQ-12`

8. `Grid_FiltersByExactAndContains` — `useFilter: true` with
   `{ processName: "code", processNameMatchType: 1 }` (`Contains`). Assert only matching rows return.
   `PatternMatchType` is `Exact = 0, Contains = 1, Wildcard = 2, Regex = 3` — confirm against
   `Sydowwe.Framework.domain.@enum.PatternMatchType` before hard-coding the integers.
9. `Grid_TreatsRegexModeAsSqlLike` — **`[Trait("Status", "KnownGap")]`, finding `CQ-12`.**
   Filter with `processNameMatchType: Regex` and pattern `.*` and assert **nothing** matches, then with
   `%` and assert **everything** does. That is the current behaviour:
   `ApplyStringMatchFilter` (`framework/…/QueryableEntityExtensions.cs:170`) maps `Regex` to
   `EF.Functions.Like` with the pattern verbatim — while the same enum member means a genuine
   `Regex.IsMatch` at ingest. Pinning it is what makes the divergence visible; invert when the
   framework helper is fixed.

### Error handling — finding `CQ-13`

10. Optional: `Grid_DoesNotSwallowCancellation`. All three `POST`s wrap the handler in
    `catch (Exception) → 500`, which turns a cancelled request into a logged error and a 500. Hard to
    assert reliably over HTTP; if it proves awkward, skip it and leave `CQ-13` to the code fix rather
    than writing a flaky test.

## Fixtures / helpers to reuse

- `TrackerDesktopMappingGridTests` / `TrackerAndroidMappingGridTests` in
  `TrackingMappingCrudAuthMatrixTests.cs:391-475` — closest existing shape, including the 401 case
  (`CreateClient()`), which these four also lack.
- Seed ledger rows with distinct `windowStart` values per row to stay clear of the unique indexes.
- Note there is **no date bound** on either `gird` query (`PERF-4`), so anything the fixture seeded for
  earlier tests in the same collection is visible. Use distinctive process/package names and assert on
  presence/absence of *those*, never on a total row count.
