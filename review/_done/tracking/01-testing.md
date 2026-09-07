# Tracking — Test Review (01)

Point-in-time snapshot, 2026-09-01. First review of this module; there is no prior
`review/tracking/` to delta against, so nothing here is a re-flag. The module's living docs
(`AdhdTimeOrganizer.Tracking/docs/summary.md`, `docs/domain-map.md`, `docs/testing.md`) were read
first and are treated as the spec — where the code contradicts them the finding is in
[`02-findings.md`](02-findings.md) under `DOC-n`.

Findings referenced by ID live in [`02-findings.md`](02-findings.md); the risk ranking is in
[`03-risks-rollout.md`](03-risks-rollout.md).

## Test infrastructure summary

Everything testing this slice lives in `AdhdTimeOrganizer.IntegrationTests` — the slice has no test
project, which `docs/testing.md` argues for and I agree with: the `DbContext`→`AppDbContext` alias,
the hand-written combined filter on `WebExtensionActivityEntry`, the FastEndpoints assembly scan and
the two Core seams are all properties of the composed host. Nine files touch tracking:
`TrackingRouteSmokeTests`, `TrackingDashboardDateRangeTests`, `TrackingFocusMetricsTests`,
`TrackingUnifiedDashboardTests`, `TrackingDashboardItemIdentityTests`, `TrackingDashboardUserZoneTests`,
`TrackingAndroidTimelineTests`, `TrackingMappingCrudAuthMatrixTests`, `ExtensionActivityTrackingTests`,
plus `TrackerPatternMappingActivityFkTests` and `ActivityTimeAutomationTests` on the seam side.

Infrastructure is `Sydowwe.Framework.Testing`: `PostgresTestBase` over the `"Postgres"` collection and
`AppDbContextFixture` (a real Postgres container, `Program`-hosted), `AuthTestBase` for the extension
token flows, `RoleTestAuthHandler`/`FakeLoggedUserService` for role and ownership scenarios, and the
Framework's `BaseCreateEndpointTests` / `BaseUpdateEndpointTests` / `BaseDeleteEndpointTests` which
`TrackingMappingCrudAuthMatrixTests` subclasses for both mapping tables.

**Two fragile things, one of them serious.**

1. **`PostgresContainerFixture` sets `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)`
   (`framework/Sydowwe.Framework.Testing/PostgresContainerFixture.cs:26`) and `Program.cs` does not.**
   Every test therefore runs against a *different* Npgsql date-time contract than production: a
   `DateTime` with `Kind=Unspecified` is silently treated as UTC in tests and **throws** against a
   `timestamp with time zone` column in production. `CQ-1` is exactly that bug, sitting in three
   shipped endpoints, and it is structurally invisible to this suite. Any test written for an
   `AllTime` baseline today will pass on a code path that 500s for real users.
2. The two ingest tables are partitioned, so the fixture has to create them through the partitioned
   SQL generator; a wave of "relation does not exist" across tracking tests means that plumbing broke
   rather than the endpoints. `docs/testing.md` already says so.

## 7. Test quality assessment

**Strengths — and they are real ones, not politeness.**

- **The dashboards are asserted on numbers, not status codes**, which is the single correct decision
  for this module. `TrackingDashboardDateRangeTests:42` (`PieChart_RepeatsTheTimeOfDayWindowOnEachDay_…`)
  and `TrackingFocusMetricsTests:141` (`RangeSpans_DoNotMeasureBlocksOrGapsAcrossTheNightBetweenTwoDaysWindows`)
  both pin failure modes that return a well-formed 200 with a plausible wrong number. Nothing else in
  this repo's review history is this well aimed.
- **The unified suite asserts equalities between two surfaces**, which is the strongest shape available:
  `sum(countedSeconds) == totals.totalSeconds`, and a deselected source getting its seconds *back*
  (`TrackingUnifiedDashboardTests:131`). Those cannot pass by coincidence.
- **The route smoke tests were repaired in the right direction.** The XML doc on
  `TrackingRouteSmokeTests:52-60` records that the theory previously asserted "not 404 / not 405" while
  all twelve routes were answering **400** on a malformed body — assertion theatre caught and fixed.
  It now asserts `OK`. The same note on `TrackingGridRoutes_AreRegistered:96-106` about `required
  TrackerDesktopMappingTypeEnum Type` is the same class of catch. This is the best self-documentation
  in the suite and should be imitated.
