# AdhdTimeOrganizer.History — Testing

## How to test this slice

History has **no test project of its own**. Everything testing it lives in
`AdhdTimeOrganizer.IntegrationTests`, and that is the right place: nearly every rule the slice owns is
a property of the *composed* application — the `DbContext` alias, the global user query filter, the
FastEndpoints assembly scan, the membership seam's fan-in, the host-side cross-slice FKs — and none of
them are observable from a library-only unit test.

The infrastructure comes from `Sydowwe.Framework.Testing`: `PostgresTestBase` + the `"Postgres"`
collection over `AppDbContextFixture` (a real Postgres container), and `UserRoleTestAuthHandler` for
role and ownership scenarios (`CreateUserRoleClient()`, `CreateAdminRoleClient()`). See the portal's
`docs/testing.md` and `docs/claude/testing.md` for the full setup.

Two bits of setup are specific to this slice:

- **The materialized views must exist before any save touching `ActivityHistory`.**
  `AppDbContextFixture.OnSchemaCreatedAsync` applies `sqlScripts/*.sql` for exactly this reason. A wave
  of `42P01` failures across unrelated history tests means that fixture step broke, not the endpoints.
- **Timezone tests need a user whose `Timezone` is not UTC.** Every dashboard folds in
  `User.Timezone`; a test seeded in UTC cannot tell a correct fold from a missing one.

## Strategy

**CRUD and the auth matrix go through the Framework base test classes.**
`HistoryCrudAuthMatrixTests` is one file of thin subclasses — `BaseGetByIdEndpointTests`,
`BaseCreateEndpointTests`, `BaseUpdateEndpointTests`, `BaseDeleteEndpointTests`,
`BaseFilterEndpointTests` — plus two hand-written ownership tests for the shapes the bases don't
cover. A new endpoint that subclasses a Framework base should be tested by subclassing the matching
base test rather than by hand.

**The dashboards are tested on values, not on status codes.** Every failure mode that matters here
produces a well-formed response containing a *plausible wrong number*: a UTC fold instead of a user
fold, a range clipped one midnight short, a group re-keyed by name across a rename. A 200 proves
nothing. `HistoryTimeOfDayDashboardTests` asserts that `sum(totalSeconds)` equals the pie chart's
total for the same range — copy that shape: pick an equality between two surfaces that can only hold
if the arithmetic is right.

**Assert on rows for anything that fails silently.** A missing membership source lowers a count; a
dropped assembly 404s a route; a missing registrar loses triggers with no exception. See the table
below.

## The guards — do not delete these

