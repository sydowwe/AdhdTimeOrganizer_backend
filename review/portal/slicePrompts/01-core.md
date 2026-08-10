# Extract `AdhdTimeOrganizer.Core`

Create the shared core project that every vertical slice will depend on. **This must land
before any slice.** Nothing else in this folder can start until it is committed and green.

---

## Ground rules

- Windows box. Use **PowerShell**. **Never round-trip a file through the shell** (no
  `Get-Content | Set-Content`, no `sed -i`) — the ANSI codepage is 1252 and it double-encodes
  UTF-8. Change file contents with editor tools only.
- `framework/` is a **git submodule**. Do not touch it. This task is parent-repo only.
- Move files with `git mv` so history survives. Change only the root namespace prefix.
  **Do not rename types.**
- **Core must not reference the host.** Core holds entities and primitives; `AppDbContext`,
  `Program.cs`, migrations and DI wiring all stay in `AdhdTimeOrganizer`.
- Confirm the baseline first: `dotnet test AdhdTimeOrganizer.IntegrationTests` must report
  **198 passed, 6 skipped, 0 failed**. Match it at the end.

## Registering the project with the host — four places, none break the build

1. `AdhdTimeOrganizer/Program.cs` → the FastEndpoints `o.Assemblies` list
   (`DisableAutoDiscovery = true` is set). Missing → endpoints **404 silently**.
2. `AdhdTimeOrganizer/config/dependencyInjection/ModuleServiceExtensions.cs` →
   `ModuleAssemblies`. **Do not also leave it in the `AddDependencyInjection` sweep** — that
   sweep `Except`s this list, and being in both registers every service twice, doubling every
   `IEnumerable<T>` (each seeder then runs twice). Nothing throws.
3. `AdhdTimeOrganizer/infrastructure/persistence/AppDbContext.cs` → `ApplyHostConfigurations`
   (~line 128) is a hard-coded list of `ApplyConfigurationsFromAssembly` calls. Add one.
   Missing → the entities are simply absent from the model.
4. `AdhdTimeOrganizer.sln`.

## The migration gate — the real proof

`BaseEntityConfigure` derives table and column names from the **class** name, not the
namespace, so moving types between projects changes **no** table or column names.

Run `dotnet ef migrations add CoreExtraction` and confirm the generated `Up` and `Down` are
**empty**. If they are not, you renamed something — revert and find it.
`AppDbContextModelSnapshot.cs` will show a huge diff (new type names, no schema). Expect it;
never hand-edit it.

---

## What moves into Core

Discover the exact file set yourself; these are the anchors.

**Entities** (`AdhdTimeOrganizer/domain/model/entity/`)
- `user/User.cs` and the user-adjacent types the Identity setup needs
- `activity/Activity.cs`, `ActivityRole`, `ActivityCategory`
- the four activity lookups — `ActivityLocationType`, `ActivityExpectedCostTier`,
  `ActivityWeatherDependency`, `ActivityExperienceType`
- the three `Activity*Profile` entities (Backlog / Project / BucketList)
- `activity/memoryAnchor/MemoryAnchor.cs`
- `timer/` — `TimerPreset`, `PomodoroTimerPreset`. **Timers folds into Core; it does not get
  its own project.** (Decided; the earlier playbook proposing `AdhdTimeOrganizer.Timers` is
  superseded.)

**Base shims and configuration helpers**
- `domain/model/entity/user/BaseEntityWithUser.cs`, `entity/base/core/BaseLookupWithUser.cs`,
  `BaseEntityWithActivity` — the portal's closing types over its concrete `User` / `Activity`
- `infrastructure/persistence/configuration/extensions/EntityWithUserBuilderExtensions.cs`
  (`IsManyWithOneUser` / `IsOneWithOneUser`) and
  `EntityWithActivityBuilderExtensions.cs`
- `domain/model/entityInterface/` — the two portal markers (`IEntityWithIsDone`,
  `IEntityWithDoneAndTotalCount`)
- the matching entity configurations for everything above

**Shared DTO primitives**
- `application/dto/dto/TimeDto.cs` + `application/validator/TimeDtoValidator.cs`
- `application/dto/request/extendable/` and `application/dto/request/generic/`, and the base
  filter/response types
- shared enums under `domain/model/enum/`

**Cross-slice event records** — `application/event/`. Both slices on either side of an event
will depend on Core rather than on each other. The **handlers** in
`application/eventHandler/` stay host-side for now.

**The activity endpoints** (~78, covering Activity + profiles + lookups + timers) have no
other home — move them with Core. Verify the count yourself before starting.

---

## Slice-specific gotchas

**Do not re-add inverse collections to Core entities.** A completed refactor removed 22
collections from `User`, `Activity`, `ActivityRole` and `ActivityCategory` so that Core would
stop pointing into the feature areas. Each of those four files carries a comment saying why it
has none. The collections that legitimately remain are Core→Core only — `User` keeps
`ActivityList`, `CategoryList`, `RoleList`, `MemoryAnchors`, `RefreshTokens`; `Activity` keeps
`MemoryAnchors` plus its `Role` / `Category` references. If you need "this user's planner
tasks", query the DbSet.

**The three `Activity*Profile` entities are not `IEntityWithUser`** and get **no** global query
filter. They are scoped by hand through `p.Activity.UserId == userId` inside
`ApplyCustomFiltering` on the three profile grids. That hand-scoping must survive the move
intact — it is the only thing keeping other users' profiles out.

**Security invariant.** `AppDbContext.OnModelCreating` applies a global query filter to every
`IEntityWithUser`; that is what scopes reads, **not** the endpoints (`ApplyUserScoping` is a
no-op virtual). Core entities must stay `IEntityWithUser` and keep their FKs and cascades.

---

## Also in this commit: band the seeder `Order` values

`IDatabaseSeeder.Order` is a **single global sequence** — it expresses FK dependencies across
all seeders, and truncation runs in reverse. Once seeders live in seven projects a slice can no
longer pick its `Order` in isolation. Fix it now, while every seeder is still in one place.

Current per-user default values collide already: `TaskPriority` 1, `TaskImportance` 2,
`RoutineTimePeriod` 3, `DefaultActivityRole` 4, `UserPlannerSettings` 5, `Calendar` 5, the four
activity lookups all 6, `TimerPreset` 10, `PomodoroTimerPreset` 11. Dev seeders run a separate
5–14 sequence with its own duplicates.

Assign non-overlapping bands with room to grow:

| Band | Slice |
|---|---|
| 000–099 | Core (users, activities, roles, categories, lookups, timers) |
| 100–199 | TodoLists |
| 200–299 | Routines |
| 300–399 | History |
| 400–499 | Planning |
| 500–599 | Reminders |
| 600–699 | Tracking |

⚠ **Banding changes relative order across slices.** Before committing, walk every seeder's FK
dependencies and confirm the new global sequence still satisfies them in both directions
(seeding forward, truncation reverse). Confirm which slice owns `TaskPriority` vs
`TaskImportance` — they are not both in the same one.

Document the bands in Core, next to the seeder interfaces, or the next person will guess.

---

## Done when

- `dotnet build AdhdTimeOrganizer.sln` clean
- `dotnet test` = **198 passed, 6 skipped, 0 failed**
- `dotnet ef migrations add CoreExtraction` produces an **empty** `Up`/`Down`
- one activity endpoint and one timer endpoint manually smoke-tested (a missing FastEndpoints
  registration is a 404, not a build error)
- `docs/summary.md` + `docs/domain-map.md` written in `AdhdTimeOrganizer.Core`, including the
  seeder band table
- one commit