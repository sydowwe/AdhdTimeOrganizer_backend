# AdhdTimeOrganizer.Tracking — Agent Summary

**Purpose:** The activity-tracking slice. Owns the three raw ingest ledgers (desktop, browser
extension, android), the two pattern-mapping lookups that resolve a tracked window to an `Activity`,
the twenty-one tracking dashboards — fifteen per-source plus the six **unified** ones that merge the
three ledgers into one picture of a day — and the retention purge over the ledgers.

**Fifth and last project of the portal split**, after `Core`, `TodoLists`, `Routines`, `History` and
`Planning`. Plan: `review/portal/slicePrompts/00-README.md`.

## Bounded context

Owns:

- **Entities** — `DesktopActivityEntry`, `WebExtensionActivityEntry`, `AndroidSessionData`,
  `TrackerDesktopMappingByPattern`, `TrackerAndroidMappingByPattern`, and their five EF
  configurations. ⚠ Three of those configurations used to live in the host's
  `infrastructure/persistence/configuration/activityHistory/` folder. **The folder name lied** — they
  were always Tracking's, and they moved with their entities. Do not assign files to projects by
  directory here.
- **38 endpoints** under `application/endpoint/activityTracking/` — desktop ingest + dashboards,
  web-extension ingest + dashboards, android sync + dashboards, the six unified dashboards, and the two
  pattern-mapping settings grids with their CRUD.
- **The five endpoint groups** (`application/endpointGroups/`). The whole folder moved: every group in
  it was a tracking group.
- **Validators** (one per dashboard, not one per source — `StackedBarsValidator` and
  `BaseTimelineValidator` each cover all three), the tracking request/response DTOs,
  `WebExtensionDataFilterRequest`.
- **Retention** — `PurgeExpiredActivityTrackingEntriesJobHandler` (a keyed `IScheduledJobHandler` on the
  Scheduler module's substrate, so this slice references no Quartz), its `TrackingScheduledJobsRegistrar`
  and `ActivityTrackingRetentionOptions`.
- **The dev `WebExtensionDataSeeder`** (`Order` inside the 600–699 band — see
  `AdhdTimeOrganizer.Core/infrastructure/persistence/seeder/SeederOrderBands.md`).

Does **not** own:

- **`PortalAuthorizationPolicies`** — the `ActivityTracking` policy *name* moved to
  `AdhdTimeOrganizer.Core/infrastructure/security/` so both this slice and the host can say it; what
  the policy actually *requires* is still declared host-side in `IdentityServiceExtensions`, and
  `ExtensionRoleClaimsProvider` (which grants the role to extension tokens) stays host-side too. Which
  clients may report activity is a product decision the host owns.
- **`WebExtensionActivityEntry`'s query filter.** The entity is here; its exclusion from
  `ApplyUserQueryFilters` and its hand-written combined filter are in `AppDbContext`. See below.
- **Partition DDL generation.** `PartitionedNpgsqlMigrationsSqlGenerator` is wired host-side in *both*
  `Program.cs` and `config/AppCommandDbContextFactory.cs`.
- **The completion automation.** It is host-side, and deliberately so — see below.

## Dependency seams

- **References:** `AdhdTimeOrganizer.Core`, `Sydowwe.Framework`, `Sydowwe.Framework.Contracts`.
  **Nothing else** — not History, not Planning, not TodoLists, not Routines, not the host.
- **Referenced by:** `AdhdTimeOrganizer` (the host) and the integration test project.

### ⚠ Two seams — the whole reason this slice could be extracted at all

The pre-split analysis recorded Tracking as the one slice that could **not** have its
dependencies inverted, because they were **writes**, not reads. `DesktopActivityHeartbeatEndpoint`
used to, in one request: write `ActivityHistory` rows (History), transition `PlannerTask.Status`
(Planning), and tick off to-do and routine items with a streak bump (TodoLists, Routines). Four
slices, three of them written to.

Both directions are now Core seams, built and committed **before** any file moved:

| Seam | Direction | Implemented by | Failure mode if broken |
|---|---|---|---|
| `IActivityTimeAttributionSink` | Tracking → *the ledger owner* | `ActivityHistoryTimeAttributionSink` (History) | **Loud** — single service, so a missing registration throws at endpoint activation |
| `ActivityTimeRecordedEvent` | Tracking → *whoever automates completion* | `ActivityTimeRecordedEventHandler` (host) | **Silent** — an undiscovered handler leaves a 200 on the wire and nothing completed |

The sink is resolved as a **single** service rather than a keyed `IEnumerable` (unlike
`IActivityMembershipSource`) precisely so that failure is loud: dropping every attribution write
silently would be far worse than a 500.

Implementations of the sink **mutate the context handed in and must not call `SaveChanges`** — the
heartbeat's own save is the transaction, which is what keeps a tracked entry and its attribution
atomic.

### ⚠ Why the completion automation is one host-side handler

`ActivityTimeRecordedEventHandler` lives in the host's `application/eventHandler/`, not split into a
Planning handler and a TodoLists/Routines handler. The rule it implements is **exclusive**: the to-do
/ routine fallback runs *only* when no planner task matched. FastEndpoints' `Mode.WaitForAll` gives
independent subscribers no ordering and no veto, so a split would silently double-complete any
activity that has both a planner task and a to-do item. The host already references every slice and
already hosts the three completion fan-out handlers.

The handler logs and swallows its own exceptions — the ingest committed before it ran, and a 500
would only make the agent re-send the window. That makes a break here silent;
`ActivityTimeAutomationTests` is the guard, and it asserts on rows, not routes.

## Things that fail silently

1. **Missing from `o.Assemblies`** in `Program.cs` (`DisableAutoDiscovery = true`) → every tracking
   route 404s. No build error.
2. **In `ModuleAssemblies` *and* the `AddDependencyInjection` AppDomain sweep** → every service
   registered twice; `WebExtensionDataSeeder` truncates and reseeds twice per run. The sweep `Except`s
   `ModuleAssemblies`, so keep it in exactly one list.
3. **`[AllowExtensionClients]` lost from an ingest endpoint** → the `DenyExtensionClients` policy the
   `Program.cs` configurator attaches by default kicks in and the browser extension / desktop agent
   get 403 instead of a build error.
4. **`WebExtensionActivityEntry` dropped from `AppDbContext.UserScopingExcludedTypes`, or its
   hand-written filter changed** → it is the one entity the general `IEntityWithUser` rule does not
   cover. Losing the user half leaks every user's browsing history to any signed-in caller; losing the
   date half unbounds the partition read.
5. **Tracking's `ApplyConfigurationsFromAssembly` call missing from `AppDbContext`** → the two
   partitioned tables quietly become plain tables in the next migration.

6. **The dashboard date range read as one continuous block** rather than as a time-of-day window
   repeated on each day of the span → every endpoint still answers 200 with a well-formed body, with
   totals inflated by every night the user's window excludes. On a narrowed working-day window that is
   most of the number on the page. `TrackingDashboardDateRangeTests` is the guard and asserts on the
   numbers; the rule itself lives on `domain/helper/DailyWindowSet.cs`.

7. **A fragmentation measure computed across the night between two days' windows** — a block stitched
   through hours the user excluded, or that overnight interval reported as the longest break. A flat
   pool of the span's sessions produces both, silently and with a well-formed 200. Every measure but
   the median is therefore computed inside one day's window and rolled up; `TrackingFocusMetricsTests`
   is the guard and the rule lives on `domain/helper/FocusMetricsCalculator.cs`.

8. **The unified dashboards' overlap rule computing the wrong number** — the merged day comes back
   shorter or longer than the day the user lived, in a perfectly well-formed 200, and the three figures
   that let a user check it (`countedSeconds`, `displacedSeconds`, `displacedTo`) agree with each other
   while being wrong together. The failure has three separate shapes and all three are silent: the two
   levels of the rule applied in the wrong order deletes phone time in favour of a browser window left
   open on a second monitor; a losing source suppressed wholesale rather than partially leaves an hour
   spent in a browser showing no browser at all; and rounding each item on its own quietly breaks the
   two arithmetic identities the page prints side by side. `TrackingUnifiedDashboardTests` is the guard
   and asserts on seconds; the rule itself lives on `domain/helper/unified/UnifiedMinuteMerger.cs`.

`TrackingRouteSmokeTests` covers 1, 4 and 5; `CoreRouteSmokeTests`' seeder-duplication test already
covers 2 over every registered seeder; `ExtensionActivityTrackingTests` covers 3 and the end-to-end
seam.

⚠ **Never anchor an assembly scan on a type that can move slices.** The Planning extraction moved the
type `ApplyHostConfigurations` was anchored on, silently turning the host's own scan into a second
Planning scan; the next `migrations add` emitted ~500 lines of table renames. Tracking's entries are
anchored on `DesktopActivityEntry`, and the host's on `typeof(AppDbContext)`.

## Navigation

`docs/domain-map.md` is the index — open it before opening source files.
