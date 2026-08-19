# Testing

Full guide: `framework/Sydowwe.Framework.Testing/docs/testing.md` (**not** the root `docs/testing.md`,
which is a foreign copy). Quick reference:

- Tests run the real portal `Program` against a Postgres container (`Testcontainers.PostgreSql`), with
  auth and a couple of singletons swapped. xunit v3 + FluentAssertions + Moq + Respawn.
- Shared infrastructure lives in `Sydowwe.Framework.Testing`: one fixture
  (`PostgresContainerFixture<TProgram, TDbContext>`), one base class (`PostgresTestBase`), one auth
  handler (`RoleTestAuthHandler`, scheme `"Test"`), one factory (`TestWebApplicationFactory<TProgram>`).
  The handler and factory are role-parametrized — do not add per-role subclasses.
- This portal closes the fixture in
  `AdhdTimeOrganizer.IntegrationTests/Infrastructure/AppDbContextFixture.cs`; tests are tagged
  `[Collection("Postgres")]`. Its `OnSchemaCreatedAsync` applies
  `AdhdTimeOrganizer/infrastructure/persistence/sqlScripts/*.sql` (the three suggestion-pattern
  materialized views), copied next to the test binaries by a `Content` item in the test csproj. They
  are hand-written SQL, not migration output, so `EnsureCreated` skips them — and without them
  `SuggestionPatternRefreshInterceptor` fails with 42P01 on any save touching `PlannerTask`,
  `ActivityHistory` or `Calendar`. Add new scripts to that folder and they are picked up. The running
  app gets the same views from `SuggestionPatternViewInstaller` (called from `Program.cs` just before
  `SeedDatabase`), which reads them as **embedded resources** and creates only the ones `to_regclass`
  says are missing — the two paths read the same three files, so a new script is installed both places.
- Test bases get HTTP clients via `CreateClient()` (Admin+User), `CreateAdminRoleClient()`,
  `CreateUserRoleClient()`, `CreateRootRoleClient()`, `CreateUnauthenticatedClient()`. For different
  test users, `CreateFactory(roles, userId)` — caller disposes. `CreateDbContext()` for
  seeding/asserting outside HTTP; override `SeedAsync(db)`.
- For each FastEndpoints base in `endpoint/base/` there is a matching abstract test base in
  `framework/Sydowwe.Framework.Testing/baseTests/` (`BaseGetByIdEndpointTests`, `BaseGridEndpointTests`,
  `BaseCreateEndpointTests`, `BaseUpdateEndpointTests`, `BaseDeleteEndpointTests`,
  `BasePatchEndpointTests`, `BaseGetAllEndpointTests`, `BaseGetSelectOptionsEndpointTests`,
  `BaseFilterEndpointTests`, `BaseSortEndpointTests`, `BaseFilterSortEndpointTests`,
  `BaseBatchDeleteEndpointTests`, `BaseToggleIsHiddenEndpointTests`). Use them — they ship the auth
  matrix + 404 paths. Add endpoint-specific scenarios (validation, business rules, IDOR) in the
  concrete subclass.

## Things that only a behavioural test catches

Every cross-slice mechanism in this solution fails **silently** — no build error, no exception, just a
filter that stops narrowing or a job that never fires. Assert on rows, not on routes. Existing guards
worth copying the shape of:

- `SeamWiringTests` — the seam registry (placement, key coverage, no unhandled events).
- `HistoryRouteSmokeTests.Grid_MembershipFilter_NarrowsThroughTheSeam`.
- `ActivityTimeAutomationTests` — the event-driven completion rule.
- `TrackingRouteSmokeTests.WebExtensionActivityEntry_KeepsItsCombinedQueryFilter` — seeds two users and
  an out-of-range row rather than inspecting metadata.
- `UserScopingQueryFilterTests` — asserts the generated SQL parameterizes the user id, and that two
  users hitting one endpoint in one process each see only their own rows.
- `PlanningRouteSmokeTests.PlannerTaskToTodoListItem_ForeignKey_SurvivesTheSliceSplit`.
- `ActivityProfilesRouteSmokeTests.Core_DoesNotReferenceActivityProfiles`.
- `AccountDeletionSummaryTests` — the deletion-warning counts. Seeds a *different* row count into every
  category, so a subquery aimed at the neighbouring table lands on the wrong number instead of
  coincidentally the right one, and pins that the web-extension count deliberately reads past the
  partition-window query filter.
- `ActivityMergeTests.Merge_RepointsEveryReferencingSlice` — one merge, one referencing row per slice,
  asserted on the surviving rows in six projects rather than on the endpoint's `repointedCount`. The
  `IActivityReferenceSource` seam resolves by string key, so a slice dropped from `ModuleAssemblies`
  leaves its rows on activities the same request then deletes — and every activity FK here is
  `Cascade`, so they are destroyed, not orphaned, while the response still looks right.
- `ActivityArchivingTests` — which endpoints exclude archived activities and, just as important, which
  must not. The rule is "only pickers exclude"; both halves fail as a valid 200 with a list of the
  wrong length, so every picker is asserted not to contain a seeded archived row and every
  record-reading surface is asserted to still resolve it. It also pins that the *absent* filter means
  active-only (the settings table's default view depends on it) and that `usageCount` sorts in SQL —
  a count applied after projection would sort every row on `0` and return an arbitrary page in
  convincing order.
- `ModuleWiringTests` — the whole composition root.
- `PerUserDefaultMatcherTests` — no DB needed.
- `ActivityRoleSystemKeyTests` — the three app-referenced activity roles resolve by `SystemKey` after the
  user renames them, and stay undeletable. The lookup they replaced was by display name, so a rename
  404'd it and quick-create died in four dialogs with nothing thrown and nothing logged.
