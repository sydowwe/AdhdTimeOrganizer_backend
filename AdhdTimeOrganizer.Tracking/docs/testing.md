# AdhdTimeOrganizer.Tracking — Testing

## How to test this slice

Tracking has **no test project of its own**. Everything testing it lives in
`AdhdTimeOrganizer.IntegrationTests`, and that is the right place: almost every rule the slice owns is
a property of the *composed* application — the `DbContext` alias, the hand-written combined query
filter on `WebExtensionActivityEntry`, the FastEndpoints assembly scan, the partition annotations, the
two Core seams and the host-side completion handler. None of them are observable from a library-only
unit test.

The infrastructure comes from `Sydowwe.Framework.Testing`: `PostgresTestBase` + the `"Postgres"`
collection over `AppDbContextFixture` (a real Postgres container), `AuthTestBase` for the extension
token flows, and `UserRoleTestAuthHandler` for role/ownership scenarios. See the portal's
`docs/testing.md` and `docs/claude/testing.md` for the full setup.

Three bits of setup are specific to this slice:

- **Two ingest tables are partitioned**, so the fixture has to create them through the partitioned
  SQL generator. A wave of "relation does not exist" failures across tracking tests means that
  plumbing broke, not the endpoints.
- **The ingest endpoints need an extension-flavoured token** carrying the `ActivityTracking` role.
  `ExtensionActivityTrackingTests` builds them; a plain web JWT is *supposed* to get a 403 here, so a
  403 in a new test is often the test's fault, not the endpoint's.
- **Dashboard tests need a user whose `Timezone` is not UTC.** Bands, spans and the same-weekday
  baseline all fold in the user's clock; a UTC-seeded test cannot tell a correct fold from a missing
  one.

## Strategy

**Mapping CRUD and the auth matrix go through the Framework base test classes.**
`TrackingMappingCrudAuthMatrixTests` is one file of thin subclasses — `BaseCreateEndpointTests`,
`BaseUpdateEndpointTests`, `BaseDeleteEndpointTests` — for both mapping tables, plus hand-written
ownership and grid tests for the shapes the bases don't cover. A new endpoint that subclasses a
Framework base should be tested by subclassing the matching base test rather than by hand.

**The dashboards are tested on numbers, not on status codes.** This is the single most important
thing about testing this slice. Every failure mode that matters here returns a well-formed 200
containing a *plausible wrong number*: a span read continuously instead of as a repeated time-of-day
window, a block stitched through an excluded night, a merged minute credited to the wrong source,
parts that no longer add up to their total. A 200 proves nothing.

Where possible, assert an **equality between two surfaces** that can only hold if the arithmetic is
right — the unified suite's `sum(countedSeconds) == totals.totalSeconds` and
`countedSeconds + displacedSeconds ==` that source's own per-source dashboard are the model to copy,
because both identities are printed side by side on one screen for the user to check.

**Assert on rows for anything that fails silently.** A dropped assembly 404s a route; a lost
`[AllowExtensionClients]` 403s an agent; a missing registrar loses the purge trigger with no
exception; a broken query filter leaks another user's browsing history. See the table below.

## The guards — do not delete these

