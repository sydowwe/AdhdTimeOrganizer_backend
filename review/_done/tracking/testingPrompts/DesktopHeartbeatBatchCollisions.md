# TEST-6 — `POST /api/activity-tracking/desktop/heartbeat` batch edges and pattern resolution

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today:** `ExtensionActivityTrackingTests` covers this route well on **auth** (four cases) and
once **end to end** (`Heartbeat_MappedEntry_AttributesTimeAndCompletesPlannerTask:198`, the seam
guard). Nothing covers what the handler does with an awkward batch — four distinct 500s (`CQ-8`,
`CQ-9`) and two undefined behaviours (`CQ-16`, `SEC-4`) live in that gap, on a client that retries
automatically and forever.

## The endpoint

- **Route:** `POST /api/activity-tracking/desktop/heartbeat` (group `ActivityTrackingDesktopGroup`,
  literal `/heartbeat`)
- **Source:** `AdhdTimeOrganizer.Tracking/application/endpoint/desktop/command/DesktopActivityHeartbeatEndpoint.cs`
- **Validator:** `application/validator/desktop/DesktopActivityHeartbeatValidator.cs`
- **Auth:** `[AllowExtensionClients]` + `Policies(PortalAuthorizationPolicies.ActivityTracking)`
- **Unique index:** `desktop_activity_entry (user_id, window_start, record_date, process_name, window_title)`
  — note `active_monitor` is **not** in it.

```jsonc
{
  "windowStart": "2026-06-15T09:00:00Z",   // must have Second == 0 && Millisecond == 0
  "entries": [{
    "processName": "chrome.exe", "productName": "Google Chrome",
    "windowTitle": "GitHub", "executablePath": "C:/…/chrome.exe",
    "isFullscreen": false, "activeSeconds": 40, "backgroundSeconds": 0,
    "isPlayingSound": false, "activeMonitor": 1
  }]
}
```

Response is a bare `int`: entries **persisted** (entries matched by an `isIgnored` mapping are skipped
and not counted; entries with both counters zero are filtered before the loop).

## Scenarios

### Batch collisions — finding `CQ-8` (a real 500)

1. `Heartbeat_WithTwoWindowsOfOneProcessSharingATitle_DoesNotFailTheBatch`
   One window, two entries with the same `processName` **and** `windowTitle`, differing only in
   `activeMonitor` (1 and 2) — which is what one application open on two monitors looks like.
   Expect 200 and the seconds preserved (one summed row, or two rows if the index is widened).
   **Fails today with a 500**: every entry in a batch shares `windowStart`, so the two collide on the
   unique index and the whole window is lost — and the agent re-sends it identically.
   Fix is either grouping in the handler or adding `active_monitor` to the index (see `MIG-2` — the
   table is partitioned, so prefer the handler fix). Pin the current behaviour under
   `[Trait("Status", "KnownGap")]` if you are not fixing it in this pass.
2. `Heartbeat_ResentWindow_DoesNotDuplicate` — post the identical batch twice. Assert what happens; a
   500 on the second call is the same defect seen from the retry side, and is what an agent recovering
   from a timeout actually does.

### Length validation — finding `CQ-9`

3. `Heartbeat_WithAnOverLongWindowTitle_Returns400` — `windowTitle` of 2000 characters
   (column is `varchar(1024)`). **Fails today with a 500** (Postgres `22001`); the validator has no
   `MaximumLength` rule on any string. Same for `processName` / `productName` at >255.
4. `Heartbeat_WithA1024CharacterNonAsciiTitle_DoesNotFailTheIndex` — 1024 CJK characters is ~3 kB,
   over Postgres' ~2704-byte btree tuple limit, and `window_title` is in the unique index. Optional but
   cheap, and it is the half of `CQ-9` that survives a naive `MaximumLength(1024)` fix.

### Pattern resolution — findings `CQ-16`, `SEC-4`

5. `Heartbeat_SkipsAnEntryMatchedByAnIgnoreRule` — seed a mapping with `IsIgnored = true` matching
   `chrome.exe`; post two entries, one matching. Assert the response count is 1 and only the other row
   exists. This is a documented behaviour (`summary.md`) with no test.
6. `Heartbeat_WithTwoMatchingMappings_ResolvesDeterministically`
   Seed two active mappings that both match one entry (e.g. one on `processName`, one on
   `windowTitle`) pointing at different activities. Assert which one wins.
   **Undefined today**: `mappings.FirstOrDefault(m => MatchesPattern(m, entry))` (`:64`) takes whatever
   order Postgres returned. Write the test to the intended rule — most-specific-first, then lowest id —
   and fix `:58-60` to order accordingly; or pin the current non-determinism as a `KnownGap` naming
   `CQ-16`. **Do not** write an assertion that happens to match today's arbitrary order without saying
   so; that is worse than no test.
7. `Heartbeat_MatchesEachPatternMode` — a `[Theory]` over `PatternMatchType`
   `Exact` / `Contains` / `Wildcard` / `Regex` against one entry, asserting attribution happens. The
   four modes are the module's core matching rule and only `Exact` is exercised anywhere today
   (`ExtensionActivityTrackingTests:219`).
8. `Heartbeat_WithACatastrophicRegexMapping_DoesNotHangTheRequest` — finding `SEC-4`. Seed a mapping
   with `WindowTitleMatchType = Regex` and pattern `(a+)+$`, post an entry whose title is 40 `a`s
   followed by `!`, and assert the request completes within a couple of seconds. **Hangs today**:
   `Regex.IsMatch` at `:150` has no `matchTimeout` and is not `NonBacktracking`. Give the test its own
   `HttpClient` timeout so a failure is a failed assertion rather than a hung suite; if that proves
   awkward, assert directly against the static `MatchesPattern` helper instead of over HTTP.

### Attribution

9. `Heartbeat_DoesNotAttributeAnEntryWithNoMatchingMapping` — raw row written, no `ActivityHistory`
   row, no event. The positive case exists at `:198`; the negative one does not, and "attributes
   everything" is a plausible regression.
10. `Heartbeat_AttributesActiveSecondsOnly` — an entry with `activeSeconds: 0, backgroundSeconds: 40`
    matched by an activity mapping: assert the raw row is stored and the attribution is **0 seconds**
    (`:90` passes `entry.ActiveSeconds`). Whether that is right is a product question; pin it either
    way, because it is currently invisible.

## Fixtures / helpers to reuse

- Extension token + the full seed block (role → activity → mapping → planner task):
  `ExtensionActivityTrackingTests:198-300`. Add these cases to that file or to a new
  `Endpoints/DesktopHeartbeatIngestTests.cs : AuthTestBase`.
- Seed mappings with `UserId` set **explicitly** (`TrackingMappingCrudAuthMatrixTests:75`), `IsActive
  = true` (the handler filters on it), and one target group only.
- Verify raw rows through `CreateDbContext()` → `Set<DesktopActivityEntry>()`; there is no partition
  date filter on this entity, unlike the web-extension one.
