# Module Documentation

Each feature module documents itself in its own `docs/` folder. **Before working in a module,
read its `docs/summary.md`** — it orients you and points to the navigation index in
`docs/domain-map.md` so you open only the files you need.

Modules with docs today: `AdhdTimeOrganizer.Notifications`, `AdhdTimeOrganizer.Reminders`,
`AdhdTimeOrganizer.Scheduler`, `Sydowwe.Framework`, `Sydowwe.Framework.Testing`.

> `docs/modules.md`, `docs/extendingVanillaForCustomers.md` and `docs/testing.md` at the repo root
> were copied from the MojaDigitalnaFirma solution and describe modules/types that do **not** exist
> here. Don't trust them; the per-project `docs/summary.md` files are the accurate ones.

# Solution Layout

- `AdhdTimeOrganizer` — the portal (entities, endpoints, `AppDbContext`, migrations).
- `Sydowwe.Framework` — the shared framework, used by **the portal and the modules alike**. Base
  entities, base endpoints, builder extensions, DbContext helpers, seeders, auth services.
- `AdhdTimeOrganizer.Notifications` / `.Reminders` / `.Scheduler` — opt-in module projects built on
  the `Sydowwe.Framework` primitives.
- `AdhdTimeOrganizer.IntegrationTests` + `Sydowwe.Framework.Testing`.
- `MojaDigitalnaFirma.Kernel` and `AdhdTimeOrganizer/reference/mojaCore/` are reference/foreign
  code — don't extend them.

**Which copy to use: there is one copy.** The portal's parallel set of primitives was deleted in the
framework reconciliation — reach for `Sydowwe.Framework.*` from portal code too. What still lives in
the portal is only what names a portal-specific type: the two `BaseEntityWithUser` / `BaseLookupWithUser`
closing shims, `IsManyWithOneUser` / `IsOneWithOneUser`, and a handful of entity-specific config
helpers. The old `EndpointHelper` / `DateTimeExtensions` name collisions are gone — the portal copies
are now `PortalEndpointHelper` and `TimeOnlyExtensions`. The portal/framework reconciliation is
finished; there is no outstanding duplicate to merge.

# Entity Conventions

The base hierarchy is **Framework-only** — `Sydowwe.Framework/domain/entity/`, with the marker
interfaces in `Sydowwe.Framework/domain/entityInterface/`. The portal keeps no copies, only two
closing shims (below). Portal and module entities alike derive from:

- `base/BaseEntity.cs` — `long Id` only (implements `IEntityWithId`), for SQL views /
  materialized views.
- `base/BaseTableEntity.cs` — adds `CreatedTimestamp` / `ModifiedTimestamp`. Stamped
  automatically by the `SaveChangesAsync()` override (which calls `BaseSaveChangesAsync()`), and
  also given a `now()` DB default. Tables get a `row_version` concurrency token via
  `BaseEntityConfigure()`.
- `user/BaseEntityWithUser.cs` (+ `user/IEntityWithUser.cs`) — generic over `TUser`, adds `UserId` /
  `User`. The `UserId` FK is **NOT NULL** (enforced when configured via `IsManyWithOneUser` /
  `IsOneWithOneUser`). It is filled by
  `UserDbContextExtensions.BaseWithUserEntitySaveChangesAsync` on insert when an authenticated user
  is present; background inserts without an authenticated user get `UserId == 0` and fail with an FK
  violation.
- `base/BaseLookupWithUser.cs` — `BaseEntityWithUser<TUser>` + `IBaseLookupEntity`.

**The portal's two closing types.** C# can't infer `TUser` from a constraint, so the portal closes the
two user-scoped bases over its own `User` and every entity declaration names the shim, not the
generic:
- `domain/model/entity/user/BaseEntityWithUser.cs` → `BaseEntityWithUser<User>`
- `domain/model/entity/base/core/BaseLookupWithUser.cs` → `BaseLookupWithUser<User>`

Keep them plain closing types — behaviour belongs in Framework. `domain/model/entityInterface/` holds
only two portal-specific markers (`IEntityWithIsDone`, `IEntityWithDoneAndTotalCount`); the
`IBase*Entity` family is Framework's.

# Entity Configuration

When writing EF Core entity configurations, always use the builder extension helpers — don't
hand-roll `ToTable` / `HasKey` / row_version / timestamps.

**Portal** — `AdhdTimeOrganizer/infrastructure/persistence/configuration/extensions/` — three files
survive here, each holding only what is tied to a portal type; everything general comes from Framework
(see below).
- `EntityWithUserBuilderExtensions.cs` — `IsManyWithOneUser<TEntity>(navigationProperty?,
  deleteBehavior = Cascade)` and `IsOneWithOneUser<TEntity>(…)`, and nothing else. They survive
  because they name the portal's concrete `User`.
