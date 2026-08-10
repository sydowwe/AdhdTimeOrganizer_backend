# Extract `AdhdTimeOrganizer.Reminders`

`Core`, `TodoLists`, `Routines`, `History` and `Planning` must already exist and be committed.
Reminders depends on **Planning** — `ReminderRegistrationService` queries `PlannerTasks` for
task-linked reminders — so extracting it earlier produces a slice→host reference that will not
compile. That is the only reason this slice, the smallest at ~5 endpoints, comes so late.

---

## Ground rules

- Windows box. Use **PowerShell**. **Never round-trip a file through the shell** (no
  `Get-Content | Set-Content`, no `sed -i`) — codepage 1252 double-encodes UTF-8. Use editor
  tools to change file contents.
- `framework/` is a **git submodule**. Do not touch it. Parent repo only.
- `git mv` so history survives. Change only the root namespace prefix. **Do not rename types.**
- **Never reference the host from a slice.** Reminders → Planning + Core + `Sydowwe.Framework`
  + `Sydowwe.Framework.Contracts`. Host → Reminders.
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
namespace. Run `dotnet ef migrations add RemindersSlice` and confirm `Up`/`Down` are **empty**.
`AppDbContextModelSnapshot.cs` diffs hugely with no schema in it; never hand-edit it.

## Security invariant

The global query filter in `AppDbContext.OnModelCreating` over every `IEntityWithUser` is what
scopes reads — **not** the endpoints (`ApplyUserScoping` is a no-op virtual). Keep the portal
`Reminder` entity `IEntityWithUser` with its FKs and cascades intact.

---

## What moves

- **Endpoints** — the ~5 reminder endpoints. Locate them yourself; confirm the count.
- **Entity** — `domain/model/entity/reminder/Reminder.cs` and `ReminderConfiguration`.
- **Service** — `application/service/reminder/ReminderRegistrationService.cs`.
- **DTOs, validators, seeders** for the above (`Order` values were rebased into the 500–599
  band during the Core commit).

---

## Slice-specific gotchas

**⚠ There are two unrelated "Reminders" in this solution. Do not conflate them.**
- `framework/Sydowwe.Reminders` is an **opt-in framework module inside the git submodule**,
  with its own `ReminderDefinition` entity, its own ledgers, its own retention job and its own
  registrar. **Do not touch it, do not move anything into it, and do not merge the two.**
- The portal's `Reminder` entity and `ReminderRegistrationService` are what you are extracting
  into `AdhdTimeOrganizer.Reminders`.

Namespace collisions are likely (`Sydowwe.Reminders.*` vs
`AdhdTimeOrganizer.Reminders.*`). Fully qualify or alias rather than renaming any type.

**`ReminderRegistrationService` likely talks to the framework module through
`Sydowwe.Framework.Contracts`** (`IReminderRegistry`, and `IQuietHoursReader` for quiet hours).
Check its constructor before wiring project references. Those contract interfaces are the seam;
keep going through them rather than referencing `Sydowwe.Reminders` directly.

**`IQuietHoursReader` must keep resolving to Notifications' `QuietHoursReader`.** The Reminders
module ships a `NoQuietHoursReader` for hosts without Notifications, deliberately carrying no
lifetime marker so it is never auto-registered — an auto-registered no-op would silently disable
quiet hours everywhere. If your new project's assembly ends up in a marker scan that picks it
up, quiet hours break with no error. Verify after wiring.

**`AdhdTimeOrganizer.IntegrationTests/Modules/ModuleWiringTests.cs` pins the composition root.**
It is the test that catches double registration, a missing `ModuleAssemblies` entry, and the
`IQuietHoursReader` resolution. Run it specifically, not just the whole suite, and read its
assertions if anything about this slice's DI feels uncertain.

---

## Done when

- `dotnet build AdhdTimeOrganizer.sln` clean
- `dotnet test` = **216 passed, 6 skipped, 0 failed**, with `ModuleWiringTests` green
- `dotnet ef migrations add RemindersSlice` produces an **empty** `Up`/`Down`
- one reminder endpoint manually smoke-tested (a missing FastEndpoints registration is a 404,
  not a build error)
- `IQuietHoursReader` still resolves to Notifications' `QuietHoursReader`, not the no-op
- `docs/summary.md` + `docs/domain-map.md` written in the new project
- one commit