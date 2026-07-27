# Sydowwe.Framework — Agent Summary

**Purpose:** The cross-solution foundation library — base entities, `BaseDbContext`, EF Core builder/CRUD helpers, audit interceptor, generic FastEndpoints base classes, identity/JWT/2FA auth, the
seeder framework, and the `Result` type.

**Bounded context:** Owns *domain-agnostic infrastructure* only. It knows about
`User` / `UserRole` / `RefreshToken` and the audit tables, but **nothing** about Attendance, Inventory, Employee, etc. Business modules depend on it; it never depends on them. The concrete
`BaseDbContext`, the host wiring, and all feature entities live in the portals / Core modules. The materialized-view refresh helper is generic (`RefreshMaterializedViewAsync(string viewName)`);
domain-specific wrappers such as `RefreshAttendanceViewAsync` live in their own modules. Sort-key remapping for domain fields (e.g. "address" → "addressComputed") is done by overriding
`PreprocessSortBy` in the portal endpoint, not in the framework.

## Dependency seams

- **Consumed by:** every `AdhdTimeOrganizer.*` module and every
  `*.AdminPortal`. They derive entities from the base hierarchy, derive endpoints from the base endpoints, and derive a concrete `BaseDbContext`.
- **Exposes to hosts:** `BaseDbContext` (abstract `partial`, with an
  `OnModelCreatingPartial` hook), the DI marker interfaces, and the four seeder-manager
  contracts (`IAppWideDefaultSeederManager`, `IPerUserDefaultSeederManager`,
  `IAppWideDevSeederManager`, `IPerUserDevSeederManager`).
- **Host must supply:** an `ISeedUserProvider` implementation. The framework has no mapped user
  entity to query, so the per-user managers can't find users without it.
- **Host must wire (in the `AddDbContext` callback for the concrete context):**
  `AuditSaveChangesInterceptor` via `options.AddInterceptors(...)` **and**
  `PartitionedNpgsqlMigrationsSqlGenerator` via `ReplaceService<IMigrationsSqlGenerator,…>()`
  — in **both** the runtime callback and the design-time factory.
- **DI registration:** marker interfaces (`IScopedService`, `ISingletonService`,
  `ITransientService`, `IMapperService`) are auto-registered by a Scrutor
  `services.Scan(...)` in the portal's `AddCore()`. `IMapperService` is registered
  `AsSelf` (singleton); the others `AsImplementedInterfaces`. `IDecoratorService`
  classes are wrapped over an existing Core registration via `services.Decorate(...)`
  in the portal — the decorated interface is inferred from the decorator's **first constructor parameter**.

## Gotchas — things that will bite you

- **The base endpoints do NOT take a `TMapper` generic.** (The root CLAUDE.md describes an older `…<TEntity, TRequest, TMapper>` shape — the code has moved on.)
  The real contracts are:
    - **Reads** use a static-abstract projection: `TResponse : IIdResponse,
    IProjectionResponse<TResponse, TEntity>` — you implement
      `static IQueryable<TResponse> Projection(IQueryable<TEntity>)`. Reads are
      `AsNoTracking` and project in the DB, never load full entities.
    - **Create** uses `TRequest : ICreateRequest<TEntity>` exposing `TEntity ToEntity`.
    - **Update** uses `IUpdateRequest`; **patch** implements `Mapping(entity, req)`.
- **`AllowedRoles()` defaults to `GetUserRole()`** (User + Admin + Root) on every base endpoint — this app's users are all plain `User`, so an admin-only default made the endpoints unreachable.
  Override to `GetAdminRole()` (Admin + Root) for genuine admin surface. Role names come from `UserRoleEnum` (User · Admin · Root); helpers `GetUserRole`/`GetAdminRole` return the cumulative arrays
  from the canonical `UserRoles` groups in `domain/helper/EndpointExtensions.cs`. **Because the default is now User, a read that spans rows of every user must override `ApplyUserScoping` (
  Grid/Filter/Sort/FilterSort) or it leaks.**
- **Auditing is automatic for every `BaseTableEntity`** via the
  `AuditSaveChangesInterceptor`. It opens its **own transaction** when there isn't one, writes audit rows in the same commit, and skips updates whose only diff is
  `row_version`/`CreatedTimestamp`/`ModifiedTimestamp`. `ExecuteUpdateAsync`/
  `ExecuteDeleteAsync` bypass the ChangeTracker → **no audit** → avoid them in audited paths (or call `IAuditService.LogAsync` manually).