- `EntityWithActivityBuilderExtensions.cs` — `IsManyWithOneActivity<TEntity>()` /
  `IsOneWithOneActivity<TEntity>()` for `BaseEntityWithActivity`.
- `TodoListEntityConfigurationExtensions.cs` — `BaseTodoListConfigure<TEntity>()` for
  `BaseTodoListItem`.

**Shared — `Sydowwe.Framework`, used by portal *and* module code alike.** The portal's own
`EntityBuilderExtensions.cs` and `PartitioningExtensions.cs` were deleted in the framework
reconciliation; portal configurations now `using
Sydowwe.Framework.infrastructure.persistence.configuration.extensions`.
- `configuration/extensions/EntityBuilderExtensions.cs` — `BaseEntityConfigure<TEntity>()` (call
  first: snake_case table name, serial `Id` PK, `row_version` concurrency token, `CreatedTimestamp` /
  `ModifiedTimestamp` defaults), `EnumColumn()` (enum as string), `FlagsEnumColumn()` (`[Flags]` enum
  as `int`) — both column helpers have nullable overloads — plus:
  - `PriceColumn(x => x.Prop, isRequired = true)` — `decimal(18,2)`.
  - `StoredComputedColumn(x => x.Prop, sql)` — Postgres `GENERATED ALWAYS AS (…) STORED`.
  - `EncryptedColumn(x => x.Prop)` — AES-256-GCM at-rest encryption (GDPR Art. 32) for
    high-sensitivity strings. Stores a versioned token in a `text` column; randomized, so the column
    **cannot** be filtered/sorted/uniqued — use only for fields read by row id. Key comes from the
    `FIELD_ENCRYPTION_KEY` env var (base64, 32 bytes; in `.env`, never the repo). See
    `Sydowwe.Framework/infrastructure/persistence/encryption/`. Currently unused by any entity here.

  ⚠ **Table-name gotcha in `BaseEntityConfigure`:** it derives the table name with
  `.Replace("Read", "")` on the *whole* class name, not a suffix strip. No entity in this solution
  contains `Read`, so nothing is affected today — but a future `ReadingLog` / `ThreadState` would
  silently map to `ing_log` / `th_state`. Give such an entity an explicit `ToTable(...)`.
- `configuration/extensions/NameTextColorEntityConfigurationExtension.cs` — the name/text/color/icon
  base-entity helpers: `BaseNameTextEntityConfigure`, `BaseTextColorEntityConfigure`,
  `BaseTextColorIconEntityConfigure`, `BaseNameTextColorEntityConfigure`,
  `BaseNameTextColorIconEntityConfigure`. Each is constrained to the matching `IBase…Entity` marker and
  calls `BaseEntityConfigure()` (directly or through the next helper down) for you. Note the **file
  name doesn't match the class prefix** — that is why a filename-based sweep for
  `EntityWithUserBuilderExtensions` missed these and once concluded the portal held the only copy.
- `infrastructure/persistence/PartitioningExtensions.cs` — note the different path, **not** under
  `configuration/extensions/`. `IsPartitionedByRange("Column", partitions)`. In use on
  `DesktopActivityEntry` and `WebExtensionActivityEntry`. Partition SQL is emitted by
  `PartitionedNpgsqlMigrationsSqlGenerator`, wired via
  `optionsBuilder.ReplaceService<IMigrationsSqlGenerator, …>()` in **both** `Program.cs` and
  `config/AppCommandDbContextFactory.cs` (the design-time factory). New partitioned tables need
  nothing beyond `IsPartitionedByRange`.

# DbContext Helpers

`Sydowwe.Framework/infrastructure/persistence/DbContextExtensions.cs` is the single copy — the
portal's own was deleted in the framework reconciliation, so portal and module code both use this
one. It exposes `DbContextHelper` — Result-returning CRUD helpers that wrap `SaveChanges` with
`DbUtils.HandleException`:
- `BaseSaveChangesAsync()` — stamps `CreatedTimestamp` / `ModifiedTimestamp` for `BaseTableEntity`
  entries. Called by the `SaveChangesAsync()` override and inside the helper methods — you do not
  need to call it manually.
- `AddEntityAsync`, `AddRangeAsync` (transactional, chunks of 300)
- `UpdateEntityAsync`, `UpdateRangeAsync`
- `DeleteEntityAsync`, `DeleteRangeAsync`, `DeleteByIdAsync`
- `SetActiveStatusAsync`, `SetActiveStatusRangeAsync` — for `ISoftDeletable` (`IsActive`).

# Seeding

One copy, in `Sydowwe.Framework/infrastructure/persistence/seeder/` — portal and module seeders both
use it. Pick the seeder kind by two questions: **who owns the rows**, and **is this production data
or a fixture**.

