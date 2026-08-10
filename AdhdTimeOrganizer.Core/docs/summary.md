# AdhdTimeOrganizer.Core — Agent Summary

**Purpose:** The shared core every vertical slice depends on. It owns the two hub entities — `User`
and `Activity` — everything hanging directly off them (roles, categories, the four activity lookups,
the three activity profiles, memory anchors, timer presets), the primitives the slices build on
(the closing base shims, the `IsManyWithOneUser` / `IsManyWithOneActivity` configuration helpers, the
shared enums, the extendable/generic request+response bases), and the cross-slice **event records**.

**This is the first project of the portal split.** Read
`review/portal/slicePrompts/00-README.md` for the plan; six more slices (TodoLists, Routines,
History, Planning, Reminders, Tracking) come out of `AdhdTimeOrganizer` after this one, in that
order.

## Bounded context

Owns: the hub entities and their EF configurations, the 78 activity endpoints and 10 timer
endpoints, their DTOs and validators, the activity/timer/user seeders, and `PortalRoleCatalog`.

Does **not** own — and must never reference: `AppDbContext`, `Program.cs`, the migrations, the DI
wiring, or any feature slice. Core sits *below* everything; a reference back to the host would make
the split circular on day one.

## Dependency seams

- **References:** `Sydowwe.Framework` and `Sydowwe.Framework.Contracts` only. No host reference, by
  construction — see the comment in `AdhdTimeOrganizer.Core.csproj`.
- **Referenced by:** `AdhdTimeOrganizer` (the host), and every future slice.
- **Exposes to the host:** `User` (the concrete `TUser`), `PortalRoleCatalog`, `UserDefaultsService`
  (`IUserDefaultsService`), `SeedUserIdProvider` (`ISeedUserProvider` + `ISeedUserIdProvider`), and
  the five event records in `application/event/`.

## Gotchas — things that will bite you

- **Core takes a plain `DbContext`, never `AppDbContext`.** That is the whole reason it can be a
  separate project. `ModuleServiceExtensions.AddModuleServices` aliases `DbContext` →
  `AppDbContext`, so what actually arrives at runtime is the app context with all its global query
  filters intact. Consequences at the call site: no `dbContext.Activities` — use
  `dbContext.Set<Activity>()`; the `DbContextHelper` extensions (`AddEntityAsync`,
  `UpdateEntityAsync`, …) all extend `DbContext` and work unchanged.

- **Do not re-add inverse collections to `User` or `Activity`.** A completed refactor removed 22 of
  them precisely so Core would stop pointing into the feature areas; each of those four files carries
  a comment saying so. What legitimately remains is Core→Core only: `User` keeps `ActivityList`,
  `CategoryList`, `RoleList`, `MemoryAnchors`, `RefreshTokens`; `Activity` keeps `MemoryAnchors`, the
  three profile references and its `Role` / `Category`. For "this user's planner tasks", query the
  DbSet — the global filter scopes it for you.

- **The three `Activity*Profile` entities are not `IEntityWithUser` and get no global query filter.**
  They are scoped by hand, through `p.Activity.UserId == userId`, in `ApplyUserScoping` on the three
  profile grids — deliberately in `ApplyUserScoping` and not `ApplyCustomFiltering`, because the base
  only calls the latter when the request actually carries a filter. That hand-scoping is the only
  thing keeping other users' profiles out. `ActivityProfileGridTests` pins it.

- **Registering Core with the host is four places, none of which break the build.** FastEndpoints
  `o.Assemblies` in `Program.cs` (missing → every activity/timer route silently 404s);
  `ModuleServiceExtensions.ModuleAssemblies` (which `AddDependencyInjection` `Except`s — being in
  *both* scans doubles every `IEnumerable<T>`, so each seeder would run twice, silently);
  `AppDbContext.ApplyHostConfigurations` (missing → the entities are simply absent from the model);
  and the solution file. `CoreRouteSmokeTests` pins the first two.

- **`ApplyHostConfigurations` needs two `ApplyConfigurationsFromAssembly` calls, not one.**
  `UserEntityConfiguration` moved here, so the single `typeof(...).Assembly` that used to cover
  everything now covers only Core; the host's own configurations need their own call (currently
  anchored on `PlannerTaskConfiguration`). Drop one and its tables vanish from the model.

- **Moving a type between projects changes no table or column name.** `BaseEntityConfigure` derives
  those from the **class** name, not the namespace. The `CoreExtraction` migration is empty `Up`/
  `Down` for exactly that reason — but `AppDbContextModelSnapshot.cs` still shows a ~1250-line diff,
  because it keys entities by CLR name and the rename reorders every block. Expect it; never
  hand-edit it.

- **Seeder `Order` is banded now.** Core owns 010–099. See
  `infrastructure/persistence/seeder/SeederOrderBands.md` — read it before adding a seeder anywhere
  in the solution, not just here.

## Navigation

`docs/domain-map.md` is the index: what lives where, and which invariants are load-bearing.
