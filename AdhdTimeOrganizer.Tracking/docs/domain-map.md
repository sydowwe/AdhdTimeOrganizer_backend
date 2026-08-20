# AdhdTimeOrganizer.Tracking — Domain Map

Navigation index. Read `summary.md` first; open only the rows you need.

## Entities

| Entity | Table | Notes | Path |
|---|---|---|---|
| `DesktopActivityEntry` | `desktop_activity_entry` | Per-heartbeat desktop window. **RANGE-partitioned** on `RecordDate`. `ExecutablePath` is an `EncryptedColumn`. | `domain/model/entity/activityTracking/desktop/` |
| `WebExtensionActivityEntry` | `web_extension_activity_entry` | Per-minute browser window. **RANGE-partitioned** on `RecordDate`; `RecordDate` derives from `WindowStart` in the initializer so the two cannot diverge. `WindowStart` must be minute-aligned — the timeline endpoint stitches windows by `WindowStart == previous.EndedAt`. | `domain/model/entity/activityTracking/` |
| `AndroidSessionData` | `android_session_data` | Per-session android usage, deduplicated on sync by `(DeviceId, PackageName, StartedAt)`. | `domain/model/entity/activityTracking/` |
| `TrackerDesktopMappingByPattern` | `tracker_desktop_mapping_by_pattern` | Pattern → `Activity`/`Role`/`Category`, or `IsIgnored`. Matched in-memory by `MatchesPattern`. **Many** patterns to one activity; `activity_id` is `Cascade`. The unique key is the pattern: `(UserId, ProcessName, ProductName, WindowTitle)`, `NULLS NOT DISTINCT`. | `domain/model/entity/activityTracking/desktop/` |
| `TrackerAndroidMappingByPattern` | `tracker_android_mapping_by_pattern` | Android equivalent; unique on `(UserId, PackageName, AppLabel)`. | `domain/model/entity/activityTracking/android/` |

## EF configurations

`infrastructure/persistence/configuration/activityTracking/` — all five. Three of them
(`DesktopActivityEntryConfiguration`, `WebExtensionActivityEntryConfiguration`,
`AndroidSessionDataConfiguration`) came from the host's `configuration/activityHistory/` folder, which
never matched their domain.

Applied by `AppDbContext.ApplyHostConfigurations` via
`ApplyConfigurationsFromAssembly(typeof(DesktopActivityEntryConfiguration).Assembly)`.

## Endpoints — `application/endpoint/activityTracking/`

Routes come from the five groups in `application/endpointGroups/`, so the literal in `Post(...)` is
often only the tail. Check the group before assuming a path.

| Area | Route prefix | Endpoints |
|---|---|---|
| Desktop ingest | `/activity-tracking/desktop` | `DesktopActivityHeartbeatEndpoint` (`/heartbeat`) — ⚠ ingest only, see `summary.md` |
| Desktop dashboards | `/activity-tracking/desktop` | pie-chart, stacked-bars, summary-cards, timeline, focus-metrics, process-details, distinct-processes, `gird` |
| Desktop mappings | `/activity-tracking/desktop/settings` | create / update / delete / `gird`, plus `fetch-categories-and-roles-by-pattern` |
| Web-extension ingest | `/activity-tracking/web-extension` | `WebExtensionDataHeartbeatEndpoint` (`/heartbeat`) |
| Web-extension dashboards | `/activity-tracking/web-extension` | pie-chart, stacked-bars, summary-cards, timeline, focus-metrics, domain-details |
| Android ingest | `/activity-tracking/android` | `AndroidSyncEndpoint` (`/sync`) |
| Android dashboards | `/activity-tracking/android` | pie-chart, stacked-bars, summary-cards, timeline, focus-metrics, `gird` |
| Android mappings | `/activity-tracking/android/settings` | create / update / delete / `gird` |
| Unified dashboards | `/activity-tracking/unified` | sources, pie-chart, summary-cards, stacked-bars, timeline, focus-metrics — see **The unified dashboards** below |

**Request shape (all twenty-one dashboards; the six unified ones add `sources` and nothing else):**
`DateRangeAndTimeRangeDto`
(`application/dto/request/DateRangeAndTimeRangeDto.cs`) — an inclusive `dateFrom`/`dateTo` day span
plus `from`/`to`, which are a **time-of-day window repeated on each day of the span**, not the ends of
the range. It resolves through `domain/helper/DailyWindowSet.cs`, which also lays out the stacked-bars
bands. Read the span continuously instead and every endpoint still returns a well-formed 200 with
silently inflated totals — `TrackingDashboardDateRangeTests` is the guard, and it asserts on the
numbers. The two timelines take the span members for uniformity and reject anything but a single day.
The two details endpoints take an instant envelope plus the optional
`windowStartMinutes`/`windowEndMinutes` pair that carries the same daily window across.