|                 | App-wide (no user owner)                            | Per-user                                                       |
|-----------------|-----------------------------------------------------|----------------------------------------------------------------|
| **Production**  | `IAppWideDefaultSeeder` — `Seed(bool overrideData)` | `IPerUserDefaultSeeder` — `SetupDefaults` / `ResetDefaults`     |
| **Dev fixture** | `IAppWideDevSeeder` — `Seed()` + `TruncateTable()`  | `IPerUserDevSeeder` — `SeedForUser(userId)` + `TruncateTable()` |

Set `SeederName` + `Order` (from `IDatabaseSeeder`, which is identity only) and add a lifetime marker —
the DI scan registers it and the matching manager picks it up. No manual registration.

- **Only dev seeders truncate.** Default seeders upsert: `overrideData` means "update existing rows in
  place", never "wipe and re-insert". Truncating `user_role` / `user` cascades away every user↔role
  assignment. Data that wants wipe-and-reinsert is a fixture — use a `…DevSeeder`. Truncation runs in
  reverse `Order`, so express FK dependencies once via `Order`.
- **Managers** (`interface/manager/`, one per cell): `IAppWideDefaultSeederManager`,
  `IPerUserDefaultSeederManager` (`SeedAllForUserAsync` is the sign-up path, via `UserDefaultsService`),
  `IAppWideDevSeederManager`, `IPerUserDevSeederManager` (`SeedAllForRootAdminAsync`). Default managers
  let exceptions propagate; dev managers log and continue. Both dev managers also expose
  `SeedAssembly…Async` (reseed one module) and `TruncateAllTablesAsync`.
- **Finding users:** never query users from a seeder or manager in Framework — use `ISeedUserProvider`
  (`GetAllUserIdsAsync` / `GetRootAdminUserIdAsync` / `GetSeedUserIdsAsync`). The portal implements it in
  `infrastructure/persistence/seeder/SeedUserIdProvider.cs`, alongside the Kernel's `ISeedUserIdProvider`.
- Entry point is `Program.SeedDatabase` — four ordered passes, **all still commented out**, so nothing
  seeds on startup today.

# Ledger Retention

Append-only ledgers (`scheduled_job_run`, `reminder_dispatch`, notification history, …) need a
retention purge or they grow forever — GDPR Art. 5(1)(e) / §13 zák. 18/2018.

Bind the **policy** from `Sydowwe.Framework/infrastructure/persistence/retention/RetentionOptions.cs`
(`Enabled`, `RetentionYears`, `KeepLastN` + `CutoffUtc()` / `CutoffOffset()`): subclass it per module
with a `SectionName` and `services.Configure<>` it — see
`AdhdTimeOrganizer.Scheduler/application/job/SchedulerRetentionOptions.cs` and
`AdhdTimeOrganizer.Reminders/application/job/ReminderRetentionOptions.cs`. Same shape everywhere;
values may differ.

Write the **query** as plain LINQ in the module's own purge handler — there is deliberately no
shared delete helper, because the FK guards that differ per ledger are the hard part and can't be
shared. Existing examples to copy:
`AdhdTimeOrganizer.Scheduler/application/job/PurgeExpiredRunLogsJobHandler.cs` (one pass),
`AdhdTimeOrganizer.Reminders/application/job/PurgeExpiredReminderLedgersJobHandler.cs` (three
ordered passes, two self-FKs), and
`AdhdTimeOrganizer.Notifications/application/job/PurgeExpiredNotificationHistoryJobHandler.cs`. The
shape is: age gate → keep-last-N floor (`Count(newer => …) >= keepLastN`) → FK guards → one
`ExecuteDeleteAsync`.

With `Restrict` FKs, delete in dependency order and exclude still-referenced rows, or the whole
batch aborts. `ExecuteDeleteAsync` bypasses the ChangeTracker (and therefore any interceptor) —
correct for `[NoAudit]` ledgers, wrong for entities you want audited.

# Auditing

**Status: available in `Sydowwe.Framework`, but NOT wired up in this solution.** The machinery
exists — `infrastructure/persistence/audit/AuditSaveChangesInterceptor.cs`, the `AuditLog` /
`BusinessAuditLog` entities, `IAuditService` (+ `AuditService`), `[NoAudit]` / `[AuditIgnore]` — and
some module entities already carry the attributes. But the interceptor is **not** registered on
`AppDbContext` (`Program.cs` only adds `SuggestionPatternRefreshInterceptor`), the audit entity
configurations live in an assembly `AppDbContext` never applies, and there is no `audit_log`
migration. Nothing is written today — don't tell yourself CRUD is being captured.

Turning it on needs all three: `options.AddInterceptors(…AuditSaveChangesInterceptor…)` in the
`AddDbContext` callback, the audit entity configurations applied to the model, and a migration.
`audit_log` is partitioned by `Date` (yearly RANGE, composite PK `(Id, Date)`) — governed by
`Sydowwe.Framework/infrastructure/persistence/configuration/AuditLogEntityConfiguration.cs`
(`FirstYear`, `YearCount`); `business_audit_log` is not partitioned.

