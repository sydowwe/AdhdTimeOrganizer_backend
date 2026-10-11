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
| `BasePatchEndpoint<TEntity, TRequest, TResponse>` | PATCH `/{id}` | Partial update — implement `Mapping(entity, req)`. Hooks: `AuthorizeAsync` (403 on the routed row) / `BeforeMapping` (verify caller-supplied FK ids, 404) / `AfterMapping` (derive from what `Mapping` wrote) / `AfterSave`. Never override `HandleAsync` — the exception mapping and the cancellation guard live there |
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
| `BaseFilterSortEndpoint<TEntity, TResponse, TFilter>` | POST `/filter-sort` | Filter + sort without pagination — implement `ApplyCustomFiltering`; hooks `ApplyUserScoping`, `PostProcessItems(Async)` (runs after sorting, so an appended row lands last) |
| `BaseGridEndpoint<TEntity, TResponse, TFilter>` | POST `/filtered-table` | Filter + sort + paginate — implement `ApplyCustomFiltering` |

### Sort keys — what is sortable is the **response**, not the entity

The sorting bases order the *projected* response, so a client key has to resolve to a member of the DTO,
not of the model. A key may be a dotted path (`activity.name`, `activity.role.name`), pascalized segment
by segment by `QueryableEntityExtensions.SortByMany`.

- **An unresolvable key is an `InvalidSortKeyException`**, which `InvalidSortKeyMiddleware` answers with a
  **400 whose `errors.sortBy` names the key** — never a 500, and never a quietly unsorted page (that
  returns a plausible page in the default order, and on a paginated grid means rows the caller never
  sees). The middleware is registered in `Program.cs` right inside `UseUnhandledExceptionHandler`; a host
  that omits it falls back to the opaque 500.
- **Don't swallow it.** An endpoint that wraps its handler in a `catch (Exception)` must rethrow this one
  type — the two tracking distinct-entry tables do — or it reports a client's typo as a server fault.
- **A key a shipped client sends is remapped, not left to fail.** A 400 is correct and still useless to the
  person looking at the screen — a table renders its empty state for a failed request, so the error and a
  genuinely empty list look identical. Override `PreprocessSortBy` with
  `SortKeyExtensions.ExpandSortKey` / `RenameSortKey`
  (`framework/Sydowwe.Framework/application/extensions/`): `GridMemoryAnchorEndpoint` expands one
  `periodKey` column into `AnchorYear, AnchorMonth`, and the three reminder-definition reads accept
  `nextOccurrence` as an alias for `nextOccurrenceAt` through one shared `ReminderDashboardSortKeys`.
  An alias is compatibility for a client that already shipped — say which one, so it can be dropped.
- **Better than either, where it fits: give the response the shape the client already asks for.** Nesting
  the whole `ActivityResponse` made `activity.name` resolve natively on four grids that had been remapping
  a flattened `activityName`, and made the activity's role and category sortable as a side effect.
- A field filled in by `PostProcessItems` sorts on its default value. If it must be sortable, compute it
  in the projection (`Projection` override + correlated subquery), as the profile and anchor grids do.

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
| `BaseGetCurrentUserEndpoint<TUser, TResponse>` | `GetCurrentUserEndpoint` → `AppUserDataResponse`; hook `Enrich` adds `firstDayOfWeek`, `weatherLocation` and FO10's `defaultWakeUpTime` / `defaultBedTime` (`TimeDto`). Route is **GET** `/user/data`. The one-parameter `BaseGetCurrentUserEndpoint<TUser>` is for hosts with no columns of their own — deriving from it here would silently stop those fields reaching the client |
| `BaseUserRoleGetAllSelectOptionsEndpoint` | **none** — the portal exposes no role-options route |
| `BaseLogoutAllEndpoint` (non-generic) | `LogoutAllEndpoint` — empty |
| `BaseRevokeSessionEndpoint` (non-generic) | `RevokeSessionEndpoint` — empty; 404 not-found / 400 current-session are load-bearing |
| `BaseRevokeAllOtherSessionsEndpoint` (non-generic) | `RevokeAllOtherSessionsEndpoint` — empty |
| `BaseGetUserSessionsEndpoint` (non-generic) | `GetUserSessionsEndpoint` — empty |
| `BaseUpdateUserPreferencesEndpoint<TUser, TRequest>` | `UpdateUserPreferencesEndpoint`; hook `ApplyExtraPreferences`, and override `Configure` to attach the validator. Convention is "null leaves it unchanged", so a *clearable* string preference needs a second signal — `weatherLocation` takes `""` as the clear. `AfterUpdateAsync` re-syncs to-do due reminders on a zone change and, on a sleep-window or zone change, moves the user's untouched future calendar days onto the new window (FO10, Planning's `ICalendarSleepWindowSync`) |
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

