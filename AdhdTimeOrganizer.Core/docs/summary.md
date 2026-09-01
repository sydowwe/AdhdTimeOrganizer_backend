# AdhdTimeOrganizer.Core — Agent Summary

**Purpose:** The shared core every vertical slice depends on. It owns the two hub entities — `User`
and `Activity` — the roles and categories hanging off them, the timer presets, the primitives the
slices build on (the closing base shims, the `IsManyWithOneUser` / `IsManyWithOneActivity`
configuration helpers, the three shared enums, the extendable/generic request+response bases), and
the whole **cross-slice seam surface**: the `ISeam` interfaces, the `ISeamEvent` records, and
`PortalRoleCatalog` / `PortalAuthorizationPolicies`.

## Bounded context

Owns: the hub entities and their EF configurations, 40 endpoints (activity · role · category ·
timer presets), their DTOs and the 10 validators, the activity/timer/user seeders, the seam
declarations, and three services (`ActivityReferenceService`, `UserDefaultsService`,
`UserTimeZoneResolver`).

Does **not** own — and must never reference: `AppDbContext`, `Program.cs`, the migrations, the DI
wiring, or any feature slice. Core sits *below* everything; a reference back to the host or to a
slice would make the split circular.

Does **not** own the four activity lookups, the three `Activity*Profile` entities or `MemoryAnchor`
either — those are **`AdhdTimeOrganizer.ActivityProfiles`**, the one slice carved out of Core rather
than out of the host. `Activity` and `User` lost five navigation properties in that move
(`BacklogProfile`, `ProjectProfile`, `BucketListProfile`, `Activity.MemoryAnchors`,
`User.MemoryAnchors`); do not add them back.

## Dependency seams

- **References:** `Sydowwe.Framework` and `Sydowwe.Framework.Contracts` only. No host reference, by
  construction — see the comment in `AdhdTimeOrganizer.Core.csproj`.
- **Referenced by:** the host and all six slices. Core is the only thing they share.
- **Exposes to the host:** `User` (the concrete `TUser`), `PortalRoleCatalog`,
  `PortalAuthorizationPolicies`, `UserDefaultsService` (`IUserDefaultsService`), `SeedUserIdProvider`
  (`ISeedUserProvider` + `ISeedUserIdProvider`), `IUserTimeZoneResolver`, `IActivityReferenceService`.
- **Exposes to the slices:** the seam surface. Five `ISeam` interfaces and four `ISeamEvent` records,
  all declared here so no slice ever names another. `application/seam/README.md` is the registry —
  **read it before adding any cross-slice call**, including its "how each one fails" section.
- **Consumes back:** Core is also a seam *consumer*. `GridActivityEndpoint`, `GetByIdActivityEndpoint`
  and `MergeActivityEndpoint` go through `IActivityReferenceService`, which folds every slice's
  `IActivityReferenceSource` into one query. Core even implements one itself
  (`TimerPresetActivityReferenceSource`) rather than special-casing its own tables.

## Gotchas — things that will bite you

- **Core takes a plain `DbContext`, never `AppDbContext`.** That is the whole reason it can be a
  separate project. `ModuleServiceExtensions.AddModuleServices` aliases `DbContext` → `AppDbContext`,
  so what arrives at runtime is the app context with every global query filter intact. At the call
  site: no `dbContext.Activities` — use `dbContext.Set<Activity>()`; the `DbContextHelper` extensions
  (`AddEntityAsync`, `UpdateEntityAsync`, …) all extend `DbContext` and work unchanged.

- **`AppQueryFilters` lives in this project but keeps the host's namespace**
  (`AdhdTimeOrganizer.infrastructure.persistence`, not `AdhdTimeOrganizer.Core.…`). It is here
  because slice endpoints have to name `AppQueryFilters.ActivityArchived` to lift it, and the
  namespace was left alone so the host's ~40 call sites did not all churn. Don't "fix" it.

- **`IsArchived` is a flag, not a soft delete, and `PATCH /activity/{id}/archived` is its only
  writer.** The `ActivityArchived` global filter is declared **host-side** in `AppDbContext` — on
  `Activity` and mirrored onto six dependents — so an archived activity keeps rendering its name on
  the history rows and planner tasks that reference it while disappearing from every picker. Never
  let `ActivityRequest` learn about `IsArchived`: the edit form does not carry it, so an assignment
  there would silently un-archive on every save. **Archiving exists on `Activity` and nowhere else** —
  roles, categories and both preset kinds have no retire path, so the only way to get a stale role out
  of the pickers is to delete it, which is the destructive option the next gotcha is about.

- **Deleting a lookup is not a small delete, and the two lookups answer differently.** `ActivityRole`
  and `ActivityCategory` sit *above* `Activity`, so a cascade from either continues through all 17 FKs
  into `Activity` and takes history, planner tasks, to-do items, profiles, presets and tracker mappings
  with it. Category is `SetNull` (optional FK — the activity survives uncategorised, 204); role is
  refused with a 409 by `DeleteActivityRoleEndpoint` when anything is still filed under it (required
  FK, so `SetNull` is unavailable). Both counts lift `ActivityArchived` — an archived activity cascades
  like any other. `usageCount` / `canDelete` now reach both lookups, but only on their **grid and
  get-by-id**; everywhere else, including the role and category nested in an `ActivityResponse`, the
  fields are defaults meaning "not asked for". And on a category `canDelete` is advisory — the delete
  still succeeds. `ActivityLookupDeleteCascadeTests` and the second inventory in
  `ActivityForeignKeyInventoryTests` are the guards; note that the first inventory structurally could
  not see this, since it only walks FKs whose *principal* is `Activity`.