Opt-outs, for when you do write auditable entities: `[NoAudit]` on a class skips the entity
entirely; `[AuditIgnore]` on a property keeps the entity audited but excludes that column from
snapshots and `ChangedProperties` (use for sensitive PII fields).

# Logging (no PII at the call site)

`Sydowwe.Framework/domain/helper/PiiRedactor.cs` exists but is **not** wired into this app's Serilog
pipeline (`AdhdTimeOrganizer/config/SerilogConfig.cs` does no redaction). So nothing is scrubbed
automatically — and even when wired, the redactor only matches **structured** PII it can recognize
by shape: emails, IBANs, Slovak birth numbers. Free-text PII — names, addresses, phone numbers —
cannot be regex-scrubbed and will leak. Log files survive GDPR erasure, so this is an Art. 5 /
Art. 32 leak.

Rule: **never put a person's name, address, phone, or email into a log message or its structured
arguments.** Log a stable non-PII identifier instead (entity id, `{UserId}`, correlation id). When
an email genuinely aids diagnostics, pass it through `PiiRedactor.MaskEmail` (`j***@domain`).
Logging entity *type* names (`typeof(T).Name`), file names, and ids is fine.

# FastEndpoints Base Classes

Before writing a custom endpoint, check whether one of the base classes in
`Sydowwe.Framework/application/endpoint/base/` already covers the pattern. Use them when they fit;
write a plain `Endpoint<TReq, TRes>` only when they don't.

**There is one copy, and portal endpoints use it too.** The portal's parallel set was deleted in the
framework reconciliation; `AdhdTimeOrganizer/application/endpoint/base/` now holds only
`ErrorLoggingPostProcessor` and `BaseActivityFormSelectOptionsEndpoint`. Anything in this repo that
still says "portal bases vs module bases" is out of date.

Convention: `<Verb><Entity>Endpoint`
- `GetSelectOptions<Entity>Endpoint`
- `GetById<Entity>Endpoint`
- `GetAll<Entity>Endpoint`
- `GetBy<FieldName><Entity>Endpoint`
- `Update<Entity>Endpoint`
- `Create<Entity>Endpoint`
- `Delete<Entity>Endpoint`
- `BatchDelete<Entity>Endpoint`
- `Grid<Entity>Endpoint` — paginated filter+sort table view (BaseGridEndpoint)
- `FilterSort<Entity>Endpoint` — filter+sort without pagination (BaseFilterSortEndpoint)
- `Filter<Entity>Endpoint` — filter only (BaseFilterEndpoint)
- `Sort<Entity>Endpoint` — sort only (BaseSortEndpoint)

**Mapping is on the DTOs, not a `TMapper` generic** (Mapperly was removed). Writes map via the
request: `TRequest : ICreateRequest<TEntity>` exposes `TEntity ToEntity`;
`TRequest : IUpdateRequest<TEntity>` exposes `UpdateEntity(entity)`; patch implements
`Mapping(entity, req)`. Reads project in the DB via a static-abstract on the response:
`TResponse : IIdResponse, IProjectionResponse<TResponse, TEntity>` implements
`static IQueryable<TResponse> Projection(IQueryable<TEntity>)`. All reads are `AsNoTracking`.

**Commands** (`endpoint/base/command/`)
| Class | HTTP | Use when |
|---|---|---|
| `BaseCreateEndpoint<TEntity, TRequest>` | POST | Standard create — `req.ToEntity`, saves, returns new `Id` (201). Hooks: `BeforeMapping`/`AfterMapping`/`AfterSave` |
| `BaseUpdateEndpoint<TEntity, TRequest>` | PUT `/{id}` | Standard full update — `req.UpdateEntity(entity)` (transactional). Hooks: `BeforeMapping`/`UpdateEntityAsync`/`AfterMapping`/`AfterSave` |
| `BasePatchEndpoint<TEntity, TRequest, TResponse>` | PATCH `/{id}` | Partial update — implement `Mapping(entity, req)` |
| `BaseDeleteEndpoint<TEntity>` | DELETE `/{id}` | Single entity hard delete by id |
| `BaseSoftDeleteEndpoint<TEntity>` | DELETE `/{id}` | Soft delete (`ISoftDeletable.IsActive = false`) |
| `BaseBatchDeleteEndpoint<TEntity>` | POST `/batch-delete` | Delete multiple entities by id list |
| `BaseToggleIsHiddenEndpoint<TEntity>` | PATCH `/toggle-is-hidden` | Toggle `IsHidden` on entities implementing `IEntityWithIsHidden` |

