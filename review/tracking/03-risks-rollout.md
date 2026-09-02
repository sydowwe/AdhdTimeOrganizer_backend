# Tracking — Risks & Rollout (03)

Point-in-time snapshot, 2026-09-01. Synthesis of [`02-findings.md`](02-findings.md) and
[`01-testing.md`](01-testing.md); IDs are not restated, only ranked and sequenced.

## 11. Risk-ranked issue list

Every 🟠 from file 02, most severe first. No 🔴 was found — nothing here corrupts data at rest or
exposes another user's records to a reader.

| # | Sev | Issue (one line) | ID | File |
|---|---|---|---|---|
| 1 | 🟠 | A mapping may target another user's activity, and every later heartbeat then attributes the caller's time to it | `SEC-2` | `application/dto/request/{desktop,android}/CreateTracker*MappingRequest.cs` |
| 2 | 🟠 | ✅ FIXED 2026-09-03 — The web-extension heartbeat is missing the `ActivityTracking` policy the other two ingest routes carry | `SEC-1` | `application/endpoint/webExtension/command/WebExtensionDataHeartbeatEndpoint.cs:10` |
| 3 | 🟠 | Android usage data has no retention path at all — the GDPR purge covers two of three ledgers | `SEC-3` | `infrastructure/jobs/PurgeExpiredActivityTrackingEntriesJobHandler.cs:42` |
| 4 | 🟠 | `AllTime` baselines pass `DateTime.MinValue` to a `timestamptz` column — 500 in production, impossible to reproduce in tests | `CQ-1` | three `*SummaryCardsEndpoint.cs` |
| 5 | 🟠 | Summary-cards baselines ignore the per-day window the current period applies, inflating every comparison | `CQ-2` | three `*SummaryCardsEndpoint.cs` |
| 6 | 🟠 | An unbounded android session (accepted at ingest) expands to one record per minute in the unified loader | `SEC-5` | `application/validator/android/AndroidSyncValidator.cs`, `service/unified/UnifiedActivityLoader.cs:311` |
| 7 | 🟠 | ✅ FIXED 2026-09-03 — User-authored regexes run with no timeout on the once-a-minute ingest path | `SEC-4` | `endpoint/desktop/command/DesktopActivityHeartbeatEndpoint.cs:150` |
| 8 | 🟠 | Android sync mislabels an offset-bearing timestamp as UTC instead of converting it | `CQ-6` | `endpoint/android/command/AndroidSyncEndpoint.cs:37` |
| 9 | 🟠 | Duplicated session inside one sync batch → unique violation → the whole batch 500s and the device retries forever | `CQ-7` | `endpoint/android/command/AndroidSyncEndpoint.cs:88` |
| 10 | 🟠 | Two same-titled windows of one process in one heartbeat → unique violation → the window is lost | `CQ-8` | `endpoint/desktop/command/DesktopActivityHeartbeatEndpoint.cs:69` |
| 11 | 🟠 | Web-extension heartbeat is N+1 with a read-then-write race on its own unique index | `CQ-5` | `endpoint/webExtension/command/WebExtensionDataHeartbeatEndpoint.cs:31` |
| 12 | 🟠 | Android summary cards fold the weekday and the all-time day span on UTC, not the user's clock | `CQ-3` | `endpoint/android/query/AndroidSummaryCardsEndpoint.cs:118` |
| 13 | 🟠 | Android pie chart filters by default and drops the remainder — slices no longer sum to the printed total | `CQ-4` | `endpoint/android/query/AndroidPieChartEndpoint.cs:61` |
| 14 | 🟠 | No length validation on ingest or mapping writes — over-length strings 500 instead of 400 | `CQ-9` | four validators |
| 15 | 🟠 | A mapping's target cannot be changed through the API; update also drops the target rules | `CQ-10` | `dto/request/{desktop,android}/UpdateTracker*MappingRequest.cs` |
| 16 | 🟠 | Duplicate-pattern create/update returns 500 instead of a 409 the UI can act on | `CQ-11` | `endpoint/{desktop,android}/command/*Mapping*.cs` |
| 17 | 🟠 | `PatternMatchType.Regex` is a real regex at ingest and a SQL `LIKE` in every grid filter | `CQ-12` | `framework/.../QueryableEntityExtensions.cs:170` |
| 18 | 🟠 | Every per-source dashboard materialises the whole envelope of raw rows and aggregates in memory | `PERF-1` | 15 dashboard endpoints |
| 19 | 🟠 | Android stacked bars scans every session once per bucket — ~350 M comparisons on a year span | `PERF-2` | `endpoint/android/query/AndroidStackedBarsEndpoint.cs:63` |
| 20 | 🟠 | `process-details` has no maximum range; its web-extension twin caps at 31 days | `PERF-3` | `validator/desktop/DesktopProcessDetailsValidator.cs` |
| 21 | 🟠 | Distinct-entry grids `GROUP BY` the entire ledger, twice per page, with no date bound | `PERF-4` | `FetchTableDistinct{Desktop,Android}Entry.cs` |
| 22 | 🟠 | The docs credit a partition-key guard that does not exist — the module's loudest silent failure is unpinned | `DOC-5` | `docs/testing.md:176`, `TrackingRouteSmokeTests.cs:35` |
| 23 | 🟠 | Docs assert an ingest auth rule the code breaks | `DOC-1` | `docs/domain-map.md:189` |
| 24 | 🟠 | Docs assert baseline parity between summary-cards and focus-metrics that does not hold | `DOC-2` | `docs/domain-map.md:138` |
| 25 | 🟠 | Retention is documented as the slice's storage-limitation control while one ledger sits outside it | `DOC-3` | `docs/summary.md:35` |