⚠ **A session's `id` is a GUID and stable across refreshes (B28).** `GET /user/sessions` lists
`RefreshToken.SessionId`, and `DELETE /user/sessions/{id:guid}` takes it. It used to be the refresh-token
row id, a number that changed on every refresh, so an id the client read went stale at the next
rotation. The `guid` constraint is also what keeps `DELETE /user/sessions/all` off the single-session
route. Every revoke endpoint now stops the revoked sessions' **access** tokens at their next request, not
only their refresh. See [wiring.md](wiring.md#auth-plumbing-outside-the-endpoints).

**Google sign-in is portal-only, deliberately.** `GoogleSignInEndpoint`, `IGoogleSignInService` /
`GoogleSignInService`, and the `GoogleSignIn*` DTOs all stay in `AdhdTimeOrganizer`. It was moved to
Framework once and reverted: a *usable* provider has to ship the implementation, which puts
`Google.Apis.Auth` (+ `Newtonsoft.Json`) in `Sydowwe.Framework.csproj` for every solution, enabled or
not. Don't re-attempt it as part of a sweep. If a second federated
provider ever appears, the shape is a separate `Sydowwe.Framework.GoogleAuth` project, not a package
reference on the core. Google **Calendar** is unrelated and also stays portal.

⚠ **Google sign-up ignores the caller's locale.** When `GoogleSignInEndpoint` auto-registers a new
account it hardcodes `CurrentLocale = AvailableLocales.En` and `TwoFactorEnabled = false`, so a
Slovak user who first arrives through Google lands in English. Fixing it is one line here plus a
field on the SPA's sign-in call; nothing else depends on the current behaviour.

⚠ `BaseLogoutEndpoint` sets `AllowAnonymous()` **deliberately** — logout authenticates nothing, it acts
on whatever refresh token the cookie carries. Requiring a token 401s a caller whose access token
already expired, so the refresh token is never revoked and the cookies stay set. Don't "tighten" it;
`AuthFunctionalTests.Logout_RevokesRefreshToken_WhenAccessTokenIsExpired` pins this.

⚠ Framework's endpoint assembly is **excluded from FastEndpoints discovery** (`o.Assemblies` in
`Program.cs`, with `DisableAutoDiscovery = true`), so every endpoint there must be `abstract` — a
concrete one would never be routed. Don't widen `o.Assemblies` to "reuse" one; subclass it instead.

## The e2e fixture endpoint

`POST /api/test/seed` (`application/endpoint/testSupport/SeedTestDataEndpoint.cs`) resets the database
to the fixture the SPA's Playwright suite expects. It is the one endpoint in this solution that is
**unauthenticated and destructive**, so read this before touching any of it.

> **The full reference is [`docs/e2eTestSeed.md`](../e2eTestSeed.md)** — how to switch it on, the id
> table the SPA specs hardcode, what the fixture contains, and what the reset destroys. That file was
> written for this solution and is accurate; it is not one of the leftover copies. What follows here
> is only the part that constrains how you *change* the code.

- **What decides it exists is `TestSeedGate`, not a role and not a config flag.** Two conditions, both
  required: the hosting environment is on an allow-list (`Development` always; `Production` and
  `Staging` are refused even if named in `TestSeed:AllowedEnvironments`), *and* the database host is
  **this machine** — loopback or `host.docker.internal`.
- **The database half takes no configuration, and must not grow any.** There is no key that admits a
  remote host, because any such key is a way to point a truncating endpoint at a shared database.
  `DB_PORT` and `DB_NAME` are not consulted either, and neither could help: this solution's dev and
  prod databases are both `main_db` on one remote host and differ only by port
  (`AdhdTimeOrganizer/.env`), so a name check separates nothing and a port check would admit any host
  serving on that port. DNS is not consulted either — an unrecognised hostname is refused, not
  resolved, so the answer cannot change without the connection string changing.
- **Outside the gate the route is not registered.** `Program.ConfigureServices` filters the type out of
  FastEndpoints discovery via `EndpointDiscoveryOptions.Filter`, so there is no endpoint to authorize
  and none in Swagger. Do not "simplify" this into a check inside `HandleAsync`: a 403 confirms the
  endpoint is there, and the in-handler check that does exist is defence in depth, not the control.