**Reads** (`endpoint/base/read/`)
| Class | HTTP | Use when |
|---|---|---|
| `BaseGetAllEndpoint<TEntity, TResponse>` | GET | Return all records. Override `Filter()` / `Sort()` |
| `BaseGetByIdEndpoint<TEntity, TResponse>` | GET `/{id}` | Return single record by id. Hooks: `AuthorizeAsync` / `PostProcess` |
| `BaseGetByFieldEndpoint<TEntity, TResponse>` | GET | Single record by a non-id field — implement `FieldName` + `FilterByField` |
| `BaseGetAllByParentEndpoint<TEntity, TResponse>` | GET | Children of a parent — implement `ParentName` + `FilterByParent`; hook `AuthorizeAsync(parentId)` |
| `BaseGetSelectOptionsEndpoint<TEntity>` | GET `/all-options` | `id + text` select options — implement `Map(query)` |
| `BaseFilterEndpoint<TEntity, TResponse, TFilter>` | POST `/filter` | List filtered by a custom `IFilterRequest` — implement `ApplyCustomFiltering` |
| `BaseSortEndpoint<TEntity, TResponse>` | POST `/sort` | List with dynamic sort columns |
| `BaseFilterSortEndpoint<TEntity, TResponse, TFilter>` | POST `/filter-sort` | Filter + sort without pagination — implement `ApplyCustomFiltering` |
| `BaseGridEndpoint<TEntity, TResponse, TFilter>` | POST `/filtered-table` | Filter + sort + paginate — implement `ApplyCustomFiltering` |

**Auth** (`endpoint/user/command/auth/` + `endpoint/user/read/`) — the auth flow has bases too, and
they are easy to miss because they don't follow the `<Verb><Entity>` convention. Check here before
writing a standalone auth endpoint. Those generic over `TUser` are closed on the portal's `User`.

| Class | Portal subclass |
|---|---|
| `BaseLoginEndpoint<TUser>` | `LoginUserEndpoint` |
| `BaseRegisterUserEndpoint<TUser, TRequest>` | `RegisterUserEndpoint` — empty; hook `AfterUserCreatedAsync` (runs inside the transaction) |
| `BaseDeleteUserAccountEndpoint<TUser>` | `DeleteUserAccountEndpoint` — empty; hooks `BeforeDeleteAsync` / `AfterDeleteAsync` |
| `BaseLogoutEndpoint` (non-generic) | `LogoutEndpoint` — empty; route/auth all from the base |
| `BaseRefreshTokenEndpoint` (non-generic) | `RefreshTokenEndpoint` — empty; route/throttle from the base |
| `BaseChangePasswordEndpoint<TUser>` | `ChangePasswordEndpoint`; hook `AfterPasswordChangedAsync` |
| `BaseValidateTwoFactorAuthForLoginEndpoint<TUser>` | `ValidateTwoFactorAuthForLoginWebEndpoint` + `…ExtensionEndpoint` |
| `BaseSetupTwoFactorForLoginEndpoint<TUser>` | `SetupTwoFactorForLoginEndpoint` — empty; web only (reads the partial-auth *cookie*, so the extension flow has no equivalent) |
| `BaseGetCurrentUserEndpoint<TUser>` | `GetUserDataEndpoint` — empty; route is **GET** `/user/data` |
| `BaseUserRoleGetAllSelectOptionsEndpoint` | **none** — the portal exposes no role-options route |
| `BaseLogoutAllEndpoint` (non-generic) | `LogoutAllEndpoint` — empty |
| `BaseRevokeSessionEndpoint` (non-generic) | `RevokeSessionEndpoint` — empty; 404 not-found / 400 current-session are load-bearing |
| `BaseRevokeAllOtherSessionsEndpoint` (non-generic) | `RevokeAllOtherSessionsEndpoint` — empty |
| `BaseGetUserSessionsEndpoint` (non-generic) | `GetUserSessionsEndpoint` — empty |
| `BaseUpdateUserPreferencesEndpoint<TUser, TRequest>` | `UpdateUserPreferencesEndpoint`; hook `ApplyExtraPreferences`, and override `Configure` to attach the validator |
| `BaseForgotPasswordEndpoint<TUser>` | `ForgotPasswordEndpoint` — empty; hook `BuildResetLink` |
| `BaseResetPasswordEndpoint<TUser>` | `ResetPasswordEndpoint` — empty |
| `BaseGetTwoFactorAuthStatusEndpoint<TUser>` | `GetTwoFactorAuthStatusEndpoint` — empty |
| `BaseToggleTwoFactorAuthEndpoint<TUser>` | `ToggleTwoFactorAuthEndpoint` — empty |
| `BaseResetTwoFactorAuthEndpoint<TUser>` | `ResetTwoFactorAuthEndpoint` — empty |
| `BaseRegenerateRecoveryCodesEndpoint<TUser>` | `RegenerateRecoveryCodesEndpoint` — empty |
| `BaseConfirmEmailEndpoint<TUser>` | `ConfirmEmailEndpoint` — empty |
| `BaseResendConfirmationEmailEndpoint<TUser>` | `ResendConfirmationEmailEndpoint` — empty (file `ResendEmailConfirmationEndpoint.cs`) |
| `BaseChangeEmailEndpoint<TUser>` | `ChangeEmailEndpoint` — empty |
| `BaseConfirmEmailChangeEndpoint<TUser>` | `ConfirmEmailChangeEndpoint` — empty |
| `BaseExtensionLoginEndpoint<TUser>` | `ExtensionLoginEndpoint`; hook `HasExtensionAccess` |
| `BaseExtensionLogoutEndpoint` (non-generic) | `ExtensionLogoutEndpoint` — empty |
| `BaseExtensionRefreshTokenEndpoint` (non-generic) | `ExtensionRefreshTokenEndpoint` — empty |

