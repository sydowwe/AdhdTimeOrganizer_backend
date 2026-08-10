# Vertical slicing — execution playbook

**Self-contained.** You do not need to read the other files in this folder. `04-slicing-verification.md`
holds the evidence behind these decisions if you want it; nothing here depends on it.

Goal: split the `AdhdTimeOrganizer` portal (712 files, one project) into a shared core plus vertical
slice projects, so future work loads one slice instead of the whole app.

---

## Current state — read this before touching anything

**Done (2026-08-10):** the inverse-navigation refactor that unblocked everything. `User`, `Activity`,
`ActivityRole` and `ActivityCategory` no longer hold collections pointing into the feature areas.
Twenty-two such collections were deleted; ~14 entity configurations changed from
`IsManyWithOneUser(u => u.XColl)` to the parameterless `IsManyWithOneUser()`, and four hand-rolled
`.WithMany(e => e.XList)` became `.WithMany()`. **No column, index, FK or cascade changed.**

Do **not** re-add an inverse collection to a Core entity. Each of the four files carries a comment
saying why it has none; if you need "this user's planner tasks", query the DbSet
(`dbContext.PlannerTasks.Where(t => t.UserId == …)`), which the global query filter scopes anyway.

**Verified:** the portal compiles clean.
**Not verified:** the integration tests — the running app held a file lock at the time. **Run
`dotnet test` before starting any extraction**, so you don't inherit an unknown baseline.

**Still true and load-bearing:** `BaseEntityConfigure` derives table and column names from the
**class** name, not the namespace. Moving these types between projects therefore changes **no table
or column names** — every step below is a C#-and-csproj refactor with no data migration. The one
visible cost is that the next `dotnet ef migrations add` regenerates `AppDbContextModelSnapshot.cs`
with new type names: a huge diff with no schema in it. Expect it; never hand-edit it.

---

## Target graph

```
framework/Sydowwe.Framework                     (git submodule — do not touch)
        │
AdhdTimeOrganizer.Core
   User · Activity · ActivityRole · ActivityCategory · 4 activity lookups
   3 Activity*Profile · MemoryAnchor
   base shims (BaseEntityWithUser, BaseLookupWithUser, BaseEntityWithActivity)
   builder extensions · TimeDto · shared enums · cross-slice event records
        │
        ├── AdhdTimeOrganizer.Timers            pilot — ~20 files
        ├── AdhdTimeOrganizer.History           ActivityHistory + its dashboards
        ├── AdhdTimeOrganizer.Tracking          ingest · mappings · dashboards   ⚠ blocked, see §Seams
        ├── AdhdTimeOrganizer.TodoLists         lists · items · steps · priorities
        │      └── AdhdTimeOrganizer.Routines   periods · routine items · completions · 2 jobs
        ├── AdhdTimeOrganizer.Planning ────────► TodoLists
        └── AdhdTimeOrganizer.Reminders ───────► Planning
        │
AdhdTimeOrganizer  (host — stays)
   Program.cs · AppDbContext · migrations · DI wiring · Serilog
   SuggestionPatternRefreshInterceptor · SuggestionPatternViewInstaller · the 3 pattern views
   user/auth endpoints · DeleteUserAccountEndpoint · GetUserDataExportEndpoint
   the 5 event handlers
```

**Verified acyclic.** Two slice→slice edges, both one-way:
- `Planning → TodoLists` — `PlannerTask.TodolistItemId` + `PlannerTask.TodolistItem`. `TodoListItem`
  has **no** `PlannerTask` navigation, so this does not come back.
- `Reminders → Planning` — `ReminderRegistrationService` queries `PlannerTasks` for task-linked
  reminders.

**Verified:** nothing in the plain to-do area (`todoList/`, `todoListItem/`, `todoListCategory/`,
`taskPriority/`, `steps/`, the three shared bases) references any routine type. `Routines → TodoLists`
is safe.

---

## Slice extraction checklist

Repeat per slice. Land each as its own commit and get a green build + test run before the next.

1. **Create the project.** `dotnet new classlib -n AdhdTimeOrganizer.<Slice> -f net10.0`, add to the
   `.sln`. Copy `<Nullable>`, `<ImplicitUsings>`, `<LangVersion>` and analyzer settings from
   `AdhdTimeOrganizer.csproj` so the slice compiles under identical rules.
