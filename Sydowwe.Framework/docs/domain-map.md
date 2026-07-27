# Sydowwe.Framework — Domain Map

The framework's "domain" is its own persisted model (identity + audit) plus the **contracts** every business entity inherits. This is the template-faithful map;
`architecture.md` is the broader code navigation index — they overlap on the navigation table by design so you can compare the two views.

## Model

The base entity hierarchy every persisted type derives from, plus the concrete entities the framework itself owns (identity + audit).

```mermaid
erDiagram
    IEntityWithId ||..|| BaseEntity : implements
    BaseEntity ||--|| BaseTableEntity : "base of"
    BaseTableEntity ||--|| BaseEntityWithUser : "base of"

    BaseEntity {
        long Id PK
    }
    BaseTableEntity {
        DateTime CreatedTimestamp
        DateTime ModifiedTimestamp
        uint row_version "concurrency token"
    }
    BaseEntityWithUser {
        long UserId FK
    }

    User ||--o{ RefreshToken : has
    User ||--o{ BaseEntityWithUser : owns
    User }o--o{ UserRole : "via user__role"
    AuditLog }o--o| User : "changed by"
    BusinessAuditLog }o--o| User : "actor"

    User {
        long Id PK
        string Email
        string PasswordHash
    }
    UserRole {
        long Id PK
        string Name "User|Admin|Root"
    }
    RefreshToken {
        long Id PK "NoAudit"
        string Token
        DateTime ExpiresAt
    }
    AuditLog {
        long Id PK
        DateOnly Date PK "partition key"
        string EntityName
        long EntityId
        AuditActionEnum Action
        jsonb Before
        jsonb After
        "text[]" ChangedProperties
        long UserId
        string CorrelationId
    }
    BusinessAuditLog {
        long Id PK
        string EventType
        jsonb Payload
        long UserId
    }
```

## Invariants

Each assumption the code relies on, and whether the database enforces it or only the app does — the gap that turns into data-corruption bugs.

- **Every `BaseTableEntity` has `row_version`** — *DB-enforced* (concurrency token via `BaseEntityConfigure`). Conflicting writes throw
  `DbUpdateConcurrencyException` → HTTP 409 from `BaseUpdateEndpoint`. **Scope:** the base update path loads the entity fresh, so the token only prevents two in-flight requests from racing
  (load-then-save overlap). It does **not** protect against stale-form submits (user opens a form, another admin edits, first user submits later — that silently overwrites). For entities that need
  full stale-form protection, implement `IUpdateRequestWithRowVersion<TEntity>`
  on the request DTO; the handler will honour the client-supplied token.
- **`Created/ModifiedTimestamp` are always set** — *both*: DB default `now()`
  **and** app-stamped in `DbContextHelper.BaseSaveChangesAsync` (called from
  `BaseDbContext.SaveChangesAsync`). If you save through a plain `DbContext`
  bypassing the helper, only the DB default fires.
- **`BaseEntityWithUser.UserId` is set on insert** — *DB-enforced NOT NULL* when configured via `IsManyWithOneUser`/`IsOneWithOneUser` (both call `.IsRequired()`). Filled by
  `BaseWithUserEntitySaveChangesAsync` only when a user is authenticated; background/system inserts without an authenticated user leave `UserId == 0` (long default) and **fail with an FK violation**,
  not a silent null. Do not save user-owned entities from unauthenticated/background contexts.
- **Table name = snake_case (type name) with trailing `Read` stripped** — *app-enforced* convention in `BaseEntityConfigure`; nothing stops a hand-rolled
  `ToTable` from diverging.
- **Audit rows commit in the same transaction as the business save** — *app-enforced* by `AuditSaveChangesInterceptor` (opens its own transaction when none exists). A raw SQL / bulk write that
  bypasses the ChangeTracker is **not**
  audited.
- **`audit_log` PK is `(Id, Date)` and partitioned by `Date` (yearly RANGE)** — *DB-enforced*. A write dated outside the configured `FirstYear..FirstYear+YearCount`
  range lands in the default partition.
- **`Id` is a serial/identity column** — *DB-enforced*; never assign it manually.

## Business rules / domain logic

The rules an agent must not break:

- **Audit capture (`AuditSaveChangesInterceptor`).** For every non-`[NoAudit]`
  `BaseTableEntity`: `Added`/`Modified`/`Deleted` → an `AuditLog` row with before/after JSONB snapshots, `ChangedProperties` on updates, `UserId`
  (`ILoggedUserService`), `CorrelationId` (`Activity.Current.TraceId`). Updates whose only changed columns are `row_version` / `CreatedTimestamp` /
  `ModifiedTimestamp` write **nothing**, and those columns are stripped from every snapshot. `[AuditIgnore]` excludes a single property (e.g. `Employee.BirthNumber`,
  `Employee.Salary`) while still auditing the entity. `[NoAudit]` skips the whole entity (e.g. `RefreshToken`). `User` is `IdentityUser` — not a `BaseTableEntity`
  — so it is never audited and needs neither attribute.