Every password login transport shares one decision — `PasswordSignInFlow.RunAsync`
(`Sydowwe.Framework/application/service/auth/`). Call it; never re-implement the branch.

Its sign-up counterpart is `UserRegistrationFlow.RunAsync` (same folder): Identity insert → `User`
role → optional in-transaction step → `IUserDefaultsService.CreateDefaultsAsync` → commit, with
`UserRegistrationResult.StatusCode` carrying the 409/400/500 mapping. Both sign-up methods use it —
`BaseRegisterUserEndpoint` (password, passing the 2FA provisioning as the in-transaction step) and
the portal's `GoogleSignInEndpoint` (federated, no password). A new provider calls it too; do not
re-implement the create-user branch, and do not add logging to it (it sees email + password). Every
failure exit rolls back explicitly through the local `Fail(...)` rather than leaning on the implicit
rollback that disposing an uncommitted transaction performs — if you add a branch, roll back in it too.

⚠ `GetUserDataEndpoint` is **GET** `/user/data`, from the base. It used to override `Configure` to serve
POST because the SPA (separate repo) called it that way; the override was dropped in the endpoint
migration and the SPA was updated to match. It is a `Configure`-less wrapper now — don't "restore" the
POST verb.

The four session endpoints touch no user *object*, only `User.GetId()`, so they are non-generic. Their
`UserSessionResponse` DTO (`Sydowwe.Framework/application/dto/response/user/`) and the
`UserAgentParser` they use (`Sydowwe.Framework/domain/helper/`) live in Framework too — the portal
copies are gone. The two revoke endpoints sit under Framework's `command/auth/` even though their
portal subclasses live in `command/settings/`, matching how `BaseChangePasswordEndpoint` already
splits.

**Google sign-in is portal-only, deliberately.** `GoogleSignInEndpoint`, `IGoogleSignInService` /
`GoogleSignInService`, and the `GoogleSignIn*` DTOs all stay in `AdhdTimeOrganizer`. It was moved to
Framework once and reverted: a *usable* provider has to ship the implementation, which puts
`Google.Apis.Auth` (+ `Newtonsoft.Json`) in `Sydowwe.Framework.csproj` for every solution, enabled or
not. Don't re-attempt it as part of a sweep — see `migration/stays-portal.md` for the measurements.
If a second federated provider ever appears, the shape is a separate `Sydowwe.Framework.GoogleAuth`
project, not a package reference on the core. Google **Calendar** is unrelated and also stays portal.

⚠ `BaseLogoutEndpoint` sets `AllowAnonymous()` **deliberately** — logout authenticates nothing, it
acts on whatever refresh token the cookie carries. Requiring a token 401s a caller whose access token
already expired, so the refresh token is never revoked and the cookies stay set. Don't "tighten" it;
`AuthFunctionalTests.Logout_RevokesRefreshToken_WhenAccessTokenIsExpired` pins this.

⚠ Framework's endpoint assembly is **excluded from FastEndpoints discovery** (`o.Assemblies` in
`Program.cs`, with `DisableAutoDiscovery = true`), so every endpoint there must be `abstract` — a
concrete one would never be routed. Don't widen `o.Assemblies` to "reuse" one; subclass it instead.

Override `AllowedRoles()` on any base endpoint. **Default is User + Admin + Root** — every account in
this app is a plain `User`, so an admin-only default made the endpoints unreachable. Narrow to
`GetAdminRole()` / `GetAdminOrHigherRoles()` on genuine admin surface.
Role names live in one place — `UserRoleEnum` (User · Admin · Root) with the cumulative groups
`UserRoles.UserOrHigher` / `UserRoles.AdminOrHigher` in
`Sydowwe.Framework/domain/helper/EndpointExtensions.cs`. The bases default to
`IEndpoint.GetUserRole()` (`= UserRoles.UserOrHigher`); `IEndpoint.GetAdminRole()` is the
admin-or-higher counterpart. `AdhdTimeOrganizer/application/helper/PortalEndpointHelper.cs`
re-exports the same two arrays as `GetUserOrHigherRoles()` / `GetAdminOrHigherRoles()` and adds
`HttpContext.GetVerifiedUser()` closed over the portal `User` — it is a convenience wrapper, not a
second source of truth. It is named `Portal…` so it no longer collides with Framework's own
`EndpointHelper` (`Sydowwe.Framework/domain/helper/`, the result-error → HTTP status map), which is a
completely different helper. Never hard-code role strings.

