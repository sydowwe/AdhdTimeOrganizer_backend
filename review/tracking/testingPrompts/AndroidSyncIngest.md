# TEST-2 — `POST /api/activity-tracking/android/sync` is tested only on its happy path

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today:** `ExtensionActivityTrackingTests.AndroidSync_WithExtensionToken_ReturnsOk:97` posts
one valid session and asserts 200. Nothing covers the parsing, the de-duplication or the validation
this endpoint does entirely by hand — including three live bugs (`CQ-6`, `CQ-7`, `SEC-5`).

## The endpoint

- **Route:** `POST /api/activity-tracking/android/sync` (group `ActivityTrackingAndroidGroup`
  = `/activity-tracking/android`, endpoint literal `/sync`)
- **Source:** `AdhdTimeOrganizer.Tracking/application/endpoint/android/command/AndroidSyncEndpoint.cs`
- **Validator:** `application/validator/android/AndroidSyncValidator.cs`
- **Entity:** `AndroidSessionData` — not partitioned; unique on
  `(UserId, DeviceId, PackageName, SessionStartUtc)`
- **Auth:** `[AllowExtensionClients]` + `Policies(PortalAuthorizationPolicies.ActivityTracking)`

### Request / response

```jsonc
{
  "deviceId": "pixel-8",              // required, ≤100
  "syncedUpToUtc": "2026-06-15T12:00:00Z",
  "sessions": [{
    "packageName": "com.slack",       // required, ≤255
    "appLabel": "Slack",              // required, ≤255
    "sessionStartUtc": "2026-06-15T09:00:00Z",  // STRING, parsed by the handler
    "sessionEndUtc":   "2026-06-15T09:10:00Z",  // STRING
    "durationSeconds": 600            // must be > 0 AND within 2s of (end - start)
  }]
}
```

Response: `{ accepted, duplicatesSkipped, syncedUpToUtc }`.

The handler **silently drops** (logs a warning, continues) any session that fails to parse, has
`end <= start`, or whose stated duration differs from `end - start` by more than 2 seconds. Those
dropped sessions are counted in **neither** `accepted` nor `duplicatesSkipped` — worth asserting,
because a client cannot otherwise tell.

## Scenarios

### Parsing — finding `CQ-6` (a real bug)

1. `Sync_ConvertsAnOffsetBearingTimestamp_RatherThanRelabellingIt`
   Send `sessionStartUtc: "2026-06-15T11:00:00+02:00"`, `sessionEndUtc: "2026-06-15T11:10:00+02:00"`,
   `durationSeconds: 600`. Assert the stored `SessionStartUtc` is **09:00Z**.
   **This fails today:** `DateTime.TryParse(..., DateTimeStyles.RoundtripKind)` keeps the local value
   and `DateTime.SpecifyKind(start, DateTimeKind.Utc)` stamps UTC on it **without converting**, so the
   row lands at 11:00Z — two hours late, on every dashboard.
   Fix is `DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal`. Either fix the endpoint
   and let the test pass, or mark it `[Trait("Status", "KnownGap")]` asserting 11:00Z and name `CQ-6`.
2. `Sync_AcceptsANakedTimestampAsUtc` — `"2026-06-15T09:00:00"` with no zone stores 09:00Z.
3. `Sync_SkipsAnUnparseableTimestamp_WithoutFailingTheBatch` — one bad session plus one good one:
   assert 200, `accepted == 1`, and one row.

### De-duplication — finding `CQ-7` (a real bug)

4. `Sync_IsIdempotentAcrossRequests` — post the same batch twice; assert the second answers
   `accepted: 0, duplicatesSkipped: 1` and that there is still one row. This is the invariant
   `domain-map.md` calls "what makes the sync endpoint's re-send idempotent" and it is untested.
5. `Sync_DeduplicatesWithinOneBatch`
   Post **one** request containing the same `(packageName, sessionStartUtc)` twice. Assert 200,
   `duplicatesSkipped == 1`, and one row.
   **This fails today** with a 500: `existingSet` is built only from rows already in the database
   (`AndroidSyncEndpoint.cs:78-85`), so both go into `AddRange` and violate the unique index — losing
   the whole batch, which the device then retries identically, forever.
6. `Sync_TreatsADifferentDeviceAsADifferentSession` — same package and start, different `deviceId`:
   two rows. (The unique key includes the device; the in-request dedup key does not — worth pinning
   which one wins.)

### Validation and bounds — finding `SEC-5`

7. `Sync_RejectsAZeroDuration_With400` and `Sync_RejectsAnEmptyPackageName_With400`.
8. `Sync_SkipsASessionWhoseDurationDisagreesWithItsSpan` — start/end ten minutes apart but
   `durationSeconds: 60`: assert `accepted == 0` and no row.
9. `Sync_RejectsAnAbsurdlyLongSession` — `[Trait("Status", "KnownGap")]`. Post a session spanning a
   year with a matching `durationSeconds`. Assert what happens **today** (it is accepted and stored),
   and document in the XML doc that `UnifiedActivityLoader.SliceIntoMinutes` will later allocate one
   record per minute of it — ~525 000 — for every unified dashboard request that user makes
   (`SEC-5`). **Do not** then call a unified endpoint in the same test; the point is to pin the
   accepted-at-ingest half cheaply. Remove the trait and invert the assertion when `SEC-5` is fixed.

### Auth

10. `Sync_WithWebJwt_Returns403` and `Sync_Unauthenticated_Returns401` — the policy is present on this
    endpoint, so both should pass today. Cheap, and they lock the policy in place.

## Fixtures / helpers to reuse

- Extension token: `ExtensionActivityTrackingTests:308-364` (`CreateUserWithExtensionAccess`,
  `ExtensionLoginSuccessAsync`) — the existing android case at `:97` already shows the exact call shape.
- Verification: `CreateDbContext()` → `Set<AndroidSessionData>()`. There is **no** query filter on this
  entity in a fixture-built context, so no `IgnoreQueryFilters()` is needed.
- Put these in a new `Endpoints/AndroidSyncIngestTests.cs` deriving from `AuthTestBase` (the ingest
  endpoints need an extension token, which `PostgresTestBase` cannot mint).
