# AdhdTimeOrganizer.Tracking — Domain Map

Navigation index. Read `summary.md` first; open only the rows you need.

## Entities

| Entity | Table | Notes | Path |
|---|---|---|---|
| `DesktopActivityEntry` | `desktop_activity_entry` | Per-heartbeat desktop window. **RANGE-partitioned** on `RecordDate`. `ExecutablePath` is an `EncryptedColumn`. | `domain/model/entity/activityTracking/desktop/` |
| `WebExtensionActivityEntry` | `web_extension_activity_entry` | Per-minute browser window. **RANGE-partitioned** on `RecordDate`; `RecordDate` derives from `WindowStart` in the initializer so the two cannot diverge. `WindowStart` must be minute-aligned — the timeline endpoint stitches windows by `WindowStart == previous.EndedAt`. | `domain/model/entity/activityTracking/` |
| `AndroidSessionData` | `android_session_data` | Per-session android usage, deduplicated on sync by `(DeviceId, PackageName, StartedAt)`. | `domain/model/entity/activityTracking/` |
| `TrackerDesktopMappingByPattern` | `tracker_desktop_mapping_by_pattern` | Pattern → `Activity`/`Role`/`Category`, or `IsIgnored`. Matched in-memory by `MatchesPattern`. | `domain/model/entity/activityTracking/desktop/` |
| `TrackerAndroidMappingByPattern` | `tracker_android_mapping_by_pattern` | Android equivalent. | `domain/model/entity/activityTracking/android/` |

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
| Desktop dashboards | `/activity-tracking/desktop` | pie-chart, stacked-bars, summary-cards, timeline, process-details, distinct-processes, `gird` |
| Desktop mappings | `/activity-tracking/desktop/settings` | create / update / delete / `gird`, plus `fetch-categories-and-roles-by-pattern` |
| Web-extension ingest | `/activity-tracking/web-extension` | `WebExtensionDataHeartbeatEndpoint` (`/heartbeat`) |
| Web-extension dashboards | `/activity-tracking/web-extension` | pie-chart, stacked-bars, summary-cards, timeline, domain-details |
| Android ingest | `/activity-tracking/android` | `AndroidSyncEndpoint` (`/sync`) |
| Android dashboards | `/activity-tracking/android` | pie-chart, stacked-bars, summary-cards, timeline, `gird` |
| Android mappings | `/activity-tracking/android/settings` | create / update / delete / `gird` |

**Auth:** the three ingest endpoints carry `[AllowExtensionClients]` **and**
`Policies(PortalAuthorizationPolicies.ActivityTracking)`. Everything else is an ordinary web endpoint
and is therefore denied to extension clients by the `Program.cs` configurator's default.

## Seams (both declared in `AdhdTimeOrganizer.Core`)

| File | Role |
|---|---|
| `Core/application/seam/IActivityTimeAttributionSink.cs` | "record N seconds against this activity" — implemented by History's `ActivityHistoryTimeAttributionSink` |
| `Core/application/event/ActivityTimeRecordedEvent.cs` | day totals per activity — handled by the host's `ActivityTimeRecordedEventHandler` |

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
| `.../Endpoints/ExtensionActivityTrackingTests.cs` | extension-client auth on ingest, and the end-to-end attribution + completion path |
| `.../Endpoints/ActivityTimeAutomationTests.cs` | the completion branch matrix, including the exclusivity rule |
