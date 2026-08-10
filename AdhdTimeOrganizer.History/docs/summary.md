# AdhdTimeOrganizer.History — Agent Summary

**Purpose:** The activity-history slice. Owns the `ActivityHistory` entity, its CRUD/read endpoints,
the paginated grid, and the six dashboard endpoints (three `HistoryDetail*`, three `HistorySummary*`).

**Third project of the portal split**, after `AdhdTimeOrganizer.Core` and
`AdhdTimeOrganizer.TodoLists`. Plan: `review/portal/slicePrompts/00-README.md`.

## Bounded context

Owns: `ActivityHistory` + its EF configuration, 13 endpoints, the history request/response/filter
DTOs, `ActivityHistoryValidator`, and the dev `ActivityHistorySeeder` (Order 300, inside its band —
see `AdhdTimeOrganizer.Core/infrastructure/persistence/seeder/SeederOrderBands.md`).

Does **not** own:

- **`CalendarActivityEndpoint`** and its two DTOs. Filed under `activityHistory/` and it does read
  history, but it reads the `Calendar` entity too — that belongs to Planning, which is not extracted
  yet, so all three files stayed host-side at their original paths. Move them when Planning lands,
  not before.
- **The suggestion-pattern machinery** — `SuggestionPatternRefreshInterceptor`,
  `SuggestionPatternRefreshQueue`, `SuggestionPatternView`, `SuggestionPatternViewInstaller`,
  `SuggestionPatternRefreshJob`, `ActivityHistoryPattern` + its configuration, and the three
  `sqlScripts/*.sql` materialized views. These span History + Planning + Calendar and are wired into
  `Program.cs`. The interceptor fires on any save touching `ActivityHistory`, so the host now
  references this slice — that is host → slice, which is the correct direction.
- **The three tracking configurations** still sitting in the host's
  `infrastructure/persistence/configuration/activityHistory/` folder (`DesktopActivityEntry`,
  `WebExtensionActivityEntry`, `AndroidSessionData`). The folder name lies; those entities belong to
  Tracking.

## Dependency seams

- **References:** `AdhdTimeOrganizer.Core`, `Sydowwe.Framework`, `Sydowwe.Framework.Contracts`.
  **Nothing else** — in particular *not* `AdhdTimeOrganizer.TodoLists` and not the host.
- **Referenced by:** `AdhdTimeOrganizer` (the host) and the integration test project.

### ⚠ The membership seam — the one thing not to undo

The grid filters on to-do / routine membership (`IsFromTodoList`, `TaskPriorityId`,
`IsFromRoutineTodoList`, `RoutineTimePeriodId`). It used to do that by querying
`dbContext.TodoListItems` / `dbContext.RoutineTodoLists` directly, which made History depend on
TodoLists *and* Routines and forced a fixed extraction order across three slices.

It now goes through **`IActivityMembershipSource`** (`AdhdTimeOrganizer.Core/application/seam/`):

- Core declares the interface and the key constants (`ActivityMembershipSourceKeys`).
- The owning slice implements it — `TodoListActivityMembershipSource` in
  `AdhdTimeOrganizer.TodoLists`, `RoutineTodoListActivityMembershipSource` **currently host-side**
  because `RoutineTodoList` has not been extracted yet (move it with the entity, nothing else
  changes).
- History consumes `IEnumerable<IActivityMembershipSource>` and matches on `Key`.

Each source returns a **composable** `IQueryable<long>` of activity ids, so `ids.Contains(...)`
renders as `IN (SELECT …)` — the same plan the hand-written `EXISTS` subqueries produced. Do not
materialize it, and do not "simplify" the seam back into a direct
`dbContext.Set<TodoListItem>()` query: that re-creates exactly the coupling this project exists
without.

A source resolved by string key fails **silently** when it is missing or misregistered — no build
error, no exception, the filter just stops narrowing.
`HistoryRouteSmokeTests.Grid_MembershipFilter_NarrowsThroughTheSeam` is what catches that; keep it.

## Gotchas

- **History takes a plain `DbContext`, never `AppDbContext`** — `ModuleServiceExtensions` aliases it.
  So `dbContext.Set<ActivityHistory>()`, never `dbContext.ActivityHistories`.
- **User scoping comes from the DbContext, not the endpoints.** `AppDbContext.OnModelCreating`
  applies a global query filter to every `IEntityWithUser`, and `ActivityHistory` is one. The base
  endpoints' `ApplyUserScoping` is a no-op virtual — do not rely on it. Keep `ActivityHistory`
  `IEntityWithUser` with its FKs and cascades intact.
- **A save touching `ActivityHistory` needs the three materialized views to exist**, or Postgres
  fails with 42P01. The app installs them via `SuggestionPatternViewInstaller` (embedded resources);
  the test fixture applies `sqlScripts/*.sql` in `AppDbContextFixture.OnSchemaCreatedAsync`. If
  history tests start failing with 42P01, that plumbing is what broke — not this slice.
- **The grid route is `/activity-history/gird`.** The typo is in the shipped contract
  (`EndpointPath => "gird"`); the SPA calls it that way. Do not "fix" it without changing the SPA.
- **Four registration sites, none of which break the build if missed:** FastEndpoints `o.Assemblies`
  in `Program.cs` (miss → routes 404), `ModuleAssemblies` in `ModuleServiceExtensions` (miss → the
  seeder is registered by the AppDomain sweep instead; being in *both* registers it twice),
  `ApplyHostConfigurations` in `AppDbContext` (miss → the table drops out of the model), and the
  `.sln`.

## Navigation

`docs/domain-map.md` — the entity, its relationships, the endpoint inventory, and the invariants.
