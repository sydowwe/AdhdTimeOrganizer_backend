# TEST-7 — Tracking mapping CRUD auth matrix

## Context

Same infra as the other portal integration tests: `PostgresTestBase`, `CreateUserRoleClient()`,
`Sydowwe.Framework.Testing.baseTests`'s 13 abstract CRUD/auth bases, `ActivityEndpointTests.cs` as the
reference pattern (subclass framework bases + `Seed.SecondUserAsync` for the cross-user id).

## What exists today (do not duplicate)

`Endpoints/TrackingRouteSmokeTests.cs` already covers, for `AdhdTimeOrganizer.Tracking`:
- Route-registration smoke tests for all desktop/web-extension/android dashboard endpoints and the
  4 grid routes (2 pattern-mapping settings grids + 2 distinct-entry grids).
- `WebExtensionActivityEntry_KeepsItsCombinedQueryFilter` — proves the hand-written filter combining
  per-user scoping + partition-date bound survives on `WebExtensionActivityEntry` (excluded from the
  generic `IEntityWithUser` filter, so nothing else catches a regression here).
- `PartitionedTrackingTables_KeepTheirPartitionKey` — proves `DesktopActivityEntry` and
  `WebExtensionActivityEntry` keep their range-partition annotation.
- `RetentionPurgeJob_IsRegisteredAndScheduled` — proves the GDPR retention-purge job handler is in DI
  and gets scheduled on boot.
- Ingest auth (extension-client token + `ActivityTracking` policy) is covered separately in
  `Endpoints/ExtensionActivityTrackingTests.cs` — don't duplicate that either.

None of the above is a CRUD auth matrix. Both mapping families have full CRUD:
`application/endpoint/desktop/command/{Create,Update,Delete}TrackerDesktopMappingEndpoint.cs` and
`application/endpoint/android/command/{Create,Update,Delete}TrackerAndroidMappingEndpoint.cs`. **No
auth coverage exists** — no test proves a cross-user mapping id 404s on Update/Delete, or that the two
settings grids (`GridTrackerDesktopMappingEndpoint`, `GridTrackerAndroidMappingEndpoint`) scope to the
caller.

## What to write

Add `Endpoints/TrackingMappingCrudAuthMatrixTests.cs` subclassing the framework bases for
Create/Update/Delete/Filter (or whichever grid base matches `GridTrackerDesktopMappingEndpoint`'s
actual base class — check it inherits `BaseGridEndpoint` or similar before picking the test base)
against both `TrackerDesktopMapping` and `TrackerAndroidMapping`. Follow `ActivityEndpointTests.cs`'s
`Seed.SecondUserAsync` idiom. Confirm whether these two entities are `IEntityWithUser` before choosing
`UnauthorizedStatus`.

## Out of scope

Don't re-test dashboard routing, the `WebExtensionActivityEntry` combined filter, partitioning, the
retention job, or the ingest endpoints — all already covered as listed above. This task is CRUD auth on
the two mapping entities only.