- **Ownership is tested from the write side, not just the read side**: `Update_ForeignId_LeavesTheForeignRowUntouched`
  asserts the foreign row is *unchanged*, not merely that the response was 404.

**Weaknesses.**

- **Auth is tested on one of the three ingest endpoints.** `ExtensionActivityTrackingTests` drives
  `desktop/heartbeat` for all five auth cases and `android/sync` for one happy case.
  `web-extension/heartbeat` appears **nowhere in the suite** — no auth case, no ingest case, no
  minute-alignment case. That is the endpoint that turns out to be missing its policy (`SEC-1`), and
  the gap and the bug are the same gap.
- **`docs/testing.md` claims a guard that does not exist.** It lists `TrackingRouteSmokeTests` as
  covering silent failure #5 ("Tracking's `ApplyConfigurationsFromAssembly` missing → the two
  partitioned tables quietly become plain tables"). The class's own XML doc even `<see cref>`s a
  `PartitionedTrackingTables_KeepTheirPartitionKey` method — **there is no such test method in the
  file**. The partition annotation is unpinned (`DOC-5`, `TEST-8`).
- **Ingest is tested only through its happy path.** No test sends two entries that collide on the
  ledger's own unique index (`CQ-8`), a duplicated session inside one sync batch (`CQ-7`), an
  offset-bearing timestamp (`CQ-6`), or an over-length string (`CQ-9`). All four are 500s today.
- **The mapping CRUD matrix proves you cannot read another user's row and never asks whether you can
  *point at* one.** Identical in shape to `SEC-1` in the TodoLists review, and live here too
  (`SEC-2`) — with a worse consequence, because a mapping is a standing rule that keeps attributing.
- **Two whole unified dashboards are untested**: `unified/summary-cards` and `unified/stacked-bars`
  appear in no test file, not even the route smoke theory (which lists the fifteen per-source routes
  and none of the six unified ones).
- Fixture hygiene is otherwise good — seed in one context, assert through a fresh one — and I found no
  vacuous assertions in the tracking files, unlike the Routines pass.
- Flakiness risk is low: nothing here sleeps or races, and the dashboard tests seed absolute instants.
  The one exposure is `WebExtensionDataSeeder`'s `Random.Shared` (untested, dev-only).

## 8. Coverage matrix

Counts are distinct test methods that exercise the route. "Auth/IDOR" means an assertion about another
user's row, an unauthenticated caller, or the client-type/role gate. ⚠ = exercised only generically
(route smoke or a framework base case) with no behaviour of this endpoint asserted.

### Endpoints — desktop (14)

| Endpoint | Happy | Edge | Auth/IDOR | Test file |
|---|---|---|---|---|
| `POST /desktop/heartbeat` | ✅ 2 | ❌ | ✅ 3 | `ExtensionActivityTrackingTests` (incl. the end-to-end seam case) — batch collisions, lengths, pattern precedence unpinned → `TEST-6` |
| `POST /desktop/pie-chart` | ✅ 3 | ✅ 2 | ❌ | `TrackingDashboardDateRangeTests`, `TrackingDashboardItemIdentityTests` |
| `POST /desktop/stacked-bars` | ✅ 4 | ✅ 3 | ❌ | `TrackingDashboardDateRangeTests`, `TrackingDashboardUserZoneTests` |
| `POST /desktop/summary-cards` | ✅ 2 | ✅ 1 | ❌ | `TrackingDashboardDateRangeTests:210`, `TrackingDashboardUserZoneTests:106` — baseline window (`CQ-2`) and `AllTime` (`CQ-1`) unpinned → `TEST-3` |
| `POST /desktop/timeline` | ✅ 2 | ✅ 1 | ❌ | `TrackingDashboardDateRangeTests`, `TrackingDashboardItemIdentityTests` |
| `POST /desktop/focus-metrics` | ✅ 9 | ✅ 6 | ❌ | `TrackingFocusMetricsTests` — the best-covered endpoint in the slice |
| `GET /desktop/process-details` | ✅ 1 | ✅ 1 | ❌ | `TrackingDashboardDateRangeTests:294` — no 404 case, no range cap (`PERF-3`) → `TEST-10` |
| `GET /desktop/distinct-processes` | ⚠ smoke | ❌ | ❌ | `TrackingRouteSmokeTests:124` → `TEST-9` |
| `POST /desktop/gird` (distinct entries) | ⚠ smoke | ❌ | ❌ | `TrackingRouteSmokeTests` — pagination, filter modes, scoping unasserted → `TEST-9` |
| `POST /desktop/fetch-categories-and-roles-by-pattern` | ❌ | ❌ | ❌ | — **nothing at all** → `TEST-9` |
| `POST /desktop/settings/…-by-pattern` (create) | ✅ 2 | ❌ | ✅ 3 | `TrackerDesktopMappingCreateTests` — foreign FK ids unasserted → `TEST-4` |
| `PUT /desktop/settings/…/{id}` | ✅ 1 | ❌ | ✅ 2 | `TrackerDesktopMappingUpdateTests` — target immutability (`CQ-10`), unique collision (`CQ-11`) unasserted |
| `DELETE /desktop/settings/…/{id}` | ✅ 1 | ❌ | ✅ 2 | `TrackerDesktopMappingDeleteTests` |
| `POST /desktop/settings/…/filtered-table` | ✅ 2 | ⚠ smoke | ✅ 3 | `TrackerDesktopMappingGridTests` — `Type` switch throw (`CQ-15`) unasserted |