**User scoping — the role gate is not what keeps other users' rows out, and neither are the base
endpoints.** Since there is now one shared set of bases, `ApplyUserScoping` on
Grid/Filter/Sort/FilterSort is a **no-op virtual for portal and module code alike** — the portal's old
auto-scoping `FilteredByUser => true` override went with its deleted copies. What actually scopes:

- **Portal reads are saved by the DbContext, not the endpoint.** `AppDbContext.OnModelCreating`
  applies a global query filter to every `IEntityWithUser`
  (`ApplyUserQueryFilters`: `!IsAuthenticated || e.UserId == currentUserId`), so portal reads over
  those entities are scoped no matter which role or endpoint reaches them.
  - `WebExtensionActivityEntry` is **excluded** from that call and carries its own filter combining
    the same user check with `RecordDate >= CurrentPartitionDate`.
  - Entities that are *not* `IEntityWithUser` (`Activity*Profile`) get **no** filter — scope them
    inside `ApplyCustomFiltering`, as the three profile grids do with `p.Activity.UserId == userId`.
- **Module reads have no safety net at all:** no global filter on their entities and the same no-op
  `ApplyUserScoping`. A module read over per-user rows must override `ApplyUserScoping`, or it returns
  every user's data to any signed-in user. Where a module endpoint deliberately leaves the no-op in
  place (the Scheduler job registry, the Reminders grid) it says so in a comment — follow that habit.
- The other reads (`GetAll`, `GetById`, `GetByField`, `GetAllByParent`, `GetSelectOptions`) don't
  scope either — use their `Filter()` / `AuthorizeAsync()` hooks.
- `FilteredByUser(userId)` still exists as an explicit `IQueryable` extension
  (`Sydowwe.Framework/infrastructure/persistence/QueryableEntityExtensions.cs`) and is called by hand
  in ~8 portal endpoints. Nothing calls it for you.

# Auth Plumbing Outside the Endpoints

Everything below moved to `Sydowwe.Framework` alongside the endpoint migration. Same rule as the
endpoints: the *mechanism* is Framework's, anything naming a product decision stays in the portal.

**Token claim names — `Sydowwe.Framework/domain/helper/AuthClaims.cs`.** `AuthMethod` (`auth_method`),
`ClientType` (`client_type`), `ExtensionClientType` (`"Extension"`). `JwtService` writes them and the
authorization handlers/policies read them; both sides reference these constants. Never re-type the
literals — a typo does not fail the build, it silently changes who is allowed in.

**Extension-client gate — `Sydowwe.Framework/infrastructure/security/ExtensionClientAuthorization.cs`.**
`ExtensionClientRequirement`, `ExtensionClientAuthorizationHandler`, `[AllowExtensionClients]`, and the
policy names `DenyExtensionClients` / `WebOnly` / `ExtensionOnly` on `ExtensionClientPolicies`. Deny by
default: the endpoint configurator in `Program.cs` attaches `DenyExtensionClients` to every endpoint
*without* `[AllowExtensionClients]`. Don't switch this to `AuthorizationOptions.FallbackPolicy` — the
configurator gives every endpoint role metadata, and an endpoint carrying any authorization metadata
never falls back, which is why the deny is attached per endpoint. (The old file was named
`DenyExtensionClientsByDefaultPreProcessor.cs` and contained no pre-processor; it is gone.)

**What stayed portal, deliberately:**
- `infrastructure/security/PortalAuthorizationPolicies.cs` — `ActivityTracking`, the policy gating the
  tracking endpoints. Which clients may report activity is a product decision.
- `infrastructure/extService/user/auth/ExtensionRoleClaimsProvider.cs` — grants the `ActivityTracking`
  *role* to extension tokens via Framework's `IAdditionalUserClaimsProvider<TUser>` seam. It exists so
  Framework never learns this deployment's role names; moving it would invert that.
- Note the policy name and the role name are **two different constants that happen to share the string**
  `"ActivityTracking"`. Renaming one does not rename the other. The `AutoTagOverride("ActivityTracking")`
  in `endpointGroups/` is a third, unrelated use — a Swagger tag. Leave it a literal.

**Refresh-token cleanup — `Sydowwe.Framework/infrastructure/extService/user/auth/RefreshTokenCleanupService.cs`.**
`BackgroundService` next to the `RefreshTokenService` it drives; hosts register it with
`AddHostedService<RefreshTokenCleanupService>()`. First sweep runs at **startup**, then every `Interval`
(`protected virtual`, 24h) — a host restarting more often than the interval would otherwise never clean
up at all. Logs counts only, never a token owner.

