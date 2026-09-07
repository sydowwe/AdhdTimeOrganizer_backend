# TEST-7 — the retention purge's deletion predicate is untested, and android is outside it

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today:** `TrackingRouteSmokeTests.RetentionPurgeJob_IsRegisteredAndScheduled:257` proves the
job is **registered** with the Scheduler after a normal boot. Nothing runs it.
`docs/testing.md:87-89` already flags this as a standing caveat: *"a wrong predicate there deletes
other users' data with nothing to catch it."* That is still true, and there is a second problem the
caveat does not mention — `AndroidSessionData` is not purged at all (`SEC-3`).

## What is under test

- **Handler:** `AdhdTimeOrganizer.Tracking/infrastructure/jobs/PurgeExpiredActivityTrackingEntriesJobHandler.cs`
  — a keyed `IScheduledJobHandler` (**not** a Quartz `IJob`; this slice references no Quartz), key
  `"Tracking.PurgeExpiredActivityTrackingEntries"`.
- **Options:** `infrastructure/persistence/retention/ActivityTrackingRetentionOptions.cs`
  (`RetentionOptions` with `KeepLastN = 0`), bound from the `"ActivityTrackingRetention"` config
  section in `Program.cs`. `Enabled` gates the whole run; `CutoffUtc()` comes from the base.
- **Behaviour:** two `ExecuteDeleteAsync` calls with `IgnoreQueryFilters()` on
  `RecordDate < cutoffDate`, over `DesktopActivityEntry` and `WebExtensionActivityEntry`. Deliberately
  cross-user — it is a background sweep with no ambient caller.

## Running the job in a test

Do not boot Quartz. Open a scope, build the handler with `ActivatorUtilities.CreateInstance<T>` and
hand it a hand-built `ScheduledJobContext` (the handler reads nothing off it). The pattern is
`RoutineTodoListResetJobHandlerTests.cs:107-121` in this same suite — copy it.

To control `Enabled` / `RetentionYears`, either inject an `IOptions<ActivityTrackingRetentionOptions>`
built with `Options.Create(new ActivityTrackingRetentionOptions { … })` directly into
`ActivatorUtilities.CreateInstance`, or use `Fixture.CreateFactory(roles: null, configureServices: …)`
to replace the registration. The former is simpler and enough here.

⚠ The handler takes a plain `DbContext` (the slice never sees `AppDbContext`). A fixture-built context
has **no user query filter**, which is what lets you assert the cross-user sweep.

## Scenarios

1. `Purge_DeletesRowsOlderThanTheCutoff_AndKeepsTheRest`
   Seed, for the test user, four rows per ledger: well before the cutoff, one day before, one day
   after, and today. Run with a short `RetentionYears`. Assert exactly the first two are gone from
   both tables.
   ⚠ The cutoff compares `RecordDate` (a `DateOnly` derived from `WindowStart`) — seed instants, not
   dates, and never set `RecordDate` directly.
2. `Purge_ReachesEveryUsersRows`
   Seed an old row for a second user (see `_SHARED-CONTEXT.md`) and assert it is deleted too. This is
   the `IgnoreQueryFilters()` half, and the one the caveat calls out: a predicate that quietly picked
   up an ambient user would pass every other test in this file.
   Its mirror: `Purge_LeavesRecentRowsOfOtherUsersAlone`.
3. `Purge_DoesNothingWhenDisabled` — `Enabled = false`; assert every row survives and the run is a
   no-op.
4. `Purge_IsIdempotent` — run twice; the second deletes nothing and does not throw.
5. `Purge_DeletesAcrossPartitions` — seed old rows in two different yearly partitions (both tables are
   RANGE-partitioned on `record_date`, y2026–y2030 plus a default) and assert both go. A predicate that
   works on one partition and silently misses another is the failure mode partitioning introduces.
6. `Purge_DoesNotTouchAndroidSessionData` — **`[Trait("Status", "KnownGap")]`, finding `SEC-3`.**
   Seed a decade-old `AndroidSessionData` row, run the job, assert it is **still there**, and say in
   the XML doc that this pins a gap rather than a rule: the third ledger has no retention path at all
   (no purge, no partitioning, and not covered by the two-year `CurrentPartitionDate` read floor that
   applies to `WebExtensionActivityEntry` only). Invert the assertion and drop the trait when `SEC-3`
   is fixed.
7. `Purge_LogsTheRun` — optional. Today the handler logs only when something was deleted
   (`:52-55`), so a run that deleted nothing leaves no evidence the GDPR control executed
   (`AUDIT-1`). If you add an unconditional Information log, pin it with a captured logger.

## Also worth pinning here

8. `RetentionOptions_AndTheReadFilter_AgreeOnTheHorizon` — `AppDbContext.CurrentPartitionDate` is a
   hard-coded `AddYears(-2)` while the purge uses configured `RetentionYears`. Setting
   `RetentionYears` below 2 makes rows deleted while still nominally readable, or retained while
   invisible (`MIG-6`). A one-line assertion that `RetentionYears >= 2` in the bound options, or that
   the two agree, is cheap insurance.

## What not to duplicate

- Registration/scheduling is already pinned by `TrackingRouteSmokeTests:257` and `ModuleWiringTests`.
- The registrar's **restart** path (does `StartAsync` re-push the trigger after the RAM job store drops
  it) is a documented caveat needing a host restart inside a fixture. If you add it, assert on
  registered job keys, not on rows — `docs/testing.md:82-86` is right about that.
