# Extract `AdhdTimeOrganizer.Tracking`

**Last slice, and the only one with a prerequisite refactor.** All other slices — `Core`,
`TodoLists`, `Routines`, `History`, `Planning`, `Reminders` — must already exist and be
committed.

This is **two commits, not one.** Phase 1 builds a seam; phase 2 moves the files. Do not start
phase 2 until phase 1 is committed and green.

---

## Ground rules

- Windows box. Use **PowerShell**. **Never round-trip a file through the shell** (no
  `Get-Content | Set-Content`, no `sed -i`) — codepage 1252 double-encodes UTF-8. Use editor
  tools to change file contents.
- `framework/` is a **git submodule**. Do not touch it. Parent repo only.
- `git mv` so history survives. Change only the root namespace prefix. **Do not rename types.**
- **Never reference the host from a slice.**
- Slice services take a plain **`DbContext`**, never `AppDbContext`. The alias is registered.
- Confirm the baseline first: `dotnet test AdhdTimeOrganizer.IntegrationTests` =
  **198 passed, 6 skipped, 0 failed**. Match it after each phase.

---

# Phase 1 — build the automation seam (own commit)

`application/endpoint/activityTracking/desktop/command/DesktopActivityHeartbeatEndpoint.cs`
is not just ingest. Around lines 126–175 it:

1. queries `dbContext.PlannerTasks` for today (line ~128),
2. compares tracked seconds against the task's planned duration,
3. **mutates `PlannerTask.Status`** to `Completed` / `InProgress`, saves, and publishes
   `PlannerTaskIsDoneChangedEvent`,
4. and when no planner task matches, falls back to `AutomateWithoutPlannerTaskAsync`
   (line ~155), which reaches into `TodoListItems` and `RoutineTodoLists`.

So Tracking currently **writes into three other slices**. That is the blocker.

**The fix:** have the heartbeat publish an event — something like
`ActivityTimeRecorded(userId, activityId, secondsToday)` — whose record lives in **Core**, and
move the automation into handlers owned by `Planning` (the `PlannerTask.Status` transition) and
`TodoLists` / `Routines` (the `AutomateWithoutPlannerTaskAsync` fallback). This inverts the
dependency and matches the event pattern the completion fan-out already uses. Side benefit: a
large lump of business logic leaves an ingest endpoint.

Handlers may stay host-side initially if that is simpler — the point is only that **Tracking
stops writing into other slices**.

⚠ **This is a behaviour change, unlike every other prompt in this folder.** The heartbeat path
has no direct test coverage today. Write tests for the extracted automation *before* moving it,
so the seam is pinned. Commit phase 1 on its own with the suite green.

---

# Phase 2 — move the files

## Registering with the host — four places, none break the build

1. `AdhdTimeOrganizer/Program.cs` → FastEndpoints `o.Assemblies` (`DisableAutoDiscovery = true`).
   Missing → endpoints **404 silently**.
2. `config/dependencyInjection/ModuleServiceExtensions.cs` → `ModuleAssemblies`. **Not also in
   the `AddDependencyInjection` sweep** — it `Except`s this list; being in both registers every
   service twice and doubles every `IEnumerable<T>`. Nothing throws.
3. `infrastructure/persistence/AppDbContext.cs` → `ApplyHostConfigurations` (~line 128).
4. `AdhdTimeOrganizer.sln`.

## The migration gate

Table and column names come from the **class** name via `BaseEntityConfigure`, not the
namespace. Run `dotnet ef migrations add TrackingSlice` and confirm `Up`/`Down` are **empty**.
`AppDbContextModelSnapshot.cs` diffs hugely with no schema in it; never hand-edit it.

## What moves

- **Endpoints** — `application/endpoint/activityTracking/**` (~29 files): desktop ingest, web
  extension ingest, android, the pattern mappings, the tracking dashboards.
- **Entities** — `DesktopActivityEntry`, `WebExtensionActivityEntry`, `AndroidSessionData`,
  `TrackerDesktopMappingByPattern`, `TrackerAndroidMappingByPattern`.
