# TEST-1 — `POST /api/activity-tracking/web-extension/heartbeat` has no test of any kind

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today:** the string `activity-tracking/web-extension/heartbeat` appears in **no** file in
`AdhdTimeOrganizer.IntegrationTests`. Its two sibling ingest endpoints are covered by
`ExtensionActivityTrackingTests` (desktop: five cases including the full auth matrix; android: one
happy case). This is the highest-frequency write path in the module and the only one with nothing.

## The endpoint

- **Route:** `POST /api/activity-tracking/web-extension/heartbeat` (declared with the full literal —
  it is the one ingest endpoint not in a `Group`)
- **Source:** `AdhdTimeOrganizer.Tracking/application/endpoint/webExtension/command/WebExtensionDataHeartbeatEndpoint.cs`
- **Validator:** `application/validator/webExtension/WebExtensionHeartbeatValidator.cs`
- **Entity:** `WebExtensionActivityEntry` (partitioned; `RecordDate` derives from `WindowStart`)
- **Auth today:** `[AllowExtensionClients]` and **nothing else** — no
  `Policies(PortalAuthorizationPolicies.ActivityTracking)`, unlike the other two. That is finding
  `SEC-1`.

### Request / response

```jsonc
// WebExtensionHeartbeatRequest — every member is `required`
{
  "heartbeatAt":  "2026-06-15T09:01:00Z",
  "windowStart":  "2026-06-15T09:00:00Z",   // must be minute-aligned (Ticks % TicksPerMinute == 0)
  "windowMinutes": 1,                        // validator: must equal 1
  "isFinal": false,
  "activities": [                            // WebExtensionEntryDto
    { "domain": "github.com", "url": "https://github.com/x", "activeSeconds": 40, "backgroundSeconds": 0 }
  ]
}
```

Response is a bare `int` — the count of processed activities. Note the handler **skips** any activity
with `activeSeconds == 0 && backgroundSeconds == 0` before counting it.

## Scenarios

### Auth (this is the point of the file)

1. `Heartbeat_WithExtensionToken_ReturnsOk` — mirror
   `ExtensionActivityTrackingTests.Heartbeat_WithExtensionToken_ReturnsOk:60` against this route.
2. `Heartbeat_WithExtensionToken_ButNoActivityTrackingRole_Returns403` — mirror `:120`.
   **This fails today**: without the policy, `[AllowExtensionClients]` plus the configurator's default
   `Roles("User","Admin","Root")` lets the call through with 200.
3. `WebJwt_CannotAccess_ActivityTrackingPolicy_Returns403` — mirror `:142` using
   `CreateUserRoleClient()`. **Also fails today** (returns 200).
4. `Heartbeat_Unauthenticated_Returns401` using `CreateClient()`.

Cases 2 and 3 are the specification, not the current behaviour. Two options, both acceptable:

- **Preferred:** fix `SEC-1` first (add `Policies(PortalAuthorizationPolicies.ActivityTracking);` to
  `Configure()`) and write all four as ordinary passing tests.
- If the fix is out of scope for you, write 2 and 3 with `[Trait("Status", "KnownGap")]`, assert the
  **current** `OK`, and name `SEC-1` in the XML doc.

### Ingest behaviour

5. `Heartbeat_InsertsOneRowPerDomain_WithRecordDateDerivedFromWindowStart` — post two domains in one
   window; assert two rows, and that `RecordDate == DateOnly.FromDateTime(WindowStart)` on both. The
   derived partition key is an invariant (`domain-map.md` → Invariants) and nothing pins it today.
6. `Heartbeat_TakesRunningTotals_NotDeltas` — post the same `(windowStart, domain)` twice with
   `activeSeconds` 20 then 45; assert **one** row with `ActiveSeconds == 45`, not 65. The extension
   sends running totals and the handler assigns rather than adds
   (`WebExtensionDataHeartbeatEndpoint.cs:45-46`) — this is the contract most likely to be "fixed"
   into a bug.
7. `Heartbeat_KeepsIsFinalOnceSet` — post with `isFinal: true`, then again with `isFinal: false`;
   assert the row stays `IsFinal == true` (the latch at `:53-54`).
8. `Heartbeat_KeepsTheLastNonEmptyUrl` — post with a url, then with `url: ""`; assert the stored
   `Url` is still the first one (`:49-50`).
9. `Heartbeat_SkipsZeroSecondActivities` — post one activity with both counters 0 alongside a real
   one; assert the response count is 1 and only one row exists.
10. `Heartbeat_RejectsAnUnalignedWindowStart_With400` — `windowStart` at `09:00:30Z`. This is the rule
    `WebExtensionActivityEntry`'s XML doc calls load-bearing for the timeline, enforced **only** here.
11. `Heartbeat_RejectsWindowMinutesOtherThanOne_With400`.
12. `Heartbeat_StampsTheCallersUserId` — seed a second user, post as the first, assert the row's
    `UserId` is the caller's. (Cross-user reads are already covered by
    `TrackingRouteSmokeTests.WebExtensionActivityEntry_KeepsBothOfItsQueryFilters`; this is the write
    side.)

### Concurrency — finding `CQ-5`

13. `Heartbeat_ConcurrentPostsForOneWindow_DoNotFailTheBatch` — fire two identical posts for the same
    `(windowStart, domain)` with `Task.WhenAll`, assert both are non-500 and exactly one row exists.
    The handler does read-then-write with no unique-violation handling, so this **can** produce a 500
    on `ix_web_extension_activity_entry_user_id_window_start_domain_re`. If it proves too timing-
    dependent to be reliable, drop it rather than leaving a flaky test — say so in a comment and leave
    `CQ-5` to the code fix.

## Fixtures / helpers to reuse

- `AuthTestBase` + `CreateUserWithExtensionAccess` / `ExtensionLoginSuccessAsync` from
  `ExtensionActivityTrackingTests:308-364`. Put the auth cases in that file (it already owns the
  extension flow and its `CreateUserWithExtensionAccess` is private) and the ingest cases in a new
  `WebExtensionHeartbeatTests : PostgresTestBase` — those need only `CreateUserRoleClient()` once
  `SEC-1` is fixed, or the extension client if it is not.
- Verify through `CreateDbContext()` + `Set<WebExtensionActivityEntry>().IgnoreQueryFilters()` — the
  fixture context applies no user filter, but the **date** filter still applies, so keep seeded
  instants inside the last two years.