**Suggested order of work.** `SEC-2` and `SEC-1` first — they are small, self-contained and are the two
that are about authorization rather than arithmetic. Then the four ingest 500s (`CQ-6` … `CQ-9`) as one
pass, because they share a test file (`TEST-2`, `TEST-6`) and all four are on unattended,
infinitely-retrying clients. Then the baseline cluster (`CQ-1`, `CQ-2`, `CQ-3`, `PERF-6`) as **one**
refactor into a shared base per `CQ-18`, rather than three parallel patches. `SEC-3` and its migration
concerns (`MIG-4`) can run independently. The `PERF-1` rewrite is the largest item and the least urgent:
it is a scaling problem, not a wrong answer.

## 10. Migration / rollout risks

| Risk | Likelihood | Mitigation | ID |
|---|---|---|---|
| **The partitions run out at 2030-12-31.** `desktop_activity_entry` and `web_extension_activity_entry` define y2026–y2030 plus a `default` partition (`DesktopActivityEntryConfiguration.cs:42-50`). Rows for 2031 land in `default` and stay there; attaching a real y2031 partition later requires moving them out under an `ACCESS EXCLUSIVE` lock on both tables | Certain, dated | Add the next years well before the boundary — a no-op migration while the years are empty. Put a reminder against 2029 rather than trusting the default partition to be noticed | `MIG-1` |
| **Any schema change to the two partitioned ledgers locks every partition.** Fixing `CQ-8` by widening the unique index to include `active_monitor`, or adding a `CHECK`, rewrites/validates each partition in turn | Medium — one of the proposed fixes wants it | Prefer the application-side fix for `CQ-8` (group colliding entries before `Add`) precisely to avoid this. If an index change is unavoidable, build it `CONCURRENTLY` per partition outside EF | `MIG-2` |
| **Fixing `SEC-1` starts returning 403 to callers that work today.** Adding the `ActivityTracking` policy means any client posting web-extension heartbeats with a plain web JWT, or with an extension token lacking the role, breaks at deploy | High — that is the point of the fix, and nothing tells you who is doing it | Log-only first: add the policy behind a temporary log line counting refusals for one release, or check access logs for non-extension callers of the route before shipping. Coordinate with the browser-extension release train. **Shipped 2026-09-03 as a hard 403, not log-only — this risk is now live and un-mitigated; the browser-extension release train still needs telling** | `MIG-3` |
| **Adding android to the retention purge deletes on the first run.** The job runs 03:30 daily and `ExecuteDeleteAsync` is immediate and unaudited; the first run after the fix removes everything past the configured `RetentionYears` in one statement | High if shipped unannounced | Ship the predicate with retention disabled or a deliberately long floor, verify the row count in staging against a `SELECT count(*)` first, and add the index noted in `PERF-1`'s index list so the sweep does not scan. Announce to users the way any retention change should be | `MIG-4` |
| **Tests and production disagree about Npgsql date-time semantics.** `PostgresContainerFixture.cs:26` enables `Npgsql.EnableLegacyTimestampBehavior`; `Program.cs` does not. `CQ-1` is the first bug this hid; it will not be the last, and it hides them *in the direction of false confidence* | Certain, ongoing | Remove the switch from the fixture and fix whatever fails — that is the whole point of an integration suite over a real container. If it cannot be removed now, add a `ModuleWiringTests` assertion that pins the divergence so it is at least visible | `MIG-5` |
| **`WebExtensionActivityEntry`'s two-year read floor is not a retention boundary.** Any change to `RetentionYears` below 2 silently makes the purge and the query filter disagree — rows readable but deleted, or retained but invisible | Low, but silent | Derive `CurrentPartitionDate` from `ActivityTrackingRetentionOptions` rather than the hard-coded `AddYears(-2)`, or assert the relationship in a test | `MIG-6` |
| **Drift risk: no sandbox or duplicate copy of this module exists.** The split is finished; the only cross-repo hazard is `framework/` being a submodule, which `CQ-12`'s fix would touch — commit and push there **first**, then the gitlink here | Low | Standard two-repo procedure from CLAUDE.md | `MIG-7` |

## Audit-log gaps

Auditing exists in `Sydowwe.Framework` but **is not wired up in this host** (CLAUDE.md), so nothing
below is producing a missing row today. They are the decisions that will be wrong the day it is turned
on, and two of them are cheap to make now.

| Gap | Detail | ID |
|---|---|---|
| The retention purge bypasses the interceptor | `PurgeExpiredActivityTrackingEntriesJobHandler.cs:42-50` uses `ExecuteDeleteAsync` with `IgnoreQueryFilters` — deliberately, and documented in the handler's own comment: both entities are per-heartbeat technical ledgers, not user-authored records. **Accepted, previously known.** What is missing is the compensating record: the job logs a count only when something was deleted (line 52), so a run that deletes nothing leaves no trace that the GDPR control executed. Log the run unconditionally at Information with the cutoff date | `AUDIT-1` |
| `AndroidSessionData` has no audit classification | `domain/model/entity/AndroidSessionData.cs` — no `[AuditIgnore]` on `PackageName` / `AppLabel` / `DeviceId`, while `DesktopActivityEntry` marks `WindowTitle` and `ExecutablePath` and `WebExtensionActivityEntry` marks `Domain` and `Url`. Turning auditing on would start copying a person's app-usage history into the audit table — the one place with no retention policy at all. Mark the whole entity `[NoAudit]`: it is a technical ledger, like its two peers | `AUDIT-2` |
| The two mapping tables have no annotations either way | `TrackerDesktopMappingByPattern` / `TrackerAndroidMappingByPattern` are genuinely user-authored rules and *should* be audited when auditing lands — unlike the ledgers. Worth stating explicitly rather than leaving to the default, because they are the only entities in the slice where the default is the right answer and someone will "tidy" them into `[NoAudit]` alongside the ledgers | `AUDIT-3` |
| No audit trail on the attribution write | `DesktopActivityHeartbeatEndpoint.cs:90` writes through `IActivityTimeAttributionSink` into History's ledger on behalf of an automated client, and `SEC-2` shows that write can currently land on another user's activity. Whatever auditing decision History makes for `ActivityHistory`, this is the write path that most needs a "who caused this" record — the caller is an agent, not a person at a screen | `AUDIT-4` |
