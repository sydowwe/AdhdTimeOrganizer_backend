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
- **Generic-over-`TUser` services can't be scanned** — `NotificationService<User>` and
  `IDeferredNotificationDispatcher` are closed by hand in `AddModuleServices`, same as the framework
  user services in `AddDependencyInjection`.
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
- **Account deletion fans out over `ISubjectDataEraser`** (`DeleteUserAccountEndpoint.BeforeDeleteAsync`).
  The host FKs cascade only `notification` / `notification_preference` / `push_subscription`; every
  other user-keyed module table is deliberately FK-free and would outlive the account. Erasers mutate
  the ambient `DbContext` and never commit — `UserManager.DeleteAsync`'s own `SaveChanges` (same scoped
  context) is the transaction.
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
`IJwtService`, `ITwoFactorAuthService<User>` and `IUserEmailSenderService<User>` are registered
explicitly there. Dropping a portal service in favour of a generic Framework one **compiles** and then
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

**Refresh-token cleanup — `framework/Sydowwe.Framework/infrastructure/extService/user/auth/RefreshTokenCleanupService.cs`.**
`BackgroundService` next to the `RefreshTokenService` it drives; hosts register it with
`AddHostedService<RefreshTokenCleanupService>()`. First sweep runs at **startup**, then every `Interval`
(`protected virtual`, 24h) — a host restarting more often than the interval would otherwise never clean
up at all. Logs counts only, never a token owner.

## Email templates

`framework/Sydowwe.Framework/infrastructure/templates/email/` — `ConfirmEmail.html`,
`ResetPassword.html`, `ResetPasswordCode.html`, consumed by `UserEmailSenderService`. They are
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
account was already committed. Nothing in the test suite covers mail rendering, so it went unnoticed.