- **What "absent" looks like here is 401, not 404.** `IdentityServiceExtensions` sets an authorization
  `FallbackPolicy` of `RequireAuthenticatedUser`, and ASP.NET applies the fallback to requests matching
  no endpoint too — so *every* undefined path in this app answers 401 to an anonymous caller. That is
  the behaviour a closed gate has to be indistinguishable from.
- **The fixture is `E2eFixtureSeeder`, and it is deliberately not a dev seeder.** It does not implement
  `IPerUserDevSeeder`, has no `Order`, and has no entry in `SeederOrderBands.md`. Dev seeders are one
  log-and-continue chain over the root admin producing a lived-in dataset with incidental ids; a spec
  needs two named accounts, ids it can hardcode, and a hard failure when any part did not land. It does
  reuse `IAppWideDefaultSeederManager` (roles + root admin) and `IUserDefaultsService` (each account's
  per-user defaults), so a fixture account is shaped exactly like one the app created.
- **The ids it guarantees live in `E2eFixtureIds`** and are echoed in the response body. They are a
  contract with the SPA repo — add an id rather than renumbering one. `FixtureVersion` lives there too and
  the SPA fails global setup on a mismatch, so a shape change a spec could notice has to bump it.
- **A row the fixture writes must be one the app's own write path could have produced.** The three tracking
  ledgers are the case that bites: their ingest endpoints require the extension role, so the seeder writes
  the rows directly — and therefore has to carry the pipeline's rules itself (minute-aligned windows,
  attribution through `IActivityTimeAttributionSink`, nothing stored for a process an ignore rule matches).
  Adding to them without those rules seeds a state the app cannot reach, which is worse than an empty grid
  because it looks real.
- The reset is `TRUNCATE … RESTART IDENTITY CASCADE` over every table **in the model**, derived from
  `dbContext.Model` so it cannot drift from the mapping, minus the `scheduler` schema (operational
  state; its registrars only run at boot). `__EFMigrationsHistory` is not an entity and so can never
  enter that list.

