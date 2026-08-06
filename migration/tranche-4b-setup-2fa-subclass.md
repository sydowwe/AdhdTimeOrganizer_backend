# Tranche 4b — the missing `BaseSetupTwoFactorForLoginEndpoint` subclass

**Status: DONE (2026-07-30).** `SetupTwoFactorForLoginEndpoint` added under
`AdhdTimeOrganizer/application/endpoint/user/command/auth/passwordAuth/`; `POST auth/login/2fa/setup`
is the one added route. Build clean, auth suite 28/28 (was 26 — the two new tests are named below).
`IAuditService` was already resolvable (`AuditService : IScopedService`, same as `LoginUserEndpoint`
uses), so no DI change was needed. The extension gap is unchanged — see [tranche 5](tranche-5-extension.md).

**Verdict: yes, do it — but as its own tranche, not folded into tranche 4.**

This is **not a migration**. It is a new portal route that closes an existing Framework base which
currently has no consumer. Keeping it separate means tranche 4 stays a pure refactor whose Swagger
diff is empty, and this tranche's diff is exactly one added route.

## What exists

`framework/Sydowwe.Framework/application/endpoint/user/command/auth/BaseSetupTwoFactorForLoginEndpoint.cs` is
complete and abstract:

- Route `POST auth/login/2fa/setup`, `AllowAnonymous()`, `Throttle(10, 60, ThrottleHeaderKey)`
- Reads the partial-auth cookie via `jwtService.ReadTwoFactorPendingCookie(HttpContext)`
- Calls `twoFactorAuthService.SetUpTwoFactorAuth(user)`, returns `TwoFactorAuthResponse`
- ctor: `UserManager<TUser>`, `ITwoFactorAuthService<TUser>`, `IJwtService<TUser>`, `IAuditService`

`CLAUDE.md`'s auth table lists its portal subclass as **none**, and `auth-state.md` records that this
is why `TwoFactorMode.Required` is unusable: the login flow can set `RequiresTwoFactorSetup = true`,
but there is no route the SPA can call to actually provision the authenticator.

## What to add

```csharp
public class SetupTwoFactorForLoginEndpoint(
    UserManager<User> userManager,
    ITwoFactorAuthService<User> twoFactorAuthService,
    IJwtService<User> jwtService,
    IAuditService auditService)
    : BaseSetupTwoFactorForLoginEndpoint<User>(userManager, twoFactorAuthService, jwtService, auditService);
```

`Configure`-less. Place it next to the other closed auth bases in
`AdhdTimeOrganizer/application/endpoint/user/command/auth/passwordAuth/`.

## Check before wiring

- **`IAuditService` must be resolvable from DI.** The base injects it. Auditing is *not wired up* in
  this solution — the interceptor is unregistered and there is no `audit_log` migration — so
  `LogAndSaveAsync` writes nothing. That is acceptable here (the base already ships that call), but
  confirm the service itself is registered or the endpoint 500s at construction. Do not describe this
  route as "audited".
- **The extension flow has no equivalent.** `ExtensionLoginEndpoint` returns
  `RequiresTwoFactorSetup` in a `PendingAuthToken` body rather than a cookie, and this base reads a
  *cookie*. So this closes the gap for the web client only. Decide explicitly whether the extension
  needs a token-based sibling — see [tranche 5](tranche-5-extension.md).

## Then

- Update `CLAUDE.md`'s auth-bases table: `BaseSetupTwoFactorForLoginEndpoint<TUser>` →
  `SetupTwoFactorForLoginEndpoint`.
- Update `auth-state.md`, which currently records `TwoFactorMode.Required` as unusable.
- Add a test for the forced-setup path; the existing auth suite does not cover it.
  → `AuthFunctionalTests.SetupTwoFactorForLogin_WithPendingCookie_ProvisionsAuthenticator` (login with
  2FA enabled + no authenticator key → `RequiresTwoFactorSetup: true` → setup returns QR + recovery
  codes and persists the key) and `AuthSecurityTests.TwoFaSetup_WithoutPendingCookie_Returns401`.

## Risk

Low mechanically, but this is the one tranche that **adds** a route — the Swagger diff is expected to
be non-empty here and nowhere else.