- **Timestamps + `UserId` are stamped in `BaseDbContext.SaveChangesAsync`**, not by you: `BaseSaveChangesAsync()` sets `Created/ModifiedTimestamp`,
  `BaseWithUserEntitySaveChangesAsync` fills `UserId` on insert for
  `IEntityWithUser` when a user is authenticated. If you save via a raw
  `DbContext` (not `BaseDbContext`), call `BaseSaveChangesAsync()` yourself.
- **Table naming:** `BaseEntityConfigure<T>()` snake-cases the type name and **strips a trailing `Read`** (for view/projection entities). Always call it first in a configuration; don't hand-roll
  `ToTable`/`HasKey`/`row_version`.
- **`audit_log` is partitioned by year** (`AuditLogEntityConfiguration.FirstYear`
  / `YearCount`); composite PK `(Id, Date)`. `business_audit_log` is not.
- **The 2FA single-use guard (`TwoFactorAuthService`) is only as distributed as `IDistributedCache`.**
  Program.cs registers `AddDistributedMemoryCache()`, so "one attempt per password step" is
  per-process today. Swap in a real distributed cache (e.g. Redis) before scaling to more than one instance.

## Extension playbook

- **New persisted entity:** derive from `BaseEntity` (Id only — views),
  `BaseTableEntity` (timestamps + audit + row_version), or `BaseEntityWithUser`
  (adds `UserId`/`User`). Write an `IEntityTypeConfiguration` that calls
  `BaseEntityConfigure()` first, then `EnumColumn`/`FlagsEnumColumn`/`PriceColumn`/
  `StoredComputedColumn` / `IsManyWithOneUser` as needed.
- **New CRUD endpoint:** pick the matching base in
  `application/endpoint/base/` (`BaseCreateEndpoint`, `BaseUpdateEndpoint`,
  `BasePatchEndpoint`, `BaseDeleteEndpoint`, `BaseBatchDeleteEndpoint`,
  `BaseGetByIdEndpoint`, `BaseGetAllEndpoint`, `BaseGetSelectOptionsEndpoint`,
  `BaseGridEndpoint`, `BaseFilterSortEndpoint`, `BaseFilterEndpoint`,
  `BaseSortEndpoint`, `BaseToggleIsHiddenEndpoint`). Implement the request DTO (`ICreateRequest<T>` / `IUpdateRequest`) and/or response (`IProjectionResponse`). Override `AllowedRoles()` /
  `ApplyCustomFiltering()` / the `Before*`/`After*`
  hooks as needed. Add a matching test subclass from `Sydowwe.Framework.Testing`.
- **New injectable service:** implement the business interface **and** the lifetime marker (`IScopedService` / `ISingletonService` / `ITransientService`). No manual registration — the portal's
  `AddCore()` scan picks it up.
- **Decorate a Core service:** implement the decorated interface + `IScopedService`
    + `IDecoratorService`, taking the inner service as a ctor parameter (any position — resolution is order-independent).
- **Business/domain audit event:** inject `IAuditService` and call
  `LogAsync(eventType, payload?, entityName?, entityId?)` — commits with the surrounding save.
- **New seeder:** pick the kind by two questions — *who owns the rows* and *is this production data
  or a fixture* — then set `Order` + `SeederName` and add a lifetime marker; the matching manager
  finds it via the DI scan.

  |                | App-wide (no user owner) | Per-user                 |
  |----------------|--------------------------|--------------------------|
  | **Production** | `IAppWideDefaultSeeder`  | `IPerUserDefaultSeeder`  |
  | **Dev fixture**| `IAppWideDevSeeder`      | `IPerUserDevSeeder`      |

  Only the `…DevSeeder` kinds declare `TruncateTable` (managers call it in reverse `Order` when
  overriding). Default seeders upsert in place instead — see `architecture.md` for why.

## Deeper reference

- `architecture.md` — navigation index of every base class, service, helper, and extension, grouped by area, with paths.
- `../Sydowwe.Framework.Testing/summary.md` — the test-base mirror.