Guards: `TestSeedGateTests`, `TestSeedRouteRegistrationTests`, `TestSeedEndpointTests` — see
[testing.md](testing.md#things-that-only-a-behavioural-test-catches).

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

## Error bodies — `errorType`, not the status alone

Every error body the app sends is `TypedErrorResponse`
(`framework/Sydowwe.Framework/application/error/`): FastEndpoints' `statusCode` / `message` / `errors`
plus **`errorType`**, the `ResultErrorType` member name as a string (`"DbUniqueViolationError"`). It
exists because `EndpointHelper.ToStatusCode` flattens four situations onto 409 — a stale row, a unique
violation, an FK refusal and a business `Conflict` — and three onto 403, and the client has to word
them differently (*refresh and retry* / *pick another value* / *remove the dependents first* /
*confirm your address*).

- **Statuses are unchanged.** The field is additive; `ToStatusCode` is the same map it always was.
- **Producing it — two forms, both one call.** Mapped status:
  `await Send.ErrorsAsync(this.ErrorStatus(result.ErrorType), ct);` — `ErrorStatus` (on `IEndpoint`)
  maps *and* stamps, so a business conflict passes `ResultErrorType.Conflict` rather than the literal
  `409`. Hand-picked status (the auth flow's 401s, `PasswordSignInFlow`'s 403 for a lockout where
  `ToStatusCode` would say 423): `await Send.ErrorsAsync(result.ErrorType, 401, ct);` — the typed
  overload of FastEndpoints' `Send.ErrorsAsync` in `ResultErrorTypeExtensions`, which stamps and sends.
  There is no public "stamp only" call on purpose: FastEndpoints' own `Send.ErrorsAsync(int, ct)` has
  no parameter for the type, so a separate stamp is the half that gets forgotten — the 2FA login's
  lockout 401 went out typeless that way (B25).
- **The guard has two arms**, both in `ErrorStatusBypassGuardTests`: a text scan for a bare
  `EndpointHelper.ToStatusCode(` outside the two allowed files, and a Roslyn walk that fails on any
  `Send.ErrorsAsync(...)` inside an `if (x.Failed)` branch that is neither of the two forms above. The
  walk is scoped to `.Failed` branches: a literal 404 after a null check has no type in hand to drop.
- **`lockoutEndUtc`** is the optional fourth argument of the typed overload
  (`Send.ErrorsAsync(type, status, lockoutEnd, ct)`) and appears on the two lockout responses only:
  the password step's 403 and the 2FA step's 401. Both carry `errorType: "UserLockedOut"`; the 2FA
  step stays 401 so a client keeps reading a 401 on an auth route as "not signed in". The wrong code
  that *trips* the lock answers as the lockout, not as `InvalidTwoFactorAuthToken` — same as Identity
  and the password step.
- **It is absent, not null, when nothing stamped one** — FluentValidation 400s keep exactly the body
  they had, `errors` map included.
- **It is silent when unwired.** The type rides on `HttpContext.Items` and is rendered only by
  `config.Errors.ResponseBuilder = TypedErrorResponse.Build` in `Program.cs`. Remove that line and
  every endpoint still stamps, every status stays right, and the field just stops appearing.
  `ErrorTypeContractTests` asserts on the response body for that reason.

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
  (`framework/Sydowwe.Framework/application/dto/dto/TimeDto.cs`) instead of `TimeOnly`. Call
  `.ToTimeOnly()` when assigning to an entity — which throws on an out-of-range value, so **every
  request validator that carries one must call `RuleFor(x => x.SomeTime).ApplyTimeOfDayRules()`**
  (`AdhdTimeOrganizer.Core/application/validator/TimeDtoRules.cs`). There is deliberately no
  `Validator<TimeDto>`: FastEndpoints resolves validators by exact request type, so one would never run.
  The entity keeps `TimeOnly`; only the DTO edge converts, the way `PlannerTaskResponse` does. Note
  `TimeOnly.ToDto` cannot appear in an EF projection (CS9296) — inline
  `new TimeDto(x.Hour, x.Minute)` there.

  > ⚠ **What mixing costs, since the rule reads like style and is not.** `TodoListItem.DueTime` was a
  > bare `TimeOnly` on both requests and the response until 2026-09-11 — the last one in any portal DTO.
  > It serialised as `"15:09"` into an SPA that parses *every* time of day as `{hours, minutes}`, so
  > every due time read back as 00:00 and the zeroed value was then rejected on write. **Nothing on this
  > side failed**: entity, requests and response were all `TimeOnly`, so the backend was perfectly
  > self-consistent and every test passed. Only a client could see it. An off-convention type is
  > therefore not caught by round-trip tests — the guard is the convention itself.
- **Date ranges** in the history dashboards use `DateRangeDto`
  (`AdhdTimeOrganizer.Core/application/dto/dto/DateRangeDto.cs`): a `Date`, a `RangeType`, and an
  `EndDate` that **only `CustomRange` reads** — it is the range end, inclusive, capped at
  `DateRangeDto.MaxCustomRangeDays` (366, counted inclusively) because these dashboards aggregate the
  period's rows in memory; every other `RangeType` ignores it rather than rejecting it. `ToDateRange()` returns the user's days with both ends
  inclusive; `ToUtcRange(timeZone)` is what turns that into the half-open instant range to query with.
  Attach `DateRangeDto.ApplyValidationRules` from a **concrete** `Validator<TRequest>` per request type —
  FastEndpoints resolves validators by exact request type, so an abstract or open-generic base is never
  discovered and the request goes through unvalidated with no sign of it
  (`AdhdTimeOrganizer.History/application/validator/HistorySummaryDateRangeValidators.cs`).
- **Durations** — as opposed to times of day — are `IntTime`
  (`framework/Sydowwe.Framework/domain/valueObject/IntTime.cs`): `Hours` / `Minutes`, persisted as an
  `int` count of seconds via `IntTimeConverter` / `NullableIntTimeConverter`. `new IntTime(seconds)`
  and `.TotalSeconds` convert; `TotalSeconds` is `[JsonIgnore]`d, so the wire shape is
  `{hours, minutes}` — the same shape as `TimeDto`, deliberately a different type. A duration may
  exceed a day and may be added to another duration; a time of day may do neither, and `ToTimeOnly()`
  throws past 23:59. Pick by meaning, not by shape. `ActivityHistory.Length` and
  `BaseTodoListItem.SuggestedTime` are the two durations in the model.

  > `IntTime` is currently used directly in DTOs as well as entities, which couples the wire to an
  > EF-mapped domain type. `review/duration-dto-split.md` is the queued task to put a `DurationDto` at
  > that boundary, the way `TimeDto` already sits in front of `TimeOnly`.

  > ⚠ **`MyIntTime` is gone** (deleted 2026-09-11). This bullet used to name it as the module-DTO
  > convention, with a `MyIntTimeConverter` that was never written. It had zero usages in the solution:
  > a third copy of `IntTime`'s idea, differing only by a `Seconds` field. If you find it referenced
  > anywhere, that reference is stale.
