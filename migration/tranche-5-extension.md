# Tranche 5 — Browser-extension clients (3 endpoints)

**Verdict: move. One seam, and one duplication to resolve rather than propagate.**

| Portal endpoint | Route | New Framework base |
|---|---|---|
| `command/auth/extension/ExtensionLoginEndpoint.cs` | `POST /auth/extension/login` | `BaseExtensionLoginEndpoint<TUser>` |
| `command/auth/extension/ExtensionLogoutEndpoint.cs` | `POST /auth/extension/logout` | `BaseExtensionLogoutEndpoint` (non-generic) |
| `command/auth/extension/ExtensionRefreshTokenEndpoint.cs` | `POST /auth/extension/refresh` | `BaseExtensionRefreshTokenEndpoint` (non-generic) |

## The open question, answered: yes, "extension client" is already a Framework concept

The prompt asks whether a browser extension is a framework concept or an ADHD-portal one. It is
already the former, and the evidence is in Framework, not the portal:

- `IJwtService<TUser>.GenerateTokensForExtensionAsync(AuthMethodEnum, user)` — Framework
- `IJwtService.IssueTwoFactorPendingToken(userId, requiresSetup)` — the token-body counterpart of the
  cookie flow, Framework
- `IJwtService.RefreshTokensAsync(refreshToken, HttpContext)` — Framework
- The refresh-token entity already models extension sessions
- `ValidateTwoFactorAuthForLoginExtensionEndpoint` **is already on a Framework base**
  (`BaseValidateTwoFactorAuthForLoginEndpoint<TUser>`)

So the framework already carries the extension concept end to end. Leaving these three in the portal
is the inconsistency, not moving them.

## The seam

`ExtensionLoginEndpoint` reads `user.HasExtensionAccess` — a **portal `User` column**, not on
`BaseUser`:

```csharp
if (!user.HasExtensionAccess) { AddError("Extension access not enabled for this account"); … 403 }
```

Whether a solution gates extension access per account is a product decision. Expose:

```csharp
protected virtual bool HasExtensionAccess(TUser user) => true;
```

and have the portal wrapper override it to `user.HasExtensionAccess`. This is the one wrapper in the
whole migration that is legitimately not `Configure`-less-and-empty.

## The duplication to resolve, not propagate

`ExtensionLoginEndpoint`'s handler re-implements `BaseLoginEndpoint`'s core almost verbatim: find by
email → `EmailConfirmed` check → `CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)`
→ lockout message with `{minutes}m {seconds}s` → 2FA branch on `TwoFactorOptions.Mode` +
`user.TwoFactorEnabled` + `GetAuthenticatorKeyAsync(user) is null`.

The only genuine differences are the extension-access gate and that tokens come back **in the body**
rather than as cookies. Factor the shared password/lockout/2FA decision out of `BaseLoginEndpoint`
into a protected method or small service both bases call — do not land a second copy of it in
Framework. If that refactor looks too large for this tranche, split it: move the two trivial
endpoints (logout, refresh) first and do login as 5b.

## What moves alongside

| DTO | Notes |
|---|---|
| `ExtensionLoginRequest` | `{Email, Password}`. Compare with Framework's `PasswordLoginRequest` before adding — it may already fit |
| `ExtensionLoginResponse` | already derives from Framework's `LoginResponse`; adds `AccessToken`, `RefreshToken`, `PendingAuthToken` |
| `RefreshTokenRequest` | `{string? RefreshToken}` — used by both logout and refresh |
| `RefreshTokenResponse` | `{AccessToken, RefreshToken}` |

## Preserve exactly

- `ExtensionLoginEndpoint`: `AllowAnonymous()`, `Throttle(5, 60, TrustedIpMiddleware.ClientIpHeaderName)`
- `ExtensionRefreshTokenEndpoint`: `AllowAnonymous()`, `Throttle(10, 60, …)`
- **`ExtensionLogoutEndpoint` is NOT anonymous** — unlike the web `BaseLogoutEndpoint`, which sets
  `AllowAnonymous()` deliberately (and has a test pinning it). Do not harmonize these two: the web
  one acts on a cookie and must work with an expired access token; the extension one takes the token
  in the body. Carry a comment saying so.
- The status codes are load-bearing: 401 for bad credentials, **403** for lockout *and* for missing
  extension access.

## Behaviour that must survive the move

- The 2FA branch issues a **body** `PendingAuthToken`, not a cookie — no session exists until
  `/auth/login/2fa/extension` validates the code. The existing comment in the file explains this;
  carry it over.
- `ExtensionLogoutEndpoint` no-ops silently on an empty token and still returns 204.
- **PII**: `ExtensionLoginEndpoint` receives an email and a password. Add no logging here.

## Risk

Medium — the highest of the "moves cleanly" tranches, entirely because of the `BaseLoginEndpoint`
overlap. The move itself is mechanical; the de-duplication is the judgement call.
