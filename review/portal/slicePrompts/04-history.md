# Extract `AdhdTimeOrganizer.History`

`Core`, `TodoLists` and `Routines` must already exist and be committed — History filters into
both of the latter two, so extracting it earlier produces a slice→host reference that will not
compile.

---

## Ground rules

- Windows box. Use **PowerShell**. **Never round-trip a file through the shell** (no
  `Get-Content | Set-Content`, no `sed -i`) — codepage 1252 double-encodes UTF-8. Use editor
  tools to change file contents.
- `framework/` is a **git submodule**. Do not touch it. Parent repo only.
- `git mv` so history survives. Change only the root namespace prefix. **Do not rename types.**
- **Never reference the host from a slice.** History → TodoLists + Routines + Core +
  `Sydowwe.Framework`. Host → History.
- Slice services take a plain **`DbContext`**, never `AppDbContext`. The alias is registered.
- Confirm the baseline first: `dotnet test AdhdTimeOrganizer.IntegrationTests` =
  **216 passed, 6 skipped, 0 failed**. Match it at the end.

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
namespace. Run `dotnet ef migrations add HistorySlice` and confirm `Up`/`Down` are **empty**.
`AppDbContextModelSnapshot.cs` diffs hugely with no schema in it; never hand-edit it.

## Security invariant

The global query filter in `AppDbContext.OnModelCreating` over every `IEntityWithUser` is what
scopes reads — **not** the endpoints (`ApplyUserScoping` is a no-op virtual). Keep
`ActivityHistory` `IEntityWithUser` with its FKs and cascades intact.

---

## What moves

- **Endpoints** — all **14** `.cs` files under `application/endpoint/activityHistory/`:
  create / update / delete, `GetById`, `Filter`, `GetFilteredTable`, `FormSelectOptions`, the
  three `HistoryDetail*` dashboard endpoints, the three `HistorySummary*` ones, and
  `CalendarActivityEndpoint`.
- **Entity** — `ActivityHistory` and its configuration.
- **DTOs** — `application/dto/request/history/`, `application/dto/filter/history/`.
- **Validator** — `application/validator/ActivityHistoryValidator.cs`.
- **Seeder** — the dev `ActivityHistorySeeder` (its `Order` was rebased into the 300–399 band
  during the Core commit).

---

## Slice-specific gotchas

**The one outbound edge is a filter block, and it is the reason Routines must land first.**
`application/endpoint/activityHistory/activityHistory/query/GetFilteredTableActivityHistoryEndpoint.cs`
(around lines 91–106) filters on `dbContext.TodoListItems` and `dbContext.RoutineTodoLists`
through `Any(...)` subqueries — the `IsFromTodoList` / `TaskPriorityId` /
`IsFromRoutineTodoList` / `RoutineTimePeriodId` filters. These are deliberate subqueries (they
translate to `EXISTS`), written to replace cross-slice navigation collections that were removed.
**Keep them as subqueries.** Do not "simplify" them back into navigations — that would re-create
the cycle the whole split exists to avoid.

**The suggestion-pattern machinery stays host-side. All of it.** Do not move:
- `infrastructure/persistence/interceptors/SuggestionPatternRefreshInterceptor.cs`,
  `SuggestionPatternRefreshQueue.cs`, `ISuggestionPatternRefreshQueue.cs`,
  `SuggestionPatternView.cs`
- `infrastructure/persistence/SuggestionPatternViewInstaller.cs`
- `infrastructure/jobs/SuggestionPatternRefreshJob.cs`
- `domain/model/entity/suggestion/ActivityHistoryPattern.cs` and
  `configuration/suggestion/ActivityHistoryPatternConfiguration.cs`
- `infrastructure/persistence/sqlScripts/*.sql` — the three materialized views

These span History + Planning + Calendar, are wired into `Program.cs`, and are the host's
concern regardless of how the slices end up. The interceptor fires on any save touching
`ActivityHistory`, so it will now reference the slice — that is host → slice, which is fine.

**The interceptor must keep working after the move.** Without the three materialized views a
save touching `ActivityHistory` fails with Postgres error 42P01. The running app gets them from
`SuggestionPatternViewInstaller` (embedded resources, called from `Program.cs`); the test
fixture gets them from `AdhdTimeOrganizer.IntegrationTests/Infrastructure/AppDbContextFixture.cs`
via `OnSchemaCreatedAsync`, reading `sqlScripts/*.sql` copied next to the test binaries by a
`Content` item. Both paths read the same three files. If history tests start failing with 42P01,
that plumbing is what broke.

**`CalendarActivityEndpoint` is filed under History but reads calendar-shaped data.** Confirm
which entity it actually queries before assuming it belongs here — if it reads the `Calendar`
entity it belongs to Planning, which has not been extracted yet, and it should stay behind.

---

## Done when

- `dotnet build AdhdTimeOrganizer.sln` clean
- `dotnet test` = **216 passed, 6 skipped, 0 failed**
- `dotnet ef migrations add HistorySlice` produces an **empty** `Up`/`Down`
- one history endpoint and one dashboard endpoint manually smoke-tested (a missing
  FastEndpoints registration is a 404, not a build error)
- a save touching `ActivityHistory` still succeeds — proves the interceptor and views survived
- `docs/summary.md` + `docs/domain-map.md` written in the new project
- one commit