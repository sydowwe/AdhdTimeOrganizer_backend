# Endpoints, DTOs & User Scoping

Before writing a custom endpoint, check whether one of the base classes in
`framework/Sydowwe.Framework/application/endpoint/base/` already covers the pattern. Use them when
they fit; write a plain `Endpoint<TReq, TRes>` only when they don't.

**There is one copy, and portal endpoints use it too.** The portal's parallel set was deleted in the
framework reconciliation; `AdhdTimeOrganizer/application/endpoint/base/` now holds only
`ErrorLoggingPostProcessor` and `BaseActivityFormSelectOptionsEndpoint`. Anything in this repo that
still says "portal bases vs module bases" is out of date.

Convention: `<Verb><Entity>Endpoint` — `GetSelectOptions<Entity>Endpoint`, `GetById<Entity>Endpoint`,
`GetAll<Entity>Endpoint`, `GetBy<FieldName><Entity>Endpoint`, `Update<Entity>Endpoint`,
`Create<Entity>Endpoint`, `Delete<Entity>Endpoint`, `BatchDelete<Entity>Endpoint`,
`Grid<Entity>Endpoint` (paginated filter+sort table view), `FilterSort<Entity>Endpoint`,
`Filter<Entity>Endpoint`, `Sort<Entity>Endpoint`.

**Mapping is on the DTOs, not a `TMapper` generic** (Mapperly was removed). Writes map via the
request: `TRequest : ICreateRequest<TEntity>` exposes `TEntity ToEntity`;
`TRequest : IUpdateRequest<TEntity>` exposes `UpdateEntity(entity)`; patch implements
`Mapping(entity, req)`. Reads project in the DB via a static-abstract on the response:
`TResponse : IIdResponse, IProjectionResponse<TResponse, TEntity>` implements
`static IQueryable<TResponse> Projection(IQueryable<TEntity>)`. All reads are `AsNoTracking`.

## Commands (`endpoint/base/command/`)

| Class | HTTP | Use when |
|---|---|---|
| `BaseCreateEndpoint<TEntity, TRequest>` | POST | Standard create — `req.ToEntity`, saves, returns new `Id` (201). Hooks: `BeforeMapping`/`AfterMapping`/`AfterSave` |
| `BaseUpdateEndpoint<TEntity, TRequest>` | PUT `/{id}` | Standard full update — `req.UpdateEntity(entity)` (transactional). Hooks: `BeforeMapping`/`UpdateEntityAsync`/`AfterMapping`/`AfterSave` |
| `BasePatchEndpoint<TEntity, TRequest, TResponse>` | PATCH `/{id}` | Partial update — implement `Mapping(entity, req)`. Hook: `AfterSave` |
| `BaseDeleteEndpoint<TEntity>` | DELETE `/{id}` | Single entity hard delete by id. Hooks: `BeforeDeleteAsync` (read what the delete/cascade is about to destroy) / `AfterSave` |
| `BaseSoftDeleteEndpoint<TEntity>` | DELETE `/{id}` | Soft delete (`ISoftDeletable.IsActive = false`) |
| `BaseBatchDeleteEndpoint<TEntity>` | POST `/batch-delete` | Delete multiple entities by id list. Hooks: `BeforeDeleteAsync` / `AfterSave`, both taking the whole batch |
| `BaseToggleIsHiddenEndpoint<TEntity>` | PATCH `/toggle-is-hidden` | Toggle `IsHidden` on entities implementing `IEntityWithIsHidden` |

## Reads (`endpoint/base/read/`)

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

## Auth endpoints

The auth flow has bases too, and they are easy to miss because they don't follow the `<Verb><Entity>`
convention — `endpoint/user/command/auth/` + `endpoint/user/read/`. Check here before writing a
standalone auth endpoint. Those generic over `TUser` are closed on the portal's `User`.

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
(`framework/Sydowwe.Framework/application/service/auth/`). Call it; never re-implement the branch.

Its sign-up counterpart is `UserRegistrationFlow.RunAsync` (same folder): Identity insert → `User`
role → optional in-transaction step → `IUserDefaultsService.CreateDefaultsAsync` → commit, with
`UserRegistrationResult.StatusCode` carrying the 409/400/500 mapping. Both sign-up methods use it —
`BaseRegisterUserEndpoint` (password, passing the 2FA provisioning as the in-transaction step) and the
portal's `GoogleSignInEndpoint` (federated, no password). A new provider calls it too; do not
re-implement the create-user branch, and do not add logging to it (it sees email + password). Every
failure exit rolls back explicitly through the local `Fail(...)` rather than leaning on the implicit
rollback that disposing an uncommitted transaction performs — if you add a branch, roll back in it too.

⚠ `GetUserDataEndpoint` is **GET** `/user/data`, from the base. It used to override `Configure` to
serve POST because the SPA (separate repo) called it that way; the override was dropped and the SPA
was updated to match. It is a `Configure`-less wrapper now — don't "restore" the POST verb.

The four session endpoints touch no user *object*, only `User.GetId()`, so they are non-generic. Their
`UserSessionResponse` DTO (`framework/Sydowwe.Framework/application/dto/response/user/`) and the
`UserAgentParser` they use (`framework/Sydowwe.Framework/domain/helper/`) live in Framework too. The
two revoke endpoints sit under Framework's `command/auth/` even though their portal subclasses live in
`command/settings/`, matching how `BaseChangePasswordEndpoint` already splits.