# Email Templates

`Sydowwe.Framework/infrastructure/templates/email/` — `ConfirmEmail.html`, `ResetPassword.html`,
`ResetPasswordCode.html`, consumed by `UserEmailSenderService`. They are **`<EmbeddedResource>`** in
`Sydowwe.Framework.csproj` (`infrastructure\templates\email\*.html`), read via
`Assembly.GetManifestResourceStream`, so there is no copy-to-output step and no working-directory
assumption. A new template just needs to land in that folder — the glob picks it up.

A host overrides any single template by placing a file of the same name in
`{AppContext.BaseDirectory}/templates/email/`; it is checked first, and anything absent falls back
per-file to the embedded copy, so overriding one does not mean re-supplying the rest.

⚠ Do **not** go back to loading these from disk. The previous arrangement kept them in the portal as
`<Resource Include=… CopyToOutputDirectory="Always">` — `Resource` is a WPF item type the .NET SDK
ignores, so they never reached the output — and read them from
`Directory.GetCurrentDirectory()/templates/email`, a path that did not exist in any environment. Every
mail this service sends threw `FileNotFoundException`; on sign-up that surfaced as a 500 *after* the
account was already committed. Nothing in the test suite covers mail rendering, so it went unnoticed.

# DTO Conventions

- **Time-of-day values** in portal requests and responses use `TimeDto`
  (`AdhdTimeOrganizer/application/dto/dto/TimeDto.cs`) instead of `TimeOnly`. Call `.ToTimeOnly()`
  when assigning to an entity. Validated by `application/validator/TimeDtoValidator.cs`.
- **Module** (`Sydowwe.Framework`-based) DTOs use `MyIntTime`
  (`Sydowwe.Framework/domain/helper/MyIntTime.cs`) — `Hours` / `Minutes` / `Seconds`, serialized as
  those three fields, persisted as an `int` count of seconds via `MyIntTimeConverter`
  (`Sydowwe.Framework/infrastructure/persistence/converter/`). Use `new MyIntTime(seconds)` /
  `.GetInSeconds()` to convert. Don't introduce it into portal DTOs.

# Testing

Read `Sydowwe.Framework.Testing/docs/testing.md` for the full guide (**not** the root
`docs/testing.md`, which is a foreign copy). Quick reference:

- Tests run the real portal `Program` against a Postgres container (`Testcontainers.PostgreSql`),
  with auth and a couple of singletons swapped. xunit v3 + FluentAssertions + Moq + Respawn.
- Shared infrastructure lives in `Sydowwe.Framework.Testing`: one fixture
  (`PostgresContainerFixture<TProgram, TDbContext>`), one base class (`PostgresTestBase`), one auth
  handler (`RoleTestAuthHandler`, scheme `"Test"`), one factory
  (`TestWebApplicationFactory<TProgram>`). The handler and factory are role-parametrized — do not
  add per-role subclasses.
- This portal closes the fixture in
  `AdhdTimeOrganizer.IntegrationTests/Infrastructure/AppDbContextFixture.cs`; tests are tagged
  `[Collection("Postgres")]`. Its `OnSchemaCreatedAsync` applies
  `AdhdTimeOrganizer/infrastructure/persistence/sqlScripts/*.sql` (the three suggestion-pattern
  materialized views), copied next to the test binaries by a `Content` item in the test csproj. They
  are hand-written SQL, not migration output, so `EnsureCreated` skips them — and without them
  `SuggestionPatternRefreshInterceptor` fails with 42P01 on any save touching `PlannerTask`,
  `ActivityHistory` or `Calendar`. Add new scripts to that folder and they are picked up.
- Test bases get HTTP clients via `CreateClient()` (Admin+User), `CreateAdminRoleClient()`,
  `CreateUserRoleClient()`, `CreateRootRoleClient()`, `CreateUnauthenticatedClient()`. For different
  test users, `CreateFactory(roles, userId)` — caller disposes. `CreateDbContext()` for
  seeding/asserting outside HTTP; override `SeedAsync(db)`.
- For each FastEndpoints base in `endpoint/base/` there is a matching abstract test base in
  `Sydowwe.Framework.Testing/baseTests/` (`BaseGetByIdEndpointTests`, `BaseGridEndpointTests`,
  `BaseCreateEndpointTests`, `BaseUpdateEndpointTests`, `BaseDeleteEndpointTests`,
  `BasePatchEndpointTests`, `BaseGetAllEndpointTests`, `BaseGetSelectOptionsEndpointTests`,
  `BaseFilterEndpointTests`, `BaseSortEndpointTests`, `BaseFilterSortEndpointTests`,
  `BaseBatchDeleteEndpointTests`, `BaseToggleIsHiddenEndpointTests`). Use them — they ship the auth
  matrix + 404 paths. Add endpoint-specific scenarios (validation, business rules, IDOR) in the
  concrete subclass.
