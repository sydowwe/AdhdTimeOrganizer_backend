# Extract `AdhdTimeOrganizer.Routines`

`AdhdTimeOrganizer.Core` and `AdhdTimeOrganizer.TodoLists` must already exist and be committed.

`Routines → TodoLists` is verified one-way: nothing in the plain to-do area (`todoList/`,
`todoListItem/`, `todoListCategory/`, `taskPriority/`, `steps/`, the shared bases) references
any routine type. Routines references TodoLists; nothing points back.

---

## Ground rules

- Windows box. Use **PowerShell**. **Never round-trip a file through the shell** (no
  `Get-Content | Set-Content`, no `sed -i`) — codepage 1252 double-encodes UTF-8. Use editor
  tools to change file contents.
- `framework/` is a **git submodule**. Do not touch it. Parent repo only.
- `git mv` so history survives. Change only the root namespace prefix. **Do not rename types.**
- **Never reference the host from a slice.** Routines → TodoLists + Core + `Sydowwe.Framework`
  (+ `Sydowwe.Framework.Contracts` if it talks to a framework module). Host → Routines.
- Slice services take a plain **`DbContext`**, never `AppDbContext`. The alias is registered.
- Confirm the baseline first: `dotnet test AdhdTimeOrganizer.IntegrationTests` =
  **219 passed, 6 skipped, 0 failed**. Match it at the end.

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
namespace. Run `dotnet ef migrations add RoutinesSlice` and confirm `Up`/`Down` are **empty**.
`AppDbContextModelSnapshot.cs` diffs hugely with no schema in it; never hand-edit it.

## Security invariant

The global query filter in `AppDbContext.OnModelCreating` over every `IEntityWithUser` is what
scopes reads — **not** the endpoints (`ApplyUserScoping` is a no-op virtual). Keep routine
entities `IEntityWithUser` with their FKs and cascades intact.

---

## What moves

- **Endpoints** — the **20** `.cs` files under `application/endpoint/todoList/routineTodoList/`
  and `application/endpoint/todoList/routineTimePeriod/`. They subclass bases that live in
  TodoLists; that reference is expected and correct.
- **Entities** — `RoutineTodoList`, `RoutineTimePeriod`, `RoutinePeriodCompletion` from
  `domain/model/entity/todoList/`, plus their configurations
  (`RoutineToDoListConfiguration`, and the `RoutineTimePeriod` configuration — note it carries
  **two** unique indexes).
- **Domain service** — `domain/service/RoutineResetService.cs`.
- **Application service** — `application/service/routine/RoutinePeriodNotificationService.cs`.
- **Jobs** — `infrastructure/jobs/RoutineTodoListResetJob.cs` and
  `infrastructure/jobs/RoutinePeriodNudgeJob.cs`.
- **Seeders** — `RoutineTimePeriodSeeder` (per-user default) and the dev `RoutineTodoListSeeder`.
  Their `Order` values were rebased into the 200–299 band during the Core commit.
- **DTOs and validators** for the above.

---

## Slice-specific gotchas

**Quartz registration stays host-side.** The two job classes move, but they are registered in
the single `AddQuartz` block in `Program.cs`, which stays in the host and now references the
slice. Keep `[DisallowConcurrentExecution]` on them, and keep them resolving `DbContext` from a
fresh `IServiceScopeFactory` scope.

**Background inserts have no authenticated user.** `UserId` is filled on insert by
`BaseWithUserEntitySaveChangesAsync` only when an authenticated user is present; a job inserting
an `IEntityWithUser` row without one gets `UserId == 0` and an FK violation. Both routine jobs
run unauthenticated — if they insert (e.g. `RoutinePeriodCompletion`), the `UserId` must be set
explicitly. Do not change this behaviour during the move; just don't break it.

**Seeder reads must keep `IgnoreQueryFilters()`.** `UserScoping` is on, so an `IEntityWithUser`
read inside a seeder is scoped to the *ambient* user. A seeder told to seed a different user
would read back zero rows and re-insert everything. The explicit `UserId` predicate is the
scoping.

**`RoutineTimePeriodSeeder` has two unique indexes to satisfy.** Its `Collides(a, b)` must check
both. Do not simplify it to a `Text` comparison — most per-user default seeders here are keyed
on something other than `Text`.

**⚠ This slice has known open correctness findings — do NOT fix them in this commit.** The
reset job drops grace-expiry streak breaks; it never unticks checklist steps; the two `TryReset`
overloads disagree on streak scoring; a failed notification aborts the nudge sweep and loses its
idempotency markers. Isolating this area with its own tests is *why* it is extracted early, but
a move commit that also changes behaviour makes the empty-migration and green-test gates
meaningless. Move first, fix in follow-up commits.

**`RoutineResetServiceTests` stays in `AdhdTimeOrganizer.IntegrationTests`** (that project pins
host composition and stays in the parent). Its `using` directives will need updating — that is
expected and is not a "slice → host" violation, because the test project references everything.

---

## Done when

- `dotnet build AdhdTimeOrganizer.sln` clean
- `dotnet test` = **219 passed, 6 skipped, 0 failed**
- `dotnet ef migrations add RoutinesSlice` produces an **empty** `Up`/`Down`
- one routine endpoint manually smoke-tested (a missing FastEndpoints registration is a 404,
  not a build error)
- both Quartz jobs still fire — check the boot log
- `docs/summary.md` + `docs/domain-map.md` written in the new project
- one commit