2. **References.** Slice → `AdhdTimeOrganizer.Core` + `framework/Sydowwe.Framework`
   (+ `Sydowwe.Framework.Contracts` if it talks to a framework module). Host → the slice.
   **Never** slice → host.
3. **Move the files** (git mv, so history survives). Keep the folder shape inside the new project;
   change only the root namespace prefix. Do not rename types.
4. **Register with the host** — four places, none of which break the build if you forget:
   - `Program.cs`: add the assembly to the **FastEndpoints** `o.Assemblies` list. Missing → the
     slice's endpoints silently **404** (`DisableAutoDiscovery = true` is set).
   - `config/dependencyInjection/ModuleServiceExtensions.cs`: add to `ModuleAssemblies` so the DI
     marker scan finds the slice's services. `DependencyInjectionExtensions` `Except`s this list —
     if you add the assembly to *both* scans, every service registers **twice** and every
     `IEnumerable<T>` doubles (two of each seeder = every seeder runs twice, silently).
   - `AppDbContext`: make sure the slice's assembly is included wherever entity configurations are
     discovered (see `ApplyHostConfigurations`). Missing → the entities are simply not in the model.
   - The `DbContext` → `AppDbContext` alias is already registered. Slice services take a **plain
     `DbContext`**, not `AppDbContext` — that is what keeps them from referencing the host.
5. **Migration round-trip.** `dotnet ef migrations add Slice<Name>Move`. The generated `Up`/`Down`
   must be **empty**. If it isn't, you renamed something — revert and find it.
6. **Verify.** `dotnet build` then `dotnet test`. Then a manual smoke of one endpoint from the slice,
   because a missing FastEndpoints registration is a 404, not a build error.
7. **Docs.** Add `docs/summary.md` + `docs/domain-map.md` to the new project. This is the payoff —
   it is what makes future work in that slice cheap.

---

## Pilot: `AdhdTimeOrganizer.Timers`

~20 files, touches almost nothing. The point is to prove steps 1–7 on something disposable, not to
deliver value. If the pattern turns out wrong, throw the project away.

Files to move:

| From | Count |
|---|---|
| `domain/model/entity/timer/` (`TimerPreset`, `PomodoroTimerPreset`) | 2 |
| `infrastructure/persistence/configuration/timer/` | 2 |
| `application/endpoint/timer/**` | 10 |
| `application/dto/request/timer/`, `application/dto/response/timer/` | 4 |
| `infrastructure/persistence/seeder/userDefault/{TimerPreset,PomodoroTimerPreset}Seeder.cs` | 2 |
| any `application/validator/*Timer*` | check |

Both entities are user-scoped and `TimerPreset` links to `Activity`, so `Timers → Core` and nothing
else. `Endpoints/TimerPresetValidationTests.cs` in the integration-test project exercises this area —
keep it passing.

**Done when:** empty migration, green tests, and a timer endpoint responds.

---

## Recommended order after the pilot

1. **`Routines`** — seam verified clean, and it is the least-correct area in the codebase (the reset
   job drops grace-expiry streak breaks; it never unticks checklist steps; two `TryReset` overloads
   disagree on streak scoring; a failed notification aborts the nudge sweep and loses its idempotency
   markers). Isolating it with its own tests is the biggest correctness win available.
2. **`TodoLists`** — must land with or before Routines, since Routines depends on it. If you do
   Routines first, temporarily leave the shared bases in the host and move them with TodoLists.
3. **`Planning`** — then `Reminders`, which depends on it.
4. **`History`** — only entanglement is the suggestion views, which stay host-side regardless.
5. **`Tracking`** — last. Blocked, see below.

---

## Seams to build first

### Tracking's automation (blocks the Tracking slice)

`application/endpoint/activityTracking/desktop/command/DesktopActivityHeartbeatEndpoint.cs:126-175`
is not just ingest. On each heartbeat it queries today's `PlannerTasks`, compares tracked seconds
against the task's planned duration, **mutates `PlannerTask.Status`**, saves, publishes
`PlannerTaskIsDoneChangedEvent`, and — when no planner task matches — falls back to
`AutomateWithoutPlannerTaskAsync`, which reaches into `TodoListItems` and `RoutineTodoLists`.