- **Configurations** — ⚠ **the folder structure lies.** Three of them are filed under History:
  `configuration/activityHistory/DesktopActivityEntryConfiguration.cs`,
  `configuration/activityHistory/WebExtensionActivityEntryConfiguration.cs`,
  `configuration/activityHistory/AndroidSessionDataConfiguration.cs`. All three are **Tracking**.
  Plus the two `Tracker*MappingByPatternConfiguration` files. Do not assign files to projects by
  directory.
- **Retention** — `infrastructure/jobs/PurgeExpiredActivityTrackingEntriesJob.cs` and
  `infrastructure/persistence/retention/ActivityTrackingRetentionOptions.cs`.
- **Seeders** — the dev `WebExtensionDataSeeder` (`Order` rebased into the 600–699 band during
  the Core commit).
- **DTOs and validators** for the above.

## Slice-specific gotchas

**Two of these tables are partitioned.** `DesktopActivityEntry` and `WebExtensionActivityEntry`
use `IsPartitionedByRange` (from `Sydowwe.Framework`'s `PartitioningExtensions`, note: **not**
under `configuration/extensions/`). The partition SQL is emitted by
`PartitionedNpgsqlMigrationsSqlGenerator`, wired via `optionsBuilder.ReplaceService<...>()` in
**both** `Program.cs` **and** `config/AppCommandDbContextFactory.cs` (the design-time factory).
Both stay host-side. If the empty-migration check produces partition DDL, that wiring is what
broke.

**`WebExtensionActivityEntry` is excluded from the automatic user filter.** It appears in
`AppDbContext.UserScopingExcludedTypes` (~line 92) and receives a **combined** filter in
`OnModelCreating` — the same user check ANDed with `RecordDate >= CurrentPartitionDate`. That
exclusion and the hand-written filter stay host-side and must keep working. This entity is the
one place where losing the filter would not be caught by the general `IEntityWithUser` rule.

**The `ActivityTracking` authorization policy stays host-side, deliberately.**
`infrastructure/security/PortalAuthorizationPolicies.cs` (the policy) and
`infrastructure/extService/user/auth/ExtensionRoleClaimsProvider.cs` (which grants the
`ActivityTracking` **role** to extension tokens via `IAdditionalUserClaimsProvider<TUser>`) are
product decisions about which clients may report activity. Moving them would invert the seam
that keeps Framework ignorant of this deployment's role names.

⚠ Note the policy name and the role name are **two different constants that happen to share the
string** `"ActivityTracking"`. Renaming one does not rename the other. The
`AutoTagOverride("ActivityTracking")` in `endpointGroups/` is a third, unrelated use — a Swagger
tag. Leave it a literal.

**Extension clients are denied by default.** The endpoint configurator in `Program.cs` attaches
the `DenyExtensionClients` policy to every endpoint *without* `[AllowExtensionClients]`. The
tracking ingest endpoints carry that attribute — if it is lost in the move, the browser
extension and desktop agent stop being able to report, with a 403 rather than a build error.
Verify the attribute survived on every ingest endpoint.

**`Tracking → History`.** The heartbeat writes `ActivityHistory`. Reference the History slice.
After phase 1, that should be Tracking's only remaining outbound slice edge besides Core.

## Done when

- `dotnet build AdhdTimeOrganizer.sln` clean
- `dotnet test` = **198 passed, 6 skipped, 0 failed** (plus the phase-1 tests you added)
- `dotnet ef migrations add TrackingSlice` produces an **empty** `Up`/`Down` — in particular
  **no partition DDL**
- a desktop heartbeat and a web-extension ingest call both accepted from an extension-client
  token, and rejected from a web token where they should be
- the heartbeat still drives planner-task completion — through the phase-1 event, not a direct
  write
- `docs/summary.md` + `docs/domain-map.md` written in the new project
- two commits (seam, then move)