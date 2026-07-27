# Auth — state after the 2026-07-30 Framework merge

Handoff note for finishing the auth cleanup later, once the remaining base endpoints are done.
Scope: the JwtService / RefreshTokenService convergence and everything it dragged in.

## Where things live now

**`Sydowwe.Framework` owns all of it.** The portal's copies of `IJwtService`/`JwtService`,
`IRefreshTokenService`/`RefreshTokenService`, the `RefreshToken` entity, `AuthMethodEnum` and
`ClientTypeEnum` are deleted.

| Concern | Type |
|---|---|
| Token minting, refresh, partial-auth tokens | `JwtService<TUser>` / `IJwtService<TUser>` (+ non-generic `IJwtService`) |
| Refresh-token store | `RefreshTokenService` / `IRefreshTokenService` |
| Cookie names, paths, session hash | `domain/helper/AuthCookies.cs` |
| 2FA policy | `config/TwoFactorOptions.cs` — `Mode` (Disabled/Optional/Required) + `FederatedLoginSatisfiesTwoFactor` |
| 2FA validation (all transports) | `TwoFactorAuthService.ValidatePendingLoginToken` |
| Throttle key header | `application/middleware/TrustedIpMiddleware.cs` → `ClientIpHeaderName` |
| Login / 2FA / password-change endpoints | `BaseLoginEndpoint`, `BaseValidateTwoFactorAuthForLoginEndpoint`, `BaseSetupTwoFactorForLoginEndpoint`, `BaseChangePasswordEndpoint` |

**Portal side** is now thin: `LoginUserEndpoint` and `ValidateTwoFactorAuthForLoginWebEndpoint` have
empty bodies; the extension variant overrides only `ReadPendingToken` + `OnAuthenticatedAsync`. Plus
`ExtensionRoleClaimsProvider` (grants the extension-only `ActivityTracking` role via Framework's
`IAdditionalUserClaimsProvider` seam) and the DI registrations in `DependencyInjectionExtensions`.

## Key decisions

- **One partial-auth mechanism.** Carrier is the signed short-lived JWT
  (`IssueTwoFactorPendingToken` / `ReadTwoFactorPendingToken`); the DataProtection token is gone. Its
  guards were kept and now live in `ValidatePendingLoginToken`: single use (keyed on the token's
  `jti`), lockout check, `AccessFailedAsync` on a wrong code, and recovery codes accepted.
- **Transport-agnostic.** Raw-token core with cookie wrappers, so web / extension / desktop all use
  the same flow. Cookie for browsers, `PendingAuthToken` in the body for token clients.
- **Routes:** `/auth/login`, `/auth/login/2fa`, `/auth/login/2fa/extension`, `/auth/login/2fa/setup`.
- **Federated login satisfies 2FA by default** (`FederatedLoginSatisfiesTwoFactor = true`) — the usual
  convention, now a stated policy rather than a missing check in `GoogleSignInEndpoint`.
- **Extension access is deny-by-default.** The endpoint configurator in `Program.cs` attaches the
  `DenyExtensionClients` policy to every endpoint without `[AllowExtensionClients]`. The old
  `FallbackPolicy` never fired, because the same configurator gives every endpoint role metadata and
  an endpoint with any authorization metadata skips the fallback.
- **`AuthMethodEnum` values are a storage contract** (`refresh_token.auth_method` is an `int`):
  `Password = 0, Microsoft = 1, Google = 2`. Append only. Migration `FrameworkRefreshTokenEntity`
  remaps the portal's old `Google = 1` rows to `2`.
- **Business audit is live** — `BusinessAuditLogEntityConfiguration` applied to `AppDbContext`,
  migration creates `audit.business_audit_log`. The partitioned `audit_log` + CRUD interceptor are
  still deliberately not wired.

## Bugs fixed on the way (don't reintroduce)

1. `BaseWithUserEntitySaveChangesAsync` **overwrote** `UserId` on inserted `IEntityWithUser` rows
   instead of only filling an unset one — a refresh token minted for user A while B was the ambient
   caller was stored as B's. Now guarded by `UserId == 0`.
2. `RefreshTokenService` ran under the global user query filter, which applies to
   `ExecuteUpdateAsync` too, so `RevokeAllUserTokensAsync(otherUserId)` reported success and updated
   nothing. All its queries go through `IgnoreQueryFilters()`.
3. Extension 2FA issued **cookies** an extension cannot use — a 2FA-enabled extension user could never
   get a working session. Now returns a token pair in the body (response shape changed: 204 → 200).
4. `AuthCookies.RefreshTokenPath` too narrow (logout couldn't revoke) and `session-hash` not cleared
   on password change — both fixed in the earlier pass, keep `AuthCookies` as the single source.

## Left to do

- [x] **`BaseSetupTwoFactorForLoginEndpoint` now has a portal subclass.** Done 2026-07-30:
      `SetupTwoFactorForLoginEndpoint` (empty) exposes `POST auth/login/2fa/setup`, so an account with
      `RequiresTwoFactorSetup: true` can provision an authenticator off the partial-auth cookie.
      Covered by `AuthFunctionalTests.SetupTwoFactorForLogin_WithPendingCookie_ProvisionsAuthenticator`
      and `AuthSecurityTests.TwoFaSetup_WithoutPendingCookie_Returns401`. Still outstanding for
      `TwoFactorMode.Required`: **the SPA setup screen**, and the **extension has no equivalent** — its
      login returns the partial-auth token in the body, and this route reads a cookie.
- [ ] **Single-use guard is per-process.** `Program.cs` registers `AddDistributedMemoryCache()`; the
      "one attempt per password step" guarantee needs Redis before scaling out.
- [x] **Framework's `RefreshTokenEndpoint` and `LogoutEndpoint` were concrete, not abstract bases.**
      Done 2026-07-30: now `BaseRefreshTokenEndpoint` / `BaseLogoutEndpoint`, with the portal's copies
      reduced to empty subclasses. This also fixed a real logout bug — the portal copy lacked
      `AllowAnonymous()`, so a caller with an expired access token got 401 and the refresh token was
      never revoked. (`UserRoleGetAllSelectOptionsEndpoint` got the same treatment; no portal subclass.)
- [ ] **Still standalone in the portal, not on framework bases:** `GoogleSignInEndpoint`,
      `RegisterUserEndpoint`, the three `extension/*` endpoints, `ForgotPassword`/`ResetPassword`,
      email confirmation, `LogoutAll`, session management. Framework has no bases for most of these
      yet — that's the next chunk of work, not a repoint.
- [ ] **Portal `application/preprocessor/VerifyUserPreProcessor.cs`** is a thin alias over Framework's
      generic one; fold it away when the step-up call sites are touched next.
- [ ] **Google sign-up ignores locale** — `GoogleSignInEndpoint` hardcodes `CurrentLocale = En` and
      `TwoFactorEnabled = false` when auto-registering. One-line fix plus an SPA field.

## Gotchas

- Framework's auth services are **open generics without DI marker interfaces**, so the Scrutor scans
  cannot find them. `IJwtService<User>`, `IJwtService`, `ITwoFactorAuthService<User>` and
  `IUserEmailSenderService<User>` are registered explicitly in `DependencyInjectionExtensions`.
  Dropping a portal service in favour of a generic Framework one compiles and then fails at **runtime**
  on first resolution. `IRefreshTokenService` is the exception — that one is non-generic and carries
  `IScopedService`, so the scan does find it.
- `ThrottleHeaderKey` defaults to `TrustedIpMiddleware.ClientIpHeaderName`. A host that does not call
  `UseTrustedClientIpHeader()` must override it to `null`, or every caller shares one throttle bucket
  and the limit becomes global instead of per-client.
- `TrustedIpMiddleware` must be registered **after** `UseForwardedHeaders()`, or `RemoteIpAddress` is
  the proxy and every request buckets together.

## Tests

112 total, 111 passing. `Sydowwe.Framework.Testing` fixtures needed three fixes to exercise real
login: the seeded test user had no role (the global `Roles(...)` configurator 403s a role-less JWT),
test-created users lacked `NormalizedEmail`/`SecurityStamp`/`LockoutEnabled`, and assertions about
other users' rows need `IgnoreQueryFilters()`. `RefreshTokenServiceTests` gained rotation coverage
(replacement link + replay rejection).