| Test | Pins |
|---|---|
| `TrackingRouteSmokeTests.TrackingDashboardRoutes_AreRegistered` · `TrackingGridRoutes_AreRegistered` · `DesktopDistinctProcessesRoute_IsRegistered` | The slice's assembly is in FastEndpoints' `o.Assemblies` (`DisableAutoDiscovery = true`). Missing → every tracking route 404s, with no build error |
| `TrackingRouteSmokeTests.WebExtensionActivityEntry_KeepsBothOfItsQueryFilters` | The one entity the general `IEntityWithUser` scoping rule does not cover — it is in `AppDbContext.UserScopingExcludedTypes` and carries a hand-written combined filter instead. Losing the user half leaks every user's browsing history to any signed-in caller; losing the date half drops the two-year retention floor (`CurrentPartitionDate` is `today.AddYears(-2)` — a retention horizon, not a partition bound) |
| `TrackingRouteSmokeTests.PartitionedTrackingTables_KeepTheirPartitionKey` (2 cases) | `DesktopActivityEntry` and `WebExtensionActivityEntry` keep the RANGE-partition annotation their configurations put on them, **naming `record_date`**, plus the partition list the generator reads the bounds from. If Tracking's `ApplyConfigurationsFromAssembly` call went missing from `AppDbContext` the model would still build and the next migration would quietly emit plain, unpartitioned tables |
| `TrackingRouteSmokeTests.PartitionedTrackingTables_KeepTheirCompositePrimaryKey` (2 cases) | The `(Id, RecordDate)` key both configurations install over `BaseEntityConfigure()`'s single-column one. Postgres refuses a partitioned table whose primary key omits the partition column, so losing this override does not fail the model build — it fails the next migration against the database. The cheapest half of the guard: it needs no annotation knowledge and goes red the moment the configuration stops being applied |
| `TrackingRouteSmokeTests.PartitionedTrackingTables_KeepTheirNonConcurrencyRowVersion` (2 cases) | The inherited `row_version` is demoted to `IsConcurrencyToken(false).ValueGeneratedNever()` on both, because a partitioned table cannot return `xmin` in a `RETURNING` clause. Left as a token, every insert asks for `xmin` back and fails at runtime; left value-generated, EF waits on a column nothing fills |
| `TrackingRouteSmokeTests.RetentionPurgeJob_IsRegisteredAndScheduled` | `TrackingScheduledJobsRegistrar` actually pushed the purge to the Scheduler. A missing `AddHostedService` builds fine and the GDPR purge never fires |
| `TrackingRetentionPurgeTests` (9 tests) | What the purge actually **deletes** — the `RecordDate < cutoff` predicate with the boundary day surviving, the deliberately cross-user sweep in both directions (every user's old rows go; another user's recent rows stay), the `Enabled` kill switch, idempotence, two yearly bands swept by one delete, and `android_session_data` ageing out on `SessionStartUtc` (`SEC-3`, the ledger with no other retention path). Plus `RetentionOptions_AndTheReadFilter_AgreeOnTheHorizon`, which holds the configured `RetentionYears` at or beyond the hard-coded two-year `CurrentPartitionDate` read floor (`MIG-6`) |
| `TrackingDashboardDateRangeTests` (9 tests) | The span semantic: `from`/`to` is a **time-of-day window repeated on each day**, not the ends of the range; bands get each day's own start instant and truncate at the window edge; the baseline scales to the span; the timelines reject a span; the year cap holds at the boundary; the details endpoint carries the daily window across its instant envelope |
| `TrackingFocusMetricsTests` (9 tests) | The four fragmentation measures and their definitions — switch counting, client-sent block tolerance, interior-only gaps, median-not-mean — and the load-bearing rule that **nothing is measured across the night between two days' windows** |
| `TrackingUnifiedDashboardTests` (29 tests) | The overlap rule, asserted on seconds: both levels of precedence in the right order, partial (not wholesale) displacement, a deselected source getting its time **back**, the cross-source label join, device-change-is-not-a-switch, non-overlapping timeline lanes with ids unique across them, and totals on an empty result. Also the two merged dashboards that read off the same ledger — `stacked-bars` (a band's segments sum to its own height and to what the same span contributes to the pie, across a minute the merge splits into fractional seconds; label merged across sources; the same bands as the per-source bars; empty bands omitted) and `summary-cards` (the card total equals the pie slice, **the lookback is merged the same way as the span** rather than double-counted, the mean day scales to the span, `isNew`, null background for an android-only item, the shared default `topN`) |
| `TrackingDashboardItemIdentityTests` (2 tests) | The additive `key`/`label` pair carried by all three sources, and the never-blank fallback of `label` to `key` — the half that regresses silently |
| `TrackingDashboardUserZoneTests` (2 tests) | Bands and the same-weekday baseline resolve on the user's clock and calendar, not UTC |
| `TrackingAndroidTimelineTests` (2 tests) | A session's `totalSeconds` and the `minSeconds` filter read **that session's** own length, not the app's whole-day total — the drift that makes a timeline strip disagree with the chart above it |
| `ExtensionActivityTrackingTests` (8 tests) | Extension-client auth on ingest from both directions (extension token without the tracking role → 403; web JWT against the tracking policy → 403; extension JWT against a web-only endpoint → 403), and the end-to-end mapped-heartbeat → attribution → planner-task-completion path |
| `ActivityTimeAutomationTests` (5 tests) | The completion branch matrix behind `ActivityTimeRecordedEvent`, including `PlannerTaskMatched_TodoFallback_DoesNotRun` — the **exclusivity** rule that is the reason the automation is one host-side handler and not two subscribers |
| `TrackerPatternMappingActivityFkTests` (4 tests) | Both mapping FKs are N:1 (two patterns may target one activity) and `Cascade` (deleting the activity destroys the rule) |
| `TrackingMappingCrudAuthMatrixTests` | The mapping CRUD auth matrix: create stamps the *caller's* `UserId` rather than the DTO's zero, update/delete of a foreign id leaves the foreign row untouched, and the two grids exclude other users' rows |

Also relevant, though owned elsewhere: `ActivityForeignKeyInventoryTests` (the model-level companion
that freezes every activity FK in the solution and its delete behaviour), `SeamWiringTests`,
`NamedQueryFilterTests`, `UserScopingQueryFilterTests`, `ModuleSchemaTests` (every entity here must
resolve to the `tracking` schema), `UserDataExportAndDeletionTests` (a new user-scoped entity here
fails it until classified — a GDPR Art. 15 decision), and `CoreRouteSmokeTests`' seeder-duplication
test, which covers the double-registration trap over every registered seeder.

## Known gaps (living list)

One `[Trait("Status","KnownGap")]` test is carried for this slice —
`TrackingRetentionPurgeTests.Purge_LogsNothingWhenItDeletedNothing` (`AUDIT-1`): the purge handler logs
only when it deleted something, so a caught-up deployment's nightly run leaves no evidence the GDPR
control executed at all. The test asserts today's silence; invert it and drop the trait when the handler
gains an unconditional per-run Information line.

Three standing caveats rather than gaps:

- **The registrar's boot behaviour is not integration-tested.**
  `TrackingRouteSmokeTests.RetentionPurgeJob_IsRegisteredAndScheduled` asserts the job is registered
  after a normal boot, but not that `StartAsync` re-pushes the trigger after a *restart* — the one
  path whose failure is completely silent. Testing it needs a host restart within a fixture; if you
  add one, assert on registered job keys, not on rows.
- **`ExecutablePath` encryption is asserted only by the column configuration.** No test reads the
  raw column to prove the ciphertext; if you add one, read it through raw SQL, since EF decrypts on
  materialization.
- **The partition guards are model-level, and cannot be otherwise.** The three
  `PartitionedTrackingTables_*` cases assert what the configurations put on the model, not what the
  database ended up with. A `pg_class.relkind = 'p'` check would be stronger, but the fixture builds
  its schema with `EnsureCreated`, which never runs `PartitionedNpgsqlMigrationsSqlGenerator` — the
  test tables really are ordinary ones, so such an assertion would fail against a healthy model.
  Proving the DDL needs a migrated database; until there is one, the emitted migration is the only
  place partitioning is observable.
- **`WebExtensionDataSeeder` has no test.** It is a dev-only fixture seeder; the seeding chain itself
  is pinned by `PerUserDefaultSeederTests` in the host.
