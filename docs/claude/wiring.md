# Composition Root, Auth Plumbing & Config

The Notifications / Reminders / Scheduler modules are wired in
`AdhdTimeOrganizer/config/dependencyInjection/` and `Program.cs`.
`AdhdTimeOrganizer.IntegrationTests/Modules/ModuleWiringTests.cs` pins all of it — run it after
touching anything below, because **none of these break the build**.

## Module wiring

- **Two marker scans, non-overlapping assembly sets — keep them that way.**
  `DependencyInjectionExtensions.AddDependencyInjection` sweeps `AppDomain.CurrentDomain.GetAssemblies()`;
  `ModuleServiceExtensions.AddModuleServices` scans an explicit `ModuleAssemblies` list (explicit
  because the CLR loads lazily, so a module nothing has touched yet contributes nothing to an
  `AppDomain` sweep). Both look for the **same** `Sydowwe.Framework` lifetime interfaces, so the sweep
  `Except`s `ModuleAssemblies`. Drop that and every module service is registered twice: single
  resolutions still work (last wins), but every `IEnumerable<T>` doubles — two `ReminderScanJobHandler`s
  means the dispatch scan runs twice per fire, two of each seeder means every seeder runs twice.
  Nothing throws or logs.
- **Generic-over-`TUser` services can't be scanned** — `NotificationService<User>`,
  `IDeferredNotificationDispatcher`, `UserTimeZoneReader<User>` and `UserLocaleReader<User>` are closed by
  hand in `AddModuleServices`, same as the framework user services in `AddDependencyInjection`.
- **Module services take a plain `DbContext`** (that's how they stay host-agnostic), so
  `AddModuleServices` aliases `DbContext` → `AppDbContext`. Without it ~34 of them fail to activate.
- **FastEndpoints discovery is an explicit assembly list** in `Program.cs`. A module missing from it is
  not an error — its endpoints just 404.
- **Boot reconciliation:** each module's *and slice's* `…ScheduledJobsRegistrar` is an `IHostedService`
  that upserts its recurring jobs through Kernel's `IScheduler`. Required on **every** boot — the Quartz
  RAM job store drops all triggers on restart. Six are wired in `Program.cs`: Notifications, Reminders,
  Scheduler, Routines, Tracking and `PortalScheduledJobsRegistrar` (the host's own).
- **`Sydowwe.Scheduler` is the only project in the solution that references Quartz.** The host's
  scheduling surface is one call — `services.AddSchedulerSubstrate()` — which owns the single
  `AddQuartz`, the durable generic dispatcher job and the Quartz hosted service. Everything else is a
  keyed `IScheduledJobHandler` (picked up by the ordinary lifetime-marker scan) plus a
  `RecurringJobRegistration` its owner's registrar pushes. **Never write a Quartz `IJob`, add a
  `Quartz.*` package reference, or make a second `AddQuartz` call** — none of that fails to build, it
  just creates a second scheduling path invisible to the run log, the retry policy, the failure
  alerting and the dashboard. `ModuleWiringTests.OnlySchedulerModule_ReferencesQuartz` and
  `ScheduledJobHandlers_AreAllDiscoverableByKey` are the guards; note a handler in DI with no
  registration, and a registrar dropped from `Program.cs`, are both **silent** — the job simply never
  fires.
- **`IQuietHoursReader` must resolve to Notifications' `QuietHoursReader`.** Reminders ships
  `NoQuietHoursReader` for hosts without Notifications; it carries no lifetime marker on purpose,
  because an auto-registered no-op would silently disable quiet hours everywhere.
- **`IUserTimeZoneReader` must be registered, and it is the one nothing else will tell you about.** Quiet
  hours are evaluated in each recipient's own `User.Timezone`, read through this seam; because it is generic
  over the host's user type no scan can see it, and its absence throws nothing — every window just falls back
  to UTC and is enforced an hour or two off the one the settings page displays. `ModuleWiringTests`
  (`UserTimeZoneReader_IsRegisteredClosedOverTheHostsUserType`) and
  `QuietHoursUserZoneAndLateDeliveryTests` are the guards.
- **`IUserLocaleReader` is its sibling** (B15): notification text is rendered in each recipient's
  `User.Locale`, read through this seam, and the module ships it generic over the user type for the same
  reason. Its absence is at least loud — `NotificationService` and the bell endpoint take it in their
  constructors — but `ModuleWiringTests` (`UserLocaleReader_IsRegisteredClosedOverTheHostsUserType`) pins the
  concrete type so a stub cannot quietly make everyone read Slovak; `NotificationLocaleTests` asserts the bytes.
- **This host words the notification kinds it raises** — `AddNotificationVocabulary` registers
  `SkPortalNotificationTexts` / `EnPortalNotificationTexts` (`infrastructure/notification/`) through
  `NotificationTextOptions`, with `Cz` pointed at the Slovak table. The framework's tables word only the kinds
  its own modules raise; the routine nudges, the pomodoro boundary and the personal reminder are this portal's
  and would render the generic "Nová notifikácia" without this call — nothing throws. Adding a kind to the
  catalog means wording it in both tables; `NotificationVocabularyTests` is what turns a forgotten one red.
- **This host declares which notification kinds it raises** — `AddNotificationCatalog` in
  `ModuleServiceExtensions` fills `NotificationCatalogOptions.SupportedTypes` with the ten members of
  `NotificationType` some producer here actually calls `NotifyAsync` with. That is what
  `GET /notification-preference/mine` publishes; the other nine belong to the HR/inventory app that shares
  `Sydowwe.Framework.Contracts`. **Add a kind to that list whenever you add a producer** — an empty catalog
  means "the whole enum" (so a host that never opts in is unaffected), which also means a *stale* one fails
  silently: the kind still dispatches, it just loses its row on the settings screen. `NotificationCatalogTests`
  is the guard, and it keeps a second copy of the list on purpose. The same call declares `HeldWhileAwayTypes`
  and `PushOffByDefault` — the kinds that reach the bell but not the lock screen until the user ticks push
  (empty today; a bell-only kind goes there with its producer) — and `Actions`, the Web Push action buttons per
  kind (P2-03: Snooze · Dismiss, Done · Snooze 1 h, Start · Skip, Done · +15 min). A button is carried out by
  `POST /notification/{id}/action/{action}`, which hands every button but Dismiss to the one
  `INotificationActionHandler` (Contracts) that answers it — `SnoozeReminderNotificationActionHandler` (Reminders),
  `PlannerTaskNotificationActionHandler` (Planning), `TodoItemDueNotificationActionHandler` (TodoLists). A handler
  only resolves if its slice is in `ModuleAssemblies`, and a button without one silently just opens the app:
  `NotificationCatalogTests` pins exactly one handler per declared button and keeps the table's second copy.
- **Account deletion fans out over `ISubjectDataEraser`** (`UserAccountDeletionService`, used by
  `DeleteUserAccountEndpoint.BeforeDeleteAsync` and by the unconfirmed-account purge — delete an account any
  other way and the module rows outlive it). The host FKs cascade only `notification` /
  `notification_preference` / `push_subscription`; every other user-keyed module table is deliberately
  FK-free and would outlive the account. Erasers mutate the ambient `DbContext` and never commit —
  `UserManager.DeleteAsync`'s own `SaveChanges` (same scoped context) is the transaction, which is also why
  a caller deleting several accounts gives each its own scope.
- **Config precedence is env-over-JSON.** `Program.cs` re-adds `appsettings.json` after `CreateBuilder`
  to fix the base path, which put JSON *after* the environment provider; `AddEnvironmentVariables()` is
  re-added last to restore the standard order. Module secrets use `Section__Key`
  (`PushNotification__VapidPrivateKey`) and would otherwise be shadowed by an appsettings placeholder.

## Auth plumbing outside the endpoints

Same rule as the endpoints: the *mechanism* is Framework's, anything naming a product decision stays in
the portal.

**Token claim names — `framework/Sydowwe.Framework/domain/helper/AuthClaims.cs`.** `AuthMethod`
(`auth_method`), `ClientType` (`client_type`), `ExtensionClientType` (`"Extension"`). `JwtService`
writes them and the authorization handlers/policies read them; both sides reference these constants.
Never re-type the literals — a typo does not fail the build, it silently changes who is allowed in.

**Extension-client gate — `framework/Sydowwe.Framework/infrastructure/security/ExtensionClientAuthorization.cs`.**
`ExtensionClientRequirement`, `ExtensionClientAuthorizationHandler`, `[AllowExtensionClients]`, and the
policy names `DenyExtensionClients` / `WebOnly` / `ExtensionOnly` on `ExtensionClientPolicies`. Deny by
default: the endpoint configurator in `Program.cs` attaches `DenyExtensionClients` to every endpoint
*without* `[AllowExtensionClients]`. Don't switch this to `AuthorizationOptions.FallbackPolicy` — the
configurator gives every endpoint role metadata, and an endpoint carrying any authorization metadata
never falls back, which is why the deny is attached per endpoint.

**What stayed out of Framework, deliberately:**

- `AdhdTimeOrganizer.Core/infrastructure/security/PortalAuthorizationPolicies.cs` — `ActivityTracking`,
  the policy name gating the tracking endpoints. **In Core, not the host**: a slice project has to name
  the policy to attach it, and a slice cannot see the host. Only the *name* moved — what the policy
  **requires** is still declared host-side in `config/IdentityServiceExtensions.cs`, because which
  clients may report activity is a product decision. Don't push this further into Framework.
- `infrastructure/extService/user/auth/ExtensionRoleClaimsProvider.cs` — **still host-side**; grants the
  `ActivityTracking` *role* to extension tokens via Framework's `IAdditionalUserClaimsProvider<TUser>`
  seam. It exists so Framework never learns this deployment's role names; moving it would invert that.
- Note the policy name and the role name are **two different constants that happen to share the string**
  `"ActivityTracking"`. Renaming one does not rename the other. The
  `AutoTagOverride("ActivityTracking")` in `AdhdTimeOrganizer.Tracking/application/endpointGroups/` is a
  third, unrelated use — a Swagger tag. Leave it a literal.

**Framework's auth services are open generics with no DI marker interface**, so the Scrutor scans in
`config/dependencyInjection/DependencyInjectionExtensions.cs` cannot see them — `IJwtService<User>`,
`IJwtService`, `ITwoFactorAuthService<User>`, `IUserEmailSenderService<User>` and
`ISecurityAlertService<User>` are registered explicitly there. Dropping a portal service in favour of a generic Framework one **compiles** and then
throws at runtime on first resolution; add the explicit registration in the same commit.
`IRefreshTokenService` is the exception — non-generic and carrying `IScopedService`, so the scan finds it.

**`ThrottleHeaderKey` defaults to `TrustedIpMiddleware.ClientIpHeaderName`.** A host that does not call
`UseTrustedClientIpHeader()` must override it to `null`, or every caller shares a single throttle bucket
and the per-client limit silently becomes a global one. This host does call it —
`Program.cs` has `UseTrustedClientIpHeader()` directly after `UseForwardedHeaders()`, and that order is
load-bearing: before it, `RemoteIpAddress` is the proxy and every request buckets together anyway.

⚠ **The 2FA single-use guard is per-process.** `Program.cs` registers `AddDistributedMemoryCache()`, so
the "one attempt per password step" guarantee that `TwoFactorAuthService.ValidatePendingLoginToken`
enforces (keyed on the pending token's `jti`) holds only within one instance. Running a second instance
lets a pending token be spent once per process. Redis before scaling out.

**Session revocation reaches the access token (B28).** A revoke of any kind (one session, all others,
logout, logout-all, a password change or reset, an e-mail change, the refresh-reuse response, account
deletion) stops the session's access token at its **next request**. Before B28 it only stopped the
refresh, leaving the access token good for its remaining 15 minutes. The pieces:

- **The session id.** `RefreshToken.SessionId` (`user.refresh_token.session_id`, indexed) is minted at
  login and copied on every rotation, so it names the *session*. The row `Id` names only the current
  link. Every access token carries it as the `sid` claim (`AuthClaims.SessionId`). Read it with
  `ClaimsPrincipal.GetSessionIdOrNull()`, never by the literal: JwtBearer's inbound mapping renames
  `sid` to `ClaimTypes.Sid`.
- **The check.** `IdentityServiceExtensions` wires `OnTokenValidated =
  SessionRevocationBearerEvents.RejectRevokedSessionAsync`. It calls
  `ISessionRevocationService.IsSessionLiveAsync` (`framework/…/extService/user/auth/SessionRevocationService.cs`),
  and on a dead session it calls `context.Fail`: a plain 401, **no** `X-Token-Expired`. A token with no
  `sid` (minted before this deployed) passes until it expires. The line fails silently if removed;
  `SessionRevocationTests` is the guard.
- **Source of truth is the refresh-token table**: a session is live while a non-revoked row holds its
  id. The cache is read-through with two keys per session. The "revoked" key is checked first and lives
  `RevokedVerdictTtl` (16.5 min: `AuthTokenLifetimes.AccessToken` + `ClockSkew` + 1 min). The "live" key
  lives `LiveVerdictTtl` (60 s). A miss, or a throwing cache, queries the table, so a cold cache after a
  restart can never let a revoked session through.
- **Every revoke goes through `IRefreshTokenService`**, which reports the sessions it revoked. Don't
  write `IsRevoked` by hand: the refresh dies, and a cached live verdict keeps the access token working
  for up to 60 s. Account deletion is the one path that bypasses it (the rows cascade), so
  `BaseDeleteUserAccountEndpoint` snapshots the ids before the delete and reports them after.
- ⚠ **Per-process, like the 2FA guard.** With the in-memory cache a revoke is immediate on the instance
  that served it. Another instance keeps a live verdict it already cached for at most 60 s, then asks
  the database. A shared `IDistributedCache` (Redis) makes it immediate everywhere; nothing else changes.
- **Open SignalR connections** are authenticated once, at handshake, so the per-request check never sees
  them. `NotificationHub` files each connection under its `sid`, and Notifications'
  `ISessionRevocationListener` aborts them on revoke. The hub re-checks the session after filing the
  connection, which covers a revoke that lands mid-handshake. That is also per-process: a connection on
  another instance lives until it drops, and its reconnect then fails the handshake.
- **A token without `sid` passes, so nothing else may mint one under the access-token audience.** The 2FA
  pending token (issued after the password alone) is signed with the same key and used to share the
  audience, so JwtBearer accepted it as an access token and the role-less `[Authorize]` hub opened without
  the second factor. It now has its own audience (`JwtService.TwoFactorPendingAudience`, `JWT_AUDIENCE` +
  `:2fa-pending`). Pinned by `AuthSecurityTests.PendingTwoFactorToken_IsRefusedAsAnAccessToken`. A pending
  token issued before that deployed is refused by the 2FA step; the user re-enters the password.
- **Only a rotated token counts as theft.** `ValidateRefreshTokenAsync` refuses a token revoked
  *without* a replacement link (logout, a session revoke, revoke-others, a credential reset, an expiry)
  and does nothing else. Only a token that was *rotated* and then replayed past the 5 s grace window
  triggers the revoke-everything response. Before this split, the revoked device's first refresh (which
  the client always makes on its first 401) was read as reuse, so revoking a device signed out the revoker
  too. Don't merge the two branches back: `SessionRevocationTests.RevokeSession_ThenTheRevokedDeviceRefreshes_…`
  and `RefreshTokenServiceTests.ValidateRefreshToken_*` pin both sides.

**Refresh-token cleanup — `framework/Sydowwe.Framework/infrastructure/extService/user/auth/RefreshTokenCleanupService.cs`.**
`BackgroundService` next to the `RefreshTokenService` it drives; hosts register it with
`AddHostedService<RefreshTokenCleanupService>()`. First sweep runs at **startup**, then every `Interval`
(`protected virtual`, 24h) — a host restarting more often than the interval would otherwise never clean
up at all. Logs counts only, never a token owner.

## Email templates

`framework/Sydowwe.Framework/infrastructure/templates/email/` — `ConfirmEmail.html`,
`ResetPassword.html`, `ResetPasswordCode.html`, `SecurityAlert.html`, consumed by
`UserEmailSenderService`. All are English; none follow `User.Locale` yet. They are
**`<EmbeddedResource>`** in `Sydowwe.Framework.csproj` (`infrastructure\templates\email\*.html`), read
via `Assembly.GetManifestResourceStream`, so there is no copy-to-output step and no working-directory
assumption. A new template just needs to land in that folder — the glob picks it up.

A host overrides any single template by placing a file of the same name in
`{AppContext.BaseDirectory}/templates/email/`; it is checked first, and anything absent falls back
per-file to the embedded copy.

⚠ Do **not** go back to loading these from disk. The previous arrangement kept them in the portal as
`<Resource Include=… CopyToOutputDirectory="Always">` — `Resource` is a WPF item type the .NET SDK
ignores, so they never reached the output — and read them from
`Directory.GetCurrentDirectory()/templates/email`, a path that did not exist in any environment. Every
mail this service sends threw `FileNotFoundException`; on sign-up that surfaced as a 500 *after* the
account was already committed. Nothing in the test suite covered mail rendering then, so it went
unnoticed. `SecurityAlertTests` now runs the real sender, stopped at the SMTP hop. `SendEmailAsync` is
virtual for that purpose. Copy its `CapturingUserEmailSender` to test any other account mail.

## Security alerts (P1-05)

After a password, login-address or 2FA change, the framework endpoint bases mail the owner. The
`SecurityAlertKind`s are `PasswordChanged`, `PasswordReset`, `EmailChanged`, `TwoFactorEnabled`,
`TwoFactorDisabled`, `TwoFactorReset` and `RecoveryCodesRegenerated`. The bases take
`ISecurityAlertService<TUser>` in their constructor, so every host gets the mails without opting in.

- **Never fails the change.** `SecurityAlertService` swallows a send failure and logs `{Kind}` and
  `{UserId}` only. The change is already committed by the time it runs.
- **Host opt-out:** `"SecurityAlerts": { "Enabled": false }` (`SecurityAlertOptions`, read per send).
  There is no per-user switch, and it deliberately does **not** go through `NotifyAsync`, so preferences,
  quiet hours and the away hold never apply.
- **An email change alerts the *previous* address,** captured before `ChangeEmailAsync`. The mail shows
  the new address in full and carries an undo link instead of forgot-password, because a reset link would
  go to the new address. The undo link is a change-email token back to the old address, minted *after*
  the security-stamp bump (which would otherwise kill it). It is consumed by the same
  `/confirm-email-change` SPA page and endpoint.
- The sender builds every SPA link from `PAGE_URL` (`BuildEmailChangeLink`, `BuildForgottenPasswordLink`,
  both virtual). The link is built inside the swallowed path, so a missing variable loses the mail but
  not the change.