So Tracking currently **writes into three other areas**. Before extracting it: have the heartbeat
publish something like `ActivityTimeRecorded(userId, activityId, secondsToday)` and move the
automation into handlers owned by `Planning` / `TodoLists` / `Routines`. Event records go in `Core`.
Side benefit: a large lump of business logic leaves an ingest endpoint.

### Completion fan-out (`Planning ↔ TodoLists`)

The entity FK is one-way, but the *behaviour* runs both directions through FastEndpoints in-process
events. Keep it that way — move `application/event/*` records into `Core` so both slices depend on
`Core` rather than on each other. The five handlers in `application/eventHandler/` can stay host-side
initially; split them per subscribing slice later.

Note two of those events (`ActivityAddedToHistoryEvent`, `ActivityCreatedIsOnTodoListEvent`) are
**never published anywhere** — their handlers are dead code, and `docs/domain-map.md` wrongly lists
them as wired. Decide whether to wire or delete them *before* the split, so you don't carry dead
weight into a new project.

---

## Seeder `Order` must be banded before the first extraction

`IDatabaseSeeder.Order` is a **single global sequence**: it expresses FK dependencies across all
seeders, and truncation runs in reverse. Once seeders live in six projects, a slice can no longer
pick its `Order` in isolation — two slices will collide.

Fix before extracting anything with a seeder in it. Read the current values first, then assign
non-overlapping bands with room to grow, e.g.:

| Band | Slice |
|---|---|
| 000–099 | Core (users, activities, roles, categories, lookups) |
| 100–199 | TodoLists |
| 200–299 | Routines |
| 300–399 | Planning |
| 400–499 | History |
| 500–599 | Tracking |
| 600–699 | Reminders |
| 700–799 | Timers |

Document the bands in `Core`, next to the seeder interfaces, or the next person will guess.

---

## Traps

- **The folder structure lies.** Do not assign files to projects by directory. Known misfilings:
  `configuration/activityHistory/{DesktopActivityEntry,WebExtensionActivityEntry,AndroidSessionData}Configuration.cs`
  are **Tracking**, not History; `configuration/activityPlanning/TaskPriorityConfiguration.cs` is
  **TodoLists**; `domain/model/entity/Calendar.cs` sits at the entity root but belongs to **Planning**.
- **Endpoints that vanish silently.** A slice missing from the FastEndpoints assembly list 404s
  rather than failing to build. Smoke-test one endpoint per slice.
- **Double DI registration.** Adding a slice assembly to both marker scans doubles every
  `IEnumerable<T>`. Nothing throws; seeders just run twice.
- **The global query filter is the security mechanism.** It is applied in `AppDbContext.OnModelCreating`
  over every `IEntityWithUser` and keeps other users' rows out — the endpoints do **not**
  (`ApplyUserScoping` is a no-op virtual). Slice entities must stay `IEntityWithUser` and the host
  must keep applying the filter across all slice assemblies. Do **not** copy the framework modules'
  FK-free, filter-free style: those are host-agnostic by necessity and pay for it with hand-written
  erasure and no read safety net. Your slices are not reusable, so they should keep FKs, cascades and
  the filter.
- **`AdhdTimeOrganizer.IntegrationTests` stays in the parent** — it pins *host composition*, which is
  a property of the host. Per-slice test projects are a split, not a move.
- The three `Activity*Profile` entities are **not** `IEntityWithUser` and have **no** query filter;
  they scope by hand through `p.Activity.UserId == userId`. They belong in `Core` with `Activity`, and
  that hand-scoping must survive the move intact.

---

## Not yet assigned

Work these out during the relevant slice, not up front:

- **62 validators** and **198 DTOs** — mostly follow their slice, but shared primitives (`TimeDto`,
  `application/dto/request/{extendable,generic}/`, the base filter/response types) need a home in `Core`.
- **Dev seeders** (`infrastructure/persistence/seeder/dev/`, 18 files) — follow their slice.
- Whether `Timers` stays a project or folds into `Core` once the pilot has served its purpose.