| Test | Pins |
|---|---|
| `HistoryRouteSmokeTests.HistoryDashboardRoutes_AreRegistered` (7 routes) · `HistoryGridRoute_IsRegistered` · `HistorySelectOptionsRoute_IsRegistered` | The slice's assembly is in FastEndpoints' `o.Assemblies`. Missing → every history route 404s, with no build error |
| `HistoryRouteSmokeTests.Grid_MembershipFilter_NarrowsThroughTheSeam` | `IActivityMembershipSource` is resolved and matched by key. A missing or misregistered source fails **silently** — the filter simply stops narrowing |
| `HistoryRouteSmokeTests.AggregateByActivity_SumsPerId_OmitsEmptyIds_AndExcludesOtherUsers` | The aggregate keys by id, **omits** ids with no rows rather than returning zeros (which is what lets callers divide by `entryCount` unguarded), and is user-scoped |
| `HistoryDashboardGroupIdentityTests` (7 tests) | Every group-shaped response carries the `groupId` of the entity it grouped by; identity survives a rename; `groupId` is null on exactly the two synthetic buckets (`Uncategorized`, the pie's `_other` roll-up). A name-keyed response is well-formed and its numbers add up — this is the only thing that catches it |
| `HistoryDashboardUserZoneTests` (4 tests) | Ranges and picked windows resolve in `User.Timezone`, not UTC; summary and detail agree on the instants a window names; a window starting inside the spring-forward gap resolves to the end of the gap |
| `HistoryTimeOfDayDashboardTests` (5 tests) | The three contract properties of the fold: always 24 hours in order 0…23 including zeros; folded in the user's zone; a record crossing an hour boundary split by elapsed time and counted in `entries` once per hour — so `sum(entries)` is not the record count while `sum(totalSeconds)` **is** the period total |
| `HistorySummaryCustomRangeTests` (7 tests) | A custom range spans `date`…`endDate` **inclusive**, a single day is one day not two, the max span is enforced at exactly the boundary, and named range types ignore `endDate` rather than reading or rejecting it |
| `HistoryCrudAuthMatrixTests.Filter_ExcludesOtherUsersHistory` · `GetFilteredTable_ExcludesOtherUsersHistory` | The global user query filter actually scopes these reads. `ApplyUserScoping` on the endpoint bases is a **no-op virtual** and guards nothing |
| `TimerBoundaryAlarmTests` (9 tests) | The alarm lifecycle end to end: one row per *future* boundary, idempotent by session, cancel removes the session and is a no-op on an unknown one, the handler fires then self-drains, a cancelled alarm does nothing, the push envelope carries `tag` + `url` at top level (and omits `tag` for kinds supplying none), and `TimerBoundary` is exempt from quiet-hours deferral |
| `TodoListItemDailyRecapTests.TimeIsAttributedByItem_NotByActivity` | `TodoListItemLoggedTimeSource` keys on `TodoListItemId` **alone**. Widening it to the activity looks like it can only find *more* rows and in fact double-counts two items sharing one activity |
| `TodoListItemDailyRecapTests.DeletingTheItem_KeepsTheRecording_AndUnlinksIt` | The host-side `SET NULL` on `TodoListItemId`. A cascade here would delete recorded time when a task is deleted |
| `SuggestionPatternRefreshTests.Save_ActivityHistory_MarksOnlyActivityHistoryDirty` | The host interceptor fires on saves touching `ActivityHistory` and dirties only that view. Owned by the host, but this slice is what triggers it |
| `ModuleSchemaTests` | Every entity in this assembly resolves to the `history` schema. A new entity fails it until it is added to the suite's `history` table list |
| `UserDataExportAndDeletionTests` | Every user-scoped entity is either exported or listed in that suite's `KnownGaps`. A new user-scoped entity here fails it until classified — a GDPR Art. 15 decision the test refuses to let anyone skip |
| `ActivityForeignKeyInventoryTests` | The frozen inventory of FKs into `Activity` and their delete behaviours — including the fact that `TimerBoundaryAlarm.ActivityId` carries **no** FK |

Also relevant, though owned elsewhere: `SeamWiringTests` (this slice implements
`IActivityTimeAttributionSink`, `ITodoListItemLoggedTimeSource` and `IActivityReferenceSource`, and
each single seam must resolve exactly once), `NamedQueryFilterTests`, `UserScopingQueryFilterTests`,
and the scheduler suites under `Infrastructure/` that the alarms ride on.

## Known gaps (living list)

No `[Trait("Status","KnownGap")]` tests are carried for this slice today. Three standing caveats
rather than gaps:

- **The registrar's boot behaviour is not integration-tested.** `TimerBoundaryAlarmTests` covers
  schedule / cancel / fire, but not `TimerBoundaryAlarmRegistrar.StartAsync` re-pushing triggers after
  a restart — the one path whose failure is completely silent. Testing it needs a host restart within
  a fixture; if you add one, assert on registered job keys, not on rows.
- **The accepted residual race in cancel.** A tab killed between Stop and the cancel request landing
  fires an alarm for a session already over. This is accepted, not mitigated (see `summary.md`), so no
  test asserts it away — don't write one that "fixes" it.
- **`ActivityHistorySeeder` has no test.** It is a dev-only fixture seeder; the seeding chain itself is
  pinned by `PerUserDefaultSeederTests` in the host.
