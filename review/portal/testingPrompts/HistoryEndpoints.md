# TEST-6 — Activity history CRUD auth matrix

## Context

Same infra as the other portal integration tests: `PostgresTestBase`, `CreateUserRoleClient()`,
`Sydowwe.Framework.Testing.baseTests`'s 13 abstract CRUD/auth bases, `ActivityEndpointTests.cs` as the
reference pattern (subclass framework bases + `Seed.SecondUserAsync` for the cross-user id).

## What exists today (do not duplicate)

`Endpoints/HistoryRouteSmokeTests.cs` already covers, for `AdhdTimeOrganizer.History`:
- Route-registration smoke tests for `form-select-options`, the grid (`gird`), and all 6 dashboard
  endpoints (`dashboard/detail/*`, `dashboard/summary/*`) — proves the slice's assembly is in the
  FastEndpoints `o.Assemblies` list.
- `Grid_MembershipFilter_NarrowsThroughTheSeam` — proves the grid's `IsFromTodoList` filter resolves
  `IActivityMembershipSource` from DI correctly (a string-keyed seam that fails silently if
  misregistered).
- `AggregateByActivity_SumsPerId_OmitsEmptyIds_AndExcludesOtherUsers` — proves the aggregate endpoint
  sums per activity id, omits ids with no history, and excludes other users' rows.

None of that is a CRUD auth matrix. `ActivityHistory` has full CRUD:
`application/endpoint/command/{Create,Update,Delete}ActivityHistoryEndpoint.cs` and
`application/endpoint/query/{GetById,FilterActivityHistory,GetFilteredTable}Endpoint.cs`. **None of
these have auth coverage** — no test proves a cross-user `ActivityHistory` id 404s on
Update/Delete/GetById, or that the `Filter`/`GetFilteredTable` results are scoped to the caller.

## What to write

Add `Endpoints/HistoryCrudAuthMatrixTests.cs` subclassing the framework bases for
Create/Update/Delete/GetById/Filter/FetchTable against `ActivityHistory`, following
`ActivityEndpointTests.cs`'s shape exactly (same `Seed.SecondUserAsync` idiom). `ActivityHistory` is
`IEntityWithUser` per `domain-map.md` — confirm this still holds and pick `UnauthorizedStatus` (404 via
global query filter) accordingly, the same way `ActivityEndpointTests.cs` did for its entities.

## Out of scope

Don't re-test dashboard routing, the membership-filter seam, or the aggregate endpoint — those are
already pinned by `HistoryRouteSmokeTests.cs`. This task is CRUD auth only.