**Google sign-in is portal-only, deliberately.** `GoogleSignInEndpoint`, `IGoogleSignInService` /
`GoogleSignInService`, and the `GoogleSignIn*` DTOs all stay in `AdhdTimeOrganizer`. It was moved to
Framework once and reverted: a *usable* provider has to ship the implementation, which puts
`Google.Apis.Auth` (+ `Newtonsoft.Json`) in `Sydowwe.Framework.csproj` for every solution, enabled or
not. Don't re-attempt it as part of a sweep — see `migration/stays-portal.md`. If a second federated
provider ever appears, the shape is a separate `Sydowwe.Framework.GoogleAuth` project, not a package
reference on the core. Google **Calendar** is unrelated and also stays portal.

⚠ `BaseLogoutEndpoint` sets `AllowAnonymous()` **deliberately** — logout authenticates nothing, it acts
on whatever refresh token the cookie carries. Requiring a token 401s a caller whose access token
already expired, so the refresh token is never revoked and the cookies stay set. Don't "tighten" it;
`AuthFunctionalTests.Logout_RevokesRefreshToken_WhenAccessTokenIsExpired` pins this.

⚠ Framework's endpoint assembly is **excluded from FastEndpoints discovery** (`o.Assemblies` in
`Program.cs`, with `DisableAutoDiscovery = true`), so every endpoint there must be `abstract` — a
concrete one would never be routed. Don't widen `o.Assemblies` to "reuse" one; subclass it instead.

## Roles

Override `AllowedRoles()` on any base endpoint. **Default is User + Admin + Root** — every account in
this app is a plain `User`, so an admin-only default made the endpoints unreachable. Narrow to
`GetAdminRole()` / `GetAdminOrHigherRoles()` on genuine admin surface.

Role names live in one place — `UserRoleEnum` (User · Admin · Root) with the cumulative groups
`UserRoles.UserOrHigher` / `UserRoles.AdminOrHigher` in
`framework/Sydowwe.Framework/domain/helper/EndpointExtensions.cs`. The bases default to
`IEndpoint.GetUserRole()` (`= UserRoles.UserOrHigher`); `IEndpoint.GetAdminRole()` is the
admin-or-higher counterpart. There is no portal-side `PortalEndpointHelper` wrapper — call
`IEndpoint.GetUserRole()` / `GetAdminRole()` directly. Never hard-code role strings.

## User scoping — the role gate is not what keeps other users' rows out

And neither are the base endpoints. Since there is one shared set of bases, `ApplyUserScoping` on
Grid/Filter/Sort/FilterSort is a **no-op virtual for portal and module code alike**. What actually
scopes:

- **Portal reads are saved by the DbContext, not the endpoint.** `AppDbContext.OnModelCreating`
  applies a global query filter to every `IEntityWithUser` (`ApplyUserQueryFilters`:
  `ScopeUserId == null || e.UserId == ScopeUserId`), so portal reads over those entities are scoped no
  matter which role or endpoint reaches them.
  - `WebExtensionActivityEntry` is **excluded** from that call and carries its own filter combining the
    same user check with `RecordDate >= CurrentPartitionDate`.
  - Entities that are *not* `IEntityWithUser` (`Activity*Profile`) get **no** filter — scope them
    inside `ApplyCustomFiltering`, as the three profile grids do with `p.Activity.UserId == userId`.
- **Module reads have no safety net at all:** no global filter on their entities and the same no-op
  `ApplyUserScoping`. A module read over per-user rows must override `ApplyUserScoping`, or it returns
  every user's data to any signed-in user. Where a module endpoint deliberately leaves the no-op in
  place (the Scheduler job registry, the Reminders grid) it says so in a comment — follow that habit.
- The other reads (`GetAll`, `GetById`, `GetByField`, `GetAllByParent`, `GetSelectOptions`) don't scope
  either — use their `Filter()` / `AuthorizeAsync()` hooks.
- `FilteredByUser(userId)` still exists as an explicit `IQueryable` extension
  (`framework/Sydowwe.Framework/infrastructure/persistence/QueryableEntityExtensions.cs`) and is called
  by hand in ~8 portal endpoints. Nothing calls it for you.

See also the query-filter caching trap in `docs/claude/persistence.md`.

## DTO conventions

- **Time-of-day values** in portal requests and responses use `TimeDto`
  (`AdhdTimeOrganizer/application/dto/dto/TimeDto.cs`) instead of `TimeOnly`. Call `.ToTimeOnly()` when
  assigning to an entity. Validated by `application/validator/TimeDtoValidator.cs`.
- **Module** (`Sydowwe.Framework`-based) DTOs use `MyIntTime`
  (`framework/Sydowwe.Framework/domain/helper/MyIntTime.cs`) — `Hours` / `Minutes` / `Seconds`,
  serialized as those three fields, persisted as an `int` count of seconds via `MyIntTimeConverter`
  (`framework/Sydowwe.Framework/infrastructure/persistence/converter/`). Use `new MyIntTime(seconds)` /
  `.GetInSeconds()` to convert. Don't introduce it into portal DTOs.