- **Bulk-op blind spot.** `ExecuteUpdateAsync` / `ExecuteDeleteAsync` skip the ChangeTracker, so the interceptor never sees them → no audit. Use tracked saves in audited paths, or log manually via
  `IAuditService.LogAsync`.
- **Business events vs CRUD diffs.** Semantic events (not raw column diffs) go to
  `business_audit_log` via `IAuditService.LogAsync(eventType, payload?, …)`, queued onto the ambient `DbContext` and committed with the surrounding save.
  `business_audit_log` is **not** partitioned.
- **Result-based CRUD.** `DbContextHelper` wraps `SaveChanges` and returns
  `Result` / `Result<T>` rather than throwing; `DbUtils.HandleException` maps EF/Npgsql failures to a `ResultErrorType`, and `EndpointHelper.ToStatusCode`
  maps that to an HTTP status. `AddRangeAsync` is transactional and chunks at 300.
- **Role hierarchy is cumulative.** `GetUserRole` (User + Admin + Root) ⊇
  `GetAdminRole` (Admin + Root). Base endpoints default to `GetUserRole()`; admin-only surface has to override to `GetAdminRole()`.
- **Reads project in the database.** Base read endpoints call the response's
  `static Projection(IQueryable)` over an `AsNoTracking` set — entities are never fully materialized for reads.

## Glossary

| Term              | Meaning                                 | Code                                                    |
|-------------------|-----------------------------------------|---------------------------------------------------------|
| Audit log         | Automatic row-level CRUD change history | `AuditLog`, `audit_log` (partitioned)                   |
| Business audit    | Semantic/domain event log               | `BusinessAuditLog`, `business_audit_log`                |
| Concurrency token | Optimistic-lock version column          | `row_version` (uint)                                    |
| Soft delete       | Deactivate instead of remove            | `ISoftDeletableEntity.IsActive`, `SetActiveStatusAsync` |
| Select option     | id + text pair for dropdowns            | `SelectOptionResponse`, `BaseGetSelectOptionsEndpoint`  |
| Lookup entity     | Small reference table (name/text/color) | `IBaseNameTextColorEntity`, `LookupBaseConfiguration`   |
| Projection        | DB-side read shaping                    | `IProjectionResponse<TResponse,TEntity>.Projection`     |
| Decorator service | Wraps a Core service registration       | `IDecoratorService` + `services.Decorate`               |
| Root admin        | Highest privilege role (seeded)         | `UserRoleEnum.Root`, `DefaultUsersSeeder`               |

## Navigation index

The framework-owned entities and their configuration. (For the full code index — endpoints, helpers, services, extensions — see `architecture.md`.)

| Name                                                                  | Kind            | Responsibility                                                | Path                                             |
|-----------------------------------------------------------------------|-----------------|---------------------------------------------------------------|--------------------------------------------------|
| `BaseEntity` / `BaseTableEntity` / `BaseEntityWithUser`               | entity base     | the inheritance spine (Id → timestamps → user)                | `domain/entity/base/`, `domain/entity/user/`     |
| `User` / `UserRole` / `RefreshToken`                                  | identity entity | ASP.NET Identity user/role + JWT refresh token                | `domain/entity/user/`                            |
| `AuditLog` / `BusinessAuditLog`                                       | entity          | CRUD audit (partitioned) / business-event audit               | `domain/audit/`                                  |
| `UserEntityConfiguration` / `UserRoleEntityConfiguration`             | config          | identity table mapping                                        | `infrastructure/persistence/configuration/user/` |
| `AuditLogEntityConfiguration` / `BusinessAuditLogEntityConfiguration` | config          | audit mapping + yearly partitioning (`FirstYear`/`YearCount`) | `infrastructure/persistence/configuration/`      |
| `NoAuditAttribute` / `AuditIgnoreAttribute`                           | attribute       | opt entity / property out of auditing                         | `domain/audit/`                                  |
| `AuditActionEnum`                                                     | enum            | Insert / Update / Delete                                      | `domain/audit/AuditActionEnum.cs`                |
| `UserRoleEnum`                                                        | enum            | User · Admin · Root                                           | `domain/enum/UserRoleEnum.cs`                    |
| `DefaultUsersSeeder` / `UserRoleSeeder`                               | seeder          | seed root admin + roles                                       | `infrastructure/persistence/seeder/`             |
