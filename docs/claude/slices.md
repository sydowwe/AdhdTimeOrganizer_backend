# Solution Layout & Slices

Detail for the project list in `CLAUDE.md`. Each slice also documents itself in its own
`docs/summary.md` — read that before working in it.

## AdhdTimeOrganizer (the portal / host)

The remaining feature areas, endpoints, `AppDbContext`, migrations, DI wiring, `Program.cs`.
Deliberately host-side: Google Calendar + Google sign-in, the suggestion-pattern view SQL /
installer / interceptor / refresh job, `CalendarActivityEndpoint`, `ActivityTimeRecordedEventHandler`,
`PlannerTaskSeeder`, `ExtensionRoleClaimsProvider`, and the cross-slice relationship declarations.

`AdhdTimeOrganizer/reference/mojaCore/` is reference/foreign code — don't extend it.

## AdhdTimeOrganizer.Core

The first vertical slice. `User` and `Activity` plus the roles and categories hanging directly off
them, timer presets, the base shims and the `IsManyWithOne*` configuration helpers, the three
genuinely shared enums (below), the extendable/generic DTO bases, the cross-slice event records, and
the remaining 26 activity + 10 timer endpoints. **References only `Sydowwe.Framework` and
`Sydowwe.Framework.Contracts` — never the host**, which is why everything in it takes a plain
`DbContext` rather than `AppDbContext`.

Read `AdhdTimeOrganizer.Core/docs/summary.md`, and `.../seeder/SeederOrderBands.md` before adding a
seeder **anywhere** in the solution.

⚠ The four activity lookups, the three activity profiles and memory anchors are **not** here — they
are `AdhdTimeOrganizer.ActivityProfiles`, which took 52 of Core's 89 endpoint files.