### Endpoints — web extension (7)

| Endpoint | Happy | Edge | Auth/IDOR | Test file |
|---|---|---|---|---|
| `POST /web-extension/heartbeat` | ❌ | ❌ | ❌ | — **nothing at all**, and it is the endpoint missing its policy (`SEC-1`) → `TEST-1` |
| `POST /web-extension/pie-chart` | ⚠ smoke | ❌ | ❌ | `TrackingRouteSmokeTests`, `TrackingDashboardItemIdentityTests:40` (identity pair only) |
| `POST /web-extension/stacked-bars` | ✅ 1 | ✅ 1 | ❌ | `TrackingDashboardUserZoneTests:60` (zone theory) |
| `POST /web-extension/summary-cards` | ⚠ smoke | ❌ | ❌ | `TrackingRouteSmokeTests` → `TEST-3` |
| `POST /web-extension/timeline` | ⚠ smoke | ❌ | ❌ | `TrackingRouteSmokeTests` — the stitching rule `WindowStart == previous.EndedAt` is unpinned |
| `POST /web-extension/focus-metrics` | ⚠ smoke | ❌ | ❌ | `TrackingRouteSmokeTests` |
| `GET /web-extension/domain-details` | ❌ | ❌ | ❌ | — **nothing at all** → `TEST-10` |

### Endpoints — android (11)

| Endpoint | Happy | Edge | Auth/IDOR | Test file |
|---|---|---|---|---|
| `POST /android/sync` | ✅ 1 | ❌ | ✅ 1 | `ExtensionActivityTrackingTests:97` — dedup, offsets, mismatched duration, batch dupes unpinned → `TEST-2` |
| `POST /android/pie-chart` | ⚠ smoke | ❌ | ❌ | `TrackingRouteSmokeTests`, `TrackingDashboardItemIdentityTests` — the `minPercent` default (`CQ-4`) unasserted |
| `POST /android/stacked-bars` | ⚠ smoke | ❌ | ❌ | `TrackingRouteSmokeTests` |
| `POST /android/summary-cards` | ⚠ smoke | ❌ | ❌ | `TrackingRouteSmokeTests` — UTC weekday bug (`CQ-3`) sits under an untested route → `TEST-3` |
| `POST /android/timeline` | ✅ 2 | ✅ 1 | ❌ | `TrackingAndroidTimelineTests` |
| `POST /android/focus-metrics` | ⚠ smoke | ❌ | ❌ | `TrackingRouteSmokeTests` |
| `POST /android/gird` (distinct entries) | ⚠ smoke | ❌ | ❌ | `TrackingRouteSmokeTests` → `TEST-9` |
| `POST /android/settings/…-by-pattern` (create) | ✅ 2 | ❌ | ✅ 3 | `TrackerAndroidMappingCreateTests` → `TEST-4` |
| `PUT /android/settings/…/{id}` | ✅ 1 | ❌ | ✅ 2 | `TrackerAndroidMappingUpdateTests` |
| `DELETE /android/settings/…/{id}` | ✅ 1 | ❌ | ✅ 2 | `TrackerAndroidMappingDeleteTests` |
| `POST /android/settings/…/filtered-table` | ✅ 2 | ⚠ smoke | ✅ 3 | `TrackerAndroidMappingGridTests` |