**There is one request class per dashboard, not one per source.** `PieChartRequest`,
`SummaryCardsRequest`, `StackedBarsRequest`, `BaseTimelineRequest` and `FocusMetricsRequest` are each
bound by all three sources; the unified routes add `sources` on top. Android used to carry its own copy
of the stacked-bars and timeline requests, differing only in `MinSeconds` being a `long` — a naming
accident rather than a distinction, since nothing about a bucket width is source-specific. **The
response shapes are a different matter and stay separate**: each source carries a different secondary
detail list (`pages` / `windowTitles` / none) with a matching distinct count, and android has no
background concept at all, so a shared response would have to ship a structurally-always-zero
`backgroundSeconds` and a `details` list meaning URLs on one source and window titles on another.

**Every dashboard item also carries a `key`/`label` pair** (`application/dto/response/IDashboardItem.cs`)
beside its own `domain` / `processName` / `packageName` fields — additive, computed, and the same two
names on all three sources, so a client hashes one field for colour and prints one field for display
instead of six across three sources. `label` is never blank; it falls back to `key`.
`TrackingDashboardItemIdentityTests` is the guard, and the fallback is the half that regresses silently.
Not to be confused with the unified dashboards' single `label`, where one identity string across sources
is the whole point — see below.

**The four `focus-metrics` dashboards** (`application/endpoint/BaseFocusMetricsEndpoint.cs` — generic
over the request only because the merged one carries `sources`) add
`baseline` (nullable — no comparison at all is a request the client makes deliberately) and
`focusGapSeconds`, the block-tolerance the client owns and sends. They are the only dashboards
besides the timelines that need *sessions* rather than sums, and the only ones that accept a span
while doing so. Two rules are load-bearing and neither is visible in a 200:

- **Sessions are built one day-window at a time**, so a block cannot span two days and the excluded
  night is not a gap candidate. `FocusMetricsCalculator` documents every definition; they are contract,
  not implementation choices.
- **The baseline scales the same way `summary-cards`' does** — mean day over the lookback × the span's
  `DayCount` for the count, per-day means for the maxima, unscaled for the median. The two sit on one
  screen and must not disagree about what "compared to last 7 days" means.

**Session building lives in `domain/helper/TimelineSegmentBuilder.cs`, in one copy.** The web-extension
and desktop timelines each used to carry their own transcription of it; the focus-metrics dashboards
have to report on *the same* primary sessions the timeline draws, and a drifted second copy would make
the strip disagree with the chart above it while both endpoints' tests passed. Android needs none of
it — its ledger already stores real sessions.

## The unified dashboards — `application/endpoint/unified/query/`

Six endpoints under `/activity-tracking/unified` that answer over **all three ledgers at once**. They
bind the same span base as the fifteen per-source dashboards plus one field, `sources` — a non-empty
subset of `webExtension` / `desktop` / `android`.

**`sources` is a request field, not a client-side filter, and that is the whole reason these endpoints
exist.** An hour in a browser is credited to the extension while the desktop agent is also selected;
deselect the extension and that hour has to come *back* to the desktop agent as `Google Chrome`. A
client filtering a pre-merged payload can only hide a lane — it cannot give the time back.

| File | Role |
|---|---|
| `domain/helper/unified/TrackingSource.cs` | the three sources; **the enum's numeric order is level 2 of the overlap rule**, and the wire names, spelled out rather than left to the enum serializer |
| `domain/helper/unified/UnifiedMinuteMerger.cs` | the overlap rule, in one copy — read this before touching any of the six |
| `domain/helper/unified/UnifiedLabelResolver.cs` | the single identity string, and the cross-source join |
| `domain/helper/unified/SecondsAllocator.cs` | largest-remainder rounding, so the parts keep adding up |
| `application/service/unified/UnifiedActivityLoader.cs` | flattens the three ledgers onto one minute grid |
| `application/service/unified/UnifiedLedger.cs` | the one rounded ledger every merged figure is read off |
| `application/service/unified/UnifiedSpan.cs` | one request's load + merge + ledger, plus the exclusive-minute partition |

**Three things about it are contract rather than implementation, and none of them shows in a 200:**

- **The rule has two levels and the order matters.** Foreground beats background whatever the rank;
  only within one activity class does `webExtension > desktop > android` decide. Level 1 exists because
  level 2 alone credits a desktop browser window on a second monitor over the phone actually in the
  user's hands.
- **Losing is partial.** A source keeps whatever share of a minute the winners did not claim, which is
  what makes browser time the extension could not see survive as `Google Chrome`. Suppressing the
  browser process wholesale while the extension is selected is the tempting shortcut and it is wrong.
- **Everything is read off one rounded ledger.** The source chips, the pie's totals and the cards are
  all sums of the same rows, so `sum of countedSeconds == totals.totalSeconds` and
  `countedSeconds + displacedSeconds ==` that source's own dashboard hold by construction. Both are
  printed on one screen for the user to check.

`focus-metrics` is a **fifth** route beside the three per-source ones, not a replacement, and shares
`BaseFocusMetricsEndpoint<TRequest>` with them — which is generic only because the merged request
carries `sources`. Its sessions are keyed on the unified label, and that single decision settles both
questions the merge raises: a change of label is a switch whatever source either side came from, and
the same label from two sources is a device change rather than a switch.

**Auth:** the three ingest endpoints carry `[AllowExtensionClients]` **and**
`Policies(PortalAuthorizationPolicies.ActivityTracking)`. Everything else is an ordinary web endpoint
and is therefore denied to extension clients by the `Program.cs` configurator's default.

## Seams (all declared in `AdhdTimeOrganizer.Core`)

| File | Role |
|---|---|
| `Core/application/seam/IActivityTimeAttributionSink.cs` | "record N seconds against this activity" — implemented by History's `ActivityHistoryTimeAttributionSink` |
| `Core/application/event/ActivityTimeRecordedEvent.cs` | day totals per activity — handled by the host's `ActivityTimeRecordedEventHandler` |
| `Core/application/seam/IActivityReferenceSource.cs` | this slice's `application/seam/TrackerMappingActivityReferenceSource` — backs `usageCount`/`canDelete` on the activity grid, and repoints both mapping tables on `POST /activity/merge` |

## Infrastructure

| Concern | File |
|---|---|
| Retention purge (GDPR Art. 5(1)(e)) | `infrastructure/jobs/PurgeExpiredActivityTrackingEntriesJobHandler.cs` — a keyed `IScheduledJobHandler` (**not** a Quartz `IJob`; this slice references no Quartz). `ExecuteDeleteAsync` with `IgnoreQueryFilters`, so it purges every user's rows |
| Its 03:30-daily schedule | `infrastructure/scheduling/TrackingScheduledJobsRegistrar.cs` — pushed to the Scheduler module on every boot through `IScheduler` (the RAM job store drops triggers on restart) |
| Retention policy | `infrastructure/persistence/retention/ActivityTrackingRetentionOptions.cs` — bound in `Program.cs` |
| Dev fixture | `infrastructure/persistence/seeder/dev/WebExtensionDataSeeder.cs` — truncates, so it is a dev seeder, not a default one |

## Host-side pieces that belong to this slice's behaviour

Open these when changing tracking, even though they are not in this project:

| What | Where |
|---|---|
| FastEndpoints assembly list | `AdhdTimeOrganizer/Program.cs` (`o.Assemblies`) |
| DI marker scan list | `AdhdTimeOrganizer/config/dependencyInjection/ModuleServiceExtensions.cs` (`ModuleAssemblies`) |
| Configuration scan | `AdhdTimeOrganizer/infrastructure/persistence/AppDbContext.cs` (`ApplyHostConfigurations`) |
| `WebExtensionActivityEntry` scoping exclusion + combined filter | `AppDbContext.UserScopingExcludedTypes` and `OnModelCreating` |
| Partition DDL generator | `Program.cs` **and** `config/AppCommandDbContextFactory.cs` |
| `ActivityTracking` policy definition | `config/IdentityServiceExtensions.cs` |
| `ActivityTracking` role grant | `infrastructure/extService/user/auth/ExtensionRoleClaimsProvider.cs` |
| Completion automation | `application/eventHandler/ActivityTimeRecordedEventHandler.cs` |
| Hosted registrar that schedules the purge | `Program.cs` (`AddHostedService<TrackingScheduledJobsRegistrar>()`) — dropping it builds fine and the purge silently never fires |

⚠ The policy name, the role name, and the `AutoTagOverride("ActivityTracking")` Swagger tag in
`application/endpointGroups/` are **three different constants that happen to share one string**.
Renaming one renames none of the others.

## Tests

| File | Covers |
|---|---|
| `AdhdTimeOrganizer.IntegrationTests/Endpoints/TrackingRouteSmokeTests.cs` | routing, the combined query filter (behaviourally), the partition annotations, purge-job registration |
| `.../Endpoints/TrackingDashboardDateRangeTests.cs` | the day-span / time-of-day-window semantic across the dashboards, asserted on the numbers |
| `.../Endpoints/TrackingFocusMetricsTests.cs` | the four fragmentation measures — switch counting, block tolerance, interior-only gaps, median-not-mean, and the day-bounded range rule |
| `.../Endpoints/TrackingDashboardItemIdentityTests.cs` | the additive `key`/`label` pair on the per-source dashboards, and its never-blank fallback |
| `.../Endpoints/TrackingUnifiedDashboardTests.cs` | the unified overlap rule, asserted on seconds — both levels of precedence, partial displacement, a deselected source giving the time back, the cross-source label join, the device-change-is-not-a-switch rule, and the non-overlapping timeline lanes |
| `.../Endpoints/ExtensionActivityTrackingTests.cs` | extension-client auth on ingest, and the end-to-end attribution + completion path |
| `.../Endpoints/ActivityTimeAutomationTests.cs` | the completion branch matrix, including the exclusivity rule |
| `.../Endpoints/TrackerPatternMappingActivityFkTests.cs` | both mapping FKs are N:1 (two patterns, one activity) and `Cascade` (deleting the activity destroys the rule) |
| `.../Modules/ActivityForeignKeyInventoryTests.cs` | the model-level companion — freezes every activity FK in the solution and its delete behaviour |