⚠ **Core's enum folders hold exactly three enums, and an enum earns a place there only by being
Core's own or by having two consumers that cannot see each other.** `domain/model/enum/` has
`DayType` (part of `ICalendarDayLookup`'s record — History and Planning both read it) and `Location`
(ActivityProfiles, Planning and the host); `application/dto/enum/` has `ActivityDateRangeType`, used
by Core's own `DateRangeDto`. Everything else went to its owning slice: `PlannerTaskStatus` /
`RecurrenceType` / `ReminderRecurrence` / `SuggestionSourceType` → Planning,
`TrackerDesktopMappingTypeEnum` / `BaselineType` → Tracking, `HistoryGroupBy` → History,
`StreakOutcome` → Routines. Each kept its layer — domain enums under `domain/model/enum/`, DTO enums
under `application/dto/enum/`. A single-consumer enum parked in Core is drift, not sharing: put it in
the slice that owns it, and reach for a seam (not a Core enum) if a second slice later needs it.

Moving one is schema-neutral and needs **no migration** — `EnumColumn()` persists these as strings
and the snapshot records them as `b.Property<string>("…")`, so no CLR type name reaches the database.
Two rules when rewriting the `using`s: match the enum name only in **type position** (`t.RecurrenceType`
is a member access, and `BasePlannerTask.Location` is a `string` property that merely shares
`Location`'s name — treating either as a type usage pins a Core using nothing needs), and write
**LF**. `.gitattributes` pins `* text=auto eol=lf`; a bulk rewrite that joins lines with CRLF turns a
one-line diff into a whole-file one.

## AdhdTimeOrganizer.TodoLists

Lists, items, steps, categories and the per-user `TaskPriority` lookup, plus the shared to-do
primitives the Routines slice builds on (`BaseTodoListItem`, `TodoListStep`, `BaseTodoListConfigure`,
`TodoListExtensions`, `TodoListSettings`, and the toggle/step/reorder endpoint bases — those stay
**here**, not in Core). Zero outbound slice edges.

⚠ The **pinned FK constraint name** in `PlannerTaskConfiguration` exists because EF derives that name
from the order the `ApplyConfigurationsFromAssembly` calls run in, and every new slice shifts that
order.

⚠ **`TodoListItem.CompletedTimestamp` is written by an interceptor, never at a call site.**
`TodoListItemCompletionInterceptor` stamps it off the ChangeTracker on every genuine `IsDone`
transition; five places write `IsDone` and a missed one would not fail to build, the item would just
never appear in a daily recap. It is registered host-side in `Program.cs`'s `AddInterceptors` call,
and `ExecuteUpdateAsync` bypasses it — keep `IsDone` writes on tracked entities.

## AdhdTimeOrganizer.History

`ActivityHistory`, its 13 endpoints (CRUD, the `gird` grid, and the six `HistoryDetail*` /
`HistorySummary*` dashboards), DTOs, the `HistoryGroupBy` enum, validator and dev seeder.
**References Core and the framework only — not TodoLists, not Routines**: the grid's to-do / routine
membership filters were rewritten onto the `IActivityMembershipSource` seam.

⚠ **`ActivityHistory` carries two nullable item links**, `TodoListItemId` / `RoutineTodoListId` —
which task a recording was saved from, stamped when the user accepts the save-to-history prompt on
completing one (a step's prompt sends its *parent item's* id). Navigation-free and declared host-side
in `ConfigureCrossSliceRelationships` with pinned names and `SetNull`: `ActivityHistory` is the source
of truth for recorded time, so deleting a task must not delete it. They are a link, not a copy.
`TodoListItemLoggedTimeSource` serves TodoLists' daily recap off the first one and **must key on it
alone** — widening to "any time logged against this item's activity" looks strictly more generous and
is wrong, because two to-do items may share one activity and would each be credited the same seconds.
`ActivityHistoryRequest.UpdateEntity` deliberately does not write either column: the edit form does
not carry them, so assigning would unlink the row on every edit.

## AdhdTimeOrganizer.Planning

**Includes reminders** (there is no `AdhdTimeOrganizer.Reminders`). Calendar, the four planner-task
types, day templates, `TaskImportance`, `UserPlannerSettings`, `Reminder` +
`ReminderRegistrationService`, the three suggestion-pattern read-models, ~49 endpoints, 12 validators,
five seeders, and five enums (`PlannerTaskStatus`, `RecurrenceType`, `ReminderRecurrence`,
`SuggestionSourceType` in `domain/model/enum/`, `ApplyTemplateConflictResolutionEnum` in
`application/dto/enum/`). Zero outbound slice edges, because both predicted dependencies turned out
to be avoidable:

- `Planning → History` never existed. The suggestions endpoint reads
  `PlannerSuggestionFromActivityHistory` (the entity over `mv_activity_history_pattern`), not the
  `ActivityHistory` entity. The three suggestion read-models are named `PlannerSuggestionFrom<Source>`
  — consumer first, source second — and each pins its view with an explicit `ToView(...)`, so the
  class names are free to change without touching the schema. The materialized view is the decoupling.
- `Planning → TodoLists` was **deleted**. `PlannerTask.TodolistItemId` keeps its FK and its `SetNull`;
  the unused `PlannerTask.TodolistItem` navigation was removed and the relationship moved to
  `AppDbContext.ConfigureCrossSliceRelationships`, where both types are visible. Empty migration.
  Do not add the navigation back.

⚠ Two things here fail **silently**. (1) Delete the host-side relationship declaration and the FK
vanishes from the model with no build error —
`PlanningRouteSmokeTests.PlannerTaskToTodoListItem_ForeignKey_SurvivesTheSliceSplit` is the guard.
(2) `ApplyHostConfigurations`'s host-side scan is anchored on `typeof(AppDbContext).Assembly` **on
purpose**: it used to be anchored on `PlannerTaskConfiguration`, which this extraction moved out,
silently turning the host's own scan into a second Planning scan and dropping six host configurations
from the model. Nothing failed to build; the next `migrations add` emitted ~500 lines of table renames
and dropped partition keys. **Never anchor an assembly scan — `ApplyConfigurationsFromAssembly`,
`ModuleAssemblies`, FastEndpoints `o.Assemblies` — on a type that can move slices.**

## AdhdTimeOrganizer.Tracking

The three raw ingest ledgers (`DesktopActivityEntry`, `WebExtensionActivityEntry`,
`AndroidSessionData`), the two `Tracker*MappingByPattern` lookups, 29 endpoints
(desktop/web-extension/android ingest + twelve dashboards + the mapping grids), the five endpoint
groups, 17 validators, the retention purge job, the dev `WebExtensionDataSeeder` and two enums
(`TrackerDesktopMappingTypeEnum` in `domain/model/enum/`, `BaselineType` in `application/dto/enum/` —
note the first is named for desktop but the android mapping grid uses it too). Zero outbound slice
edges, which the plan said was impossible: its dependencies were **writes**, so no read-side pattern
could invert them. Two Core seams did.

⚠ **`ActivityTimeRecordedEventHandler` is host-side and single on purpose — do not split it per
owning slice.** Its rule is exclusive (the to-do/routine fallback runs *only* when no planner task
matched), and `Mode.WaitForAll` gives independent subscribers no ordering and no veto, so a split
silently double-completes anything holding both. It also logs-and-swallows, because the ingest already
committed — so a break is silent. `ActivityTimeAutomationTests` asserts on rows, not routes.

⚠ **`WebExtensionActivityEntry` is the one entity the global user filter does not cover.** It is
listed in `AppDbContext.UserScopingExcludedTypes` and given a hand-written filter combining the user
check with `RecordDate >= CurrentPartitionDate`; the entity is in the slice, both halves are
host-side. Losing the user half leaks every user's browsing history to any signed-in caller.
`TrackingRouteSmokeTests.WebExtensionActivityEntry_KeepsItsCombinedQueryFilter` seeds two users and an
out-of-range row rather than inspecting metadata.

## AdhdTimeOrganizer.ActivityProfiles

The only slice carved out of Core rather than the host. The three `Activity*Profile` entities, the
four per-user activity lookups they FK into (`ActivityLocationType`, `ActivityWeatherDependency`,
`ActivityExpectedCostTier`, `ActivityExperienceType`), `MemoryAnchor`, 52 endpoints, 8 validators, 4
profile-only enums, and 12 seeders. Zero outbound slice edges, and it needed no seam to get there:
nothing outside Core had ever referenced these eight entities. Named for what it holds rather than
"Leisure" — `ActivityProjectProfile` is DIY (difficulty, materials, tools, readiness), not leisure.

⚠ **The five inbound edges were deleted, not inverted** — `Activity.BacklogProfile` / `.ProjectProfile`
/ `.BucketListProfile` / `.MemoryAnchors` and `User.MemoryAnchors`. Each only fed a configuration
helper a navigation expression. Adding one back requires a project reference **from Core**, which
inverts the direction every slice depends on, and it compiles fine —
`ActivityProfilesRouteSmokeTests.Core_DoesNotReferenceActivityProfiles` is the guard.

⚠ **Four FK constraint names are pinned with `HasConstraintName(...)`** in this slice's
configurations. EF derives an FK's name from the principal-end navigation, so deleting those four
navigations silently renames the constraint, and each rename is a DROP + ADD CONSTRAINT pair (ACCESS
EXCLUSIVE lock + full revalidation) for zero schema benefit. The pins are what make the
`ActivityProfilesSlice` migration empty. Same reasoning as `PlannerTaskConfiguration`'s pinned name.

⚠ **Its seeders share Core's 010–099 `Order` band on purpose** — the dev chain interleaves (lookups
10–13 → Core's `Activity` 40 → profiles 50–52 → anchors 60). It is the only shared band; see
`SeederOrderBands.md`.

## Cross-slice seams

**Every cross-slice seam is listed in `AdhdTimeOrganizer.Core/application/seam/README.md`** — read it
before adding one. Four interface seams marked `ISeam` and four events marked `ISeamEvent`; both
markers exist so the whole surface shows up in one type hierarchy, and `SeamWiringTests` pins the
registry (placement, key coverage, no unhandled events).

**Four ways to avoid a slice→slice reference, all in use.** Try each before accepting a project
reference or a sequencing constraint:

1. A **materialized view** (Planning ← History).
2. A **navigation-free FK declared in the host** (Planning → TodoLists; History → TodoLists /
   Routines for the recording↔task links).
3. The **`IActivityMembershipSource` seam** (History ← TodoLists / Routines; the same shape carries
   `ITodoListItemLoggedTimeSource` the other way, TodoLists ← History).
4. For a **write**, which none of the others can invert — an **event whose decision moves to a
   subscriber** (Tracking → Planning / TodoLists / Routines, via `ActivityTimeRecordedEvent`).

All four fail silently when broken, so each needs a behavioural test rather than a route smoke test.

**`IActivityMembershipSource`** (`AdhdTimeOrganizer.Core/application/seam/`) is the pattern to reuse
whenever one slice needs to filter on another's rows: Core declares the interface and the key
constants, the **owning** slice implements it (`TodoListActivityMembershipSource` in TodoLists,
`RoutineTodoListActivityMembershipSource` in Routines), the **consuming** slice resolves
`IEnumerable<IActivityMembershipSource>` and matches on `Key`. Neither side references the other. Two
rules: the returned `IQueryable<long>` must stay **composable** (callers use it as a subquery, so
`ToList()` turns one `EXISTS` into a client-side filter), and because resolution is by *string key* a
missing or misregistered implementation is **silent** — no build error, no exception, the filter just
stops narrowing. Every consumer needs a test asserting the rows, not merely the route
(`HistoryRouteSmokeTests.Grid_MembershipFilter_NarrowsThroughTheSeam`).

**`IActivityTimeAttributionSink`** (same folder) — the heartbeat's `ActivityHistory` write. History
implements it; Tracking calls it. Resolved as a **single** service, not a keyed `IEnumerable`, so a
missing registration throws at activation instead of silently dropping every attribution.
Implementations **mutate the caller's `DbContext` and must not `SaveChanges`** — the heartbeat's own
save is the transaction.

**`ActivityTimeRecordedEvent`** (`.../application/event/`) — per-activity whole-day totals. The host's
`ActivityTimeRecordedEventHandler` owns the planner-task / to-do / routine completion rule that used
to run inline in the ingest endpoint.

## The framework submodule

`framework/` is a git submodule (github.com/sydowwe/Sydowwe.Framework) holding seven projects:

- `Sydowwe.Framework` — the shared framework, used by the portal and the modules alike. Base
  entities, base endpoints, builder extensions, DbContext helpers, seeders, auth services.
- `Sydowwe.Framework.Contracts` — the cross-module contract layer: the interfaces, enums and records
  through which the modules talk to each other and to a host without referencing it (`IScheduler`,
  `IScheduledJobHandler`, `INotificationService`, `IQuietHoursReader`, `IReminderRegistry`,
  `ISubjectDataEraser`, the payload types). **Contract types only** — no services, no EF, no package
  references.
- `Sydowwe.Framework.Testing` — the shared test infrastructure.
- `Sydowwe.Notifications` / `Sydowwe.Reminders` / `Sydowwe.Scheduler` — opt-in module projects built
  on the `Sydowwe.Framework` primitives, wired to each other only through
  `Sydowwe.Framework.Contracts`. They reference **only** those two — no portal coupling at all. Note
  they are `Sydowwe.<Module>`, *not* `Sydowwe.Framework.<Module>`: optional modules, not framework core.
- `Sydowwe.Scheduler.Xlsx` — opt-in XLSX export for the scheduler dashboard, and **the only project in
  the submodule carrying a licensed dependency** (Syncfusion.XlsIO.Net.Core). Split out precisely so
  `Sydowwe.Scheduler` itself needs no Syncfusion licence: the core writes CSV with no dependency and
  delegates XLSX through `IXlsxTableRenderer`. A host opts in by referencing this project and calling
  `services.AddSchedulerXlsxExport()` — the portal does both. Registered **by name**, not via a
  lifetime marker, and deliberately **not** in `ModuleAssemblies`. With no renderer registered, an
  XLSX request throws `NotSupportedException` rather than silently returning CSV bytes under an
  `.xlsx` content type.

`AdhdTimeOrganizer.IntegrationTests` stays in the parent: it pins *this host's* composition, which is
a property of the parent, not of the modules.

⚠ **Editing the submodule is a two-repo operation.** A change there is committed and pushed in the
`Sydowwe.Framework` repo *first*; the parent then records the new commit sha as a gitlink, which is
its own commit here. `git status` in the parent shows only "modified: framework (new commits)" —
never the individual files — so a framework edit left uncommitted in the submodule is invisible to the
parent's diff and will not travel with a parent push.

Clone with `git clone --recurse-submodules`. An existing checkout that predates the split needs
`git submodule update --init` or the solution will not restore.

⚠ **Module entities live in the submodule; their migrations live here.** `AppDbContext` owns every
module table, so a change to a module entity is a submodule commit *and* a migration commit in the
parent — in that order. Inherent to the design (modules are host-agnostic; the host owns its schema).
A CLR namespace rename does **not** change table or column names (those come from
`BaseEntityConfigure`, derived from the *class* name) — but the next `dotnet ef migrations add` will
regenerate `AppDbContextModelSnapshot.cs` with the new `Sydowwe.*` type names, producing a large but
semantically empty diff. Expect it; don't hand-edit the snapshots.

**Which copy to use: there is one copy.** The portal's parallel set of primitives was deleted in the
framework reconciliation — reach for `Sydowwe.Framework.*` from portal code too. What still lives in
the portal is only what names a portal-specific type: the two `BaseEntityWithUser` /
`BaseLookupWithUser` closing shims, `IsManyWithOneUser` / `IsOneWithOneUser`, and a handful of
entity-specific config helpers. The reconciliation is finished; there is no outstanding duplicate to
merge.

## Historical naming

`Sydowwe.Framework.Contracts` used to be a portal-level project called
**`MojaDigitalnaFirma.Kernel`**, and older docs and comments still call it "the Kernel". Same 42
contract types, same folder names; only the root namespace changed. `Sydowwe.Framework` / `.Testing`
kept their namespaces when they moved — only the on-disk `framework/` prefix is new.