- **Do not re-add inverse collections to `User` or `Activity`.** A completed refactor removed 22 of
  them precisely so Core would stop pointing into the feature areas; the ActivityProfiles extraction
  removed the last 5. What legitimately remains is Core→Core only: `User` keeps `ActivityList`,
  `CategoryList`, `RoleList`, `RefreshTokens`; `Activity` keeps only `Role` / `Category`; `ActivityRole`
  and `ActivityCategory` keep `Activities`. For "this user's planner tasks", query the DbSet — the
  global filter scopes it for you. `IsManyWithOneUser()` / `IsManyWithOneActivity()` take the
  navigation argument **optionally**, which is what made the removal possible.

- **Core's tables span three schemas, not one.** `entity.user` → `user`, `entity.activity` →
  `activity`, `entity.timer` → `public`. `ModuleSchemas.SchemaByCoreNamespace` maps Core **by
  namespace** while every other project maps by assembly, so moving a Core entity between those three
  namespaces silently moves its table between schemas. Hand-written SQL must name the schema.

- **Registering Core with the host is four places, none of which break the build.** FastEndpoints
  `o.Assemblies` in `Program.cs` (missing → every activity/timer route silently 404s);
  `ModuleServiceExtensions.ModuleAssemblies` (which `AddDependencyInjection` `Except`s — being in
  *both* scans doubles every `IEnumerable<T>`, so each per-user seeder would run twice, silently);
  `AppDbContext.ApplyHostConfigurations`; and the solution file. `CoreRouteSmokeTests` pins the first
  two.

- **`ApplyHostConfigurations` needs two `ApplyConfigurationsFromAssembly` calls, not one.**
  `UserEntityConfiguration` lives here, so the single `typeof(...).Assembly` that used to cover
  everything now covers only Core; the host's own configurations need their own call. Drop one and
  its tables vanish from the model.

- **Seam registration fails silently in the worst direction.** The keyed `IEnumerable<>` seams
  (`IActivityMembershipSource`, `IActivityReferenceSource`) simply contribute nothing when a slice
  drops out of `ModuleAssemblies` or a key is renamed: `usageCount` reads lower, so an activity with a
  year of history shows `Records: 0` and an enabled delete button, and `POST /activity/merge` leaves
  that slice's rows pointing at an activity it then deletes — cascading them away with a
  plausible-looking count in the response. The three single-resolution seams throw at endpoint
  activation instead, which is exactly why they are registered that way. `SeamWiringTests` +
  `ActivityMergeTests` are the guards.

- **Moving a type between projects changes no table or column name.** `BaseEntityConfigure` derives
  those from the **class** name, not the namespace. Renaming a Core type *is* a migration; moving one
  is not — but expect a large `AppDbContextModelSnapshot.cs` diff either way, and never hand-edit it.

- **Seeder `Order` is banded.** Core owns 010–099, **shared with ActivityProfiles** because the dev
  chain genuinely interleaves (lookups 10–13 → role 20 → category 30 → activity 40 → profiles 50–52 →
  anchors 60). Read `infrastructure/persistence/seeder/SeederOrderBands.md` before adding a seeder
  anywhere in the solution.

## Extension playbook

- **Add an activity-side field:** entity → its `IEntityTypeConfiguration` (`BaseEntityConfigure()`
  first, never hand-rolled `ToTable`/`HasKey`) → request/response DTOs → validator → migration in the
  **host**. If the field is user-visible on the grid, check whether it needs to be projected in SQL
  rather than post-processed — see `GridActivityEndpoint`.

- **Add an endpoint:** check `framework/Sydowwe.Framework/application/endpoint/base/` first; there is
  a base for every CRUD, grid, filter/sort and by-field pattern. Convention is
  `<Verb><Entity>Endpoint`, mapping lives on the DTOs. Default roles are User+Admin+Root — correct for
  everything in Core. If the endpoint must reach an archived activity, override
  `IgnoredQueryFilters => [AppQueryFilters.ActivityArchived]` (see `DeleteActivityEndpoint`) rather
  than writing a `Where`. A raw `Endpoint<>` has no such hook — use a keyed `FirstOrDefaultAsync` with
  `IgnoreQueryFilters([…])` and declare `Roles(AllowedRoles())` by hand, as `QuickEditActivityEndpoint`,
  `CloneActivityEndpoint` and `SetArchivedActivityEndpoint` do. `FindAsync` cannot lift a filter.

- **Add a cross-slice call:** don't. Add a seam. Decide interface vs. event by *who owns the
  transaction and who owns the decision*, then follow the four steps in `application/seam/README.md`
  — declare here, implement in the owning slice's `application/seam/` folder marked `IScopedService`
  (never marked `ISeam`, which Scrutor would register as), confirm the slice is in `ModuleAssemblies`,
  add a row to that README and a case to `SeamWiringTests`.

- **Add a new entity type that references `Activity`:** derive from `BaseEntityWithActivity`,
  configure the FK from the dependent side with the parameterless `IsManyWithOneActivity()`, and
  update **both** the frozen inventory in `ActivityForeignKeyInventoryTests` and — if the rows must
  survive a merge — the owning slice's `IActivityReferenceSource`.

- **Add a seeder:** pick the kind by owner (app-wide / per-user) × purpose (production / dev fixture),
  read `SeederOrderBands.md`, take a value inside 010–099 leaving gaps of 10. Only dev seeders
  truncate. Per-user defaults key off `Collides`, never row counts.

## Deeper reference

- `domain-map.md` — model, invariants, business rules, navigation index
- `testing.md` — test strategy and the guards that must not be deleted
- `../application/seam/README.md` — the seam registry