### Endpoints — unified (6)

| Endpoint | Happy | Edge | Auth/IDOR | Test file |
|---|---|---|---|---|
| `POST /unified/sources` | ✅ 4 | ✅ 2 | ❌ | `TrackingUnifiedDashboardTests` — both precedence levels, give-time-back |
| `POST /unified/pie-chart` | ✅ 5 | ✅ 3 | ❌ | `TrackingUnifiedDashboardTests` — partial displacement, label join, empty totals |
| `POST /unified/summary-cards` | ❌ | ❌ | ❌ | — **nothing at all** → `TEST-5` |
| `POST /unified/stacked-bars` | ❌ | ❌ | ❌ | — **nothing at all** → `TEST-5` |
| `POST /unified/timeline` | ✅ 3 | ✅ 2 | ❌ | `TrackingUnifiedDashboardTests` — lane exclusivity, id uniqueness, span rejection |
| `POST /unified/focus-metrics` | ✅ 1 | ✅ 1 | ❌ | `TrackingUnifiedDashboardTests:183` (device-change-is-not-a-switch) |

**No dashboard endpoint in the module has an IDOR or cross-user assertion.** They are all user-scoped by
`x.UserId == userId` in the handler *and* by the global filter, so the risk is low — but "low" here is
an inference from reading 21 handlers, not something a test states.

### Services / helpers / jobs / seeders / seams

| Unit | Tests | Notes |
|---|---|---|
| `DailyWindowSet` | 9 (indirect) | `TrackingDashboardDateRangeTests` — span semantics, band tiling, truncation, the year cap. Well pinned |
| `FocusMetricsCalculator` | 9 (indirect) | `TrackingFocusMetricsTests` — every definition including the no-measuring-across-the-night rule |
| `TimelineSegmentBuilder` | indirect | Only through the four timelines; the ±2-minute vote and `AbsorbContextSwitches`' three-ahead lookahead have no direct case |
| `UnifiedMinuteMerger` | 12 (indirect) | `TrackingUnifiedDashboardTests` — both rule levels, partial loss, the arithmetic identities |
| `SecondsAllocator` | indirect | Only via the identity assertions; the negative-shortfall branch is unreached by any test |
| `UnifiedLabelResolver` | 1 | Cross-source join only; the `(unknown)` blank fallback is unasserted |
| `UnifiedActivityLoader` / `UnifiedSpan` / `UnifiedLedger` | indirect | Through the six endpoints, four of which are tested |
| `PurgeExpiredActivityTrackingEntriesJobHandler` | 0 | Registration is pinned (`RetentionPurgeJob_IsRegisteredAndScheduled`); **the deletion predicate is not** — `docs/testing.md` already calls this a standing caveat → `TEST-7` |
| `TrackingScheduledJobsRegistrar` | 1 | Registered-after-boot only; the re-push-after-restart path is a documented caveat |
| `TrackerMappingActivityReferenceSource` | 5 | `TrackerPatternMappingActivityFkTests` (4) + `ActivityMergeTests` repoint; `SeamWiringTests` covers key registration |
| `IActivityTimeAttributionSink` / `ActivityTimeRecordedEvent` | 6 | `ExtensionActivityTrackingTests:198` end to end + `ActivityTimeAutomationTests` (5), including the exclusivity rule |
| `WebExtensionDataSeeder` | 0 | Dev fixture; documented caveat. Left as a caveat, not a `TEST-n` |
| EF configurations / partitioning | ❌ | Query filters pinned by `TrackingRouteSmokeTests` + `NamedQueryFilterTests`; **the partition key is not** → `TEST-8` |
| Schema / export classification | ✅ | `ModuleSchemaTests` (all five tables → `tracking`), `UserDataExportAndDeletionTests` |

### Honest coverage number

**About 55% of the module is meaningfully covered, and the distribution is extremely uneven.**

The parts that are covered are covered *well*: the span semantics, the fragmentation measures and the
unified overlap rule together account for 30 of the roughly 55 tracking test methods and they assert on
seconds. Those three are also the module's hardest logic, so the effort is in the right place — this is
the opposite of the usual "90% of the CRUD, none of the domain" shape.

What is missing is almost entirely **ingest and the long tail of read endpoints**. Of 38 endpoints, 6
have no test of any kind, 12 are exercised only by the route smoke theory, and only the desktop
heartbeat has an auth matrix. Weighted by endpoint, coverage is nearer 45%; weighted by risk, the
domain core pulls it to ~55%.

**The riskiest gaps, in order:**

1. **`web-extension/heartbeat` has no test at all** — and it is the one ingest endpoint whose
   authorization is wrong (`SEC-1`). A suite that tested it the way the desktop one is tested would
   have failed on the first auth case.
2. **The three summary-cards baselines** (`CQ-1`, `CQ-2`, `CQ-3`): one path 500s in production only,
   one compares a working-day figure against a round-the-clock one, one folds the weekday on UTC. The
   only baseline test asserts *scaling*, which is the half that is right.
3. **Ingest batch collisions** (`CQ-7`, `CQ-8`, `CQ-9`) — four distinct 500s on the write path with no
   test between them, on endpoints whose callers are unattended agents that will retry forever.
4. **The mapping FK ownership hole** (`SEC-2`), invisible behind an auth matrix that looks complete
   because it answers a different ownership question.
5. **The partition-key guard the docs say exists and does not** (`DOC-5`) — the failure it was meant to
   catch is silent by construction.

## 12. Missing tests — backlog

Prompt files live in `review/tracking/testingPrompts/`; each is self-contained for a context-less agent
and assumes only `_SHARED-CONTEXT.md`.

| ID | Gap | Findings it would pin | Prompt file |
|---|---|---|---|
| `TEST-1` | `web-extension/heartbeat` untested end to end: auth matrix, running-total upsert, minute alignment, `isFinal` latch | `SEC-1`, `CQ-5` | `WebExtensionHeartbeat.md` |
| `TEST-2` | `android/sync` ingest semantics: in-batch duplicates, cross-batch idempotency, offset-bearing timestamps, duration mismatch | `CQ-6`, `CQ-7`, `SEC-6` | `AndroidSyncIngest.md` |
| `TEST-3` | summary-cards baselines: `AllTime` must not 500, the lookback must honour the daily window, the weekday must fold on the user's clock | `CQ-1`, `CQ-2`, `CQ-3` | `SummaryCardsBaselineWindow.md` |
| `TEST-4` | mapping create/update with another user's `activityId` / `roleId` / `categoryId` | `SEC-2` | `MappingForeignKeyOwnership.md` |
| `TEST-5` | `unified/summary-cards` and `unified/stacked-bars` — the two merged dashboards with no test | — | `UnifiedSummaryCardsAndStackedBars.md` |
| `TEST-6` | `desktop/heartbeat` batch edges: unique-index collision inside one window, ignore-rule skip, first-match precedence, over-length strings, regex tolerance | `CQ-8`, `CQ-9`, `CQ-16`, `SEC-4` | `DesktopHeartbeatBatchCollisions.md` |
| `TEST-7` | the purge job's deletion predicate — and android's absence from it, pinned as a `KnownGap` until `SEC-3` is fixed | `SEC-3`, `AUDIT-1` | `RetentionPurgeJob.md` |
| `TEST-8` | the two partitioned tables keep their partition key — the guard `docs/testing.md` claims exists | `DOC-5` | `PartitionedTableGuard.md` |
| `TEST-9` | the two distinct-entry grids + `distinct-processes` + `fetch-categories-and-roles-by-pattern`: scoping, pagination bounds, filter modes | `CQ-12`, `CQ-13`, `CQ-14` | `DistinctEntryGrids.md` |
| `TEST-10` | `domain-details` and `process-details`: 404 shape, cross-user, the daily-window mask, the missing range cap | `PERF-3` | `DetailsEndpoints.md` |

### Deliberately not raised as gaps

- **`WebExtensionDataSeeder`** — dev-only fixture seeder, already a documented standing caveat, and
  `PerUserDefaultSeederTests` pins the chain it hangs off.
- **The registrar's restart path** — documented caveat; testing it needs a host restart inside a
  fixture, and the note in `docs/testing.md` about asserting on job keys rather than rows is right.
- **`ExecutablePath` ciphertext** — documented caveat; the column configuration is the only assertion
  today and reading raw SQL to prove it is a fair but low-value addition.
