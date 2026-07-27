# Tranche 3 — Forgot / reset password (2 endpoints)

**Verdict: move. One seam needed.**

| Portal endpoint | Route | New Framework base |
|---|---|---|
| `command/auth/forgotPassword/ForgotPasswordEndpoint.cs` | `POST /auth/forgotten-password` | `BaseForgotPasswordEndpoint<TUser>` |
| `command/auth/forgotPassword/ResetPasswordEndpoint.cs` | `POST /auth/reset-password` | `BaseResetPasswordEndpoint<TUser>` |

## Why it moves

Deps are all Framework's: `UserManager<TUser>`, `IUserEmailSenderService<TUser>`,
`IGoogleRecaptchaService`, `IRefreshTokenService`, `TrustedIpMiddleware`.

**`ResetPasswordEndpoint` is a pure lift** — its request type `ResetPasswordRequest` is *already*
Framework's (`Sydowwe.Framework/application/dto/request/user/ResetPasswordRequest.cs`, bound in the
portal via `using Sydowwe.Framework.application.dto.request.user`). Nothing to move but the endpoint.

## The one seam

`ForgotPasswordEndpoint` builds the reset link itself:

```csharp
var pageUrl = configuration["PAGE_URL"] ?? throw new InvalidOperationException("PAGE_URL not configured");
var resetLink = $"{pageUrl}/reset-password?userId={user.Id}&token={Uri.EscapeDataString(token)}";
```

`/reset-password?userId=…&token=…` is a **SPA route**, i.e. a product decision — a different solution
on this framework has a different frontend path. Put the flow in the base and expose:

```csharp
protected virtual string BuildResetLink(TUser user, string token);
```

with the current string as the default implementation, still reading `PAGE_URL`. That keeps the
portal wrapper `Configure`-less and override-less today while making the base genuinely reusable.

## What moves alongside

- `AdhdTimeOrganizer/application/dto/request/user/ForgotPasswordRequest.cs` — `{Email,
  RecaptchaToken}`. Generic; move it. Check Framework has no `ForgotPasswordRequest` already.

## Preserve exactly

- Both: `AllowAnonymous()`.
- `ForgotPassword`: `Throttle(3, 60, TrustedIpMiddleware.ClientIpHeaderName)`.
- `ResetPassword`: `Throttle(5, 60, TrustedIpMiddleware.ClientIpHeaderName)`.

## Behaviour that must survive the move

- **The two endpoints deliberately differ on reCAPTCHA failure.** `ForgotPassword` returns 204 (it
  must not reveal anything — same response as "no such user"); `ResetPassword` returns a 400 with an
  error. That asymmetry is intentional, not an inconsistency to fix.
- `ForgotPassword` returns 204 for an unknown user *and* for an unconfirmed one — anti-enumeration.
- `ResetPassword` revokes all refresh tokens after a successful reset. Keep it.
- **PII**: `SendPasswordResetLinkAsync(user, user.Email!, resetLink)` — the email is a parameter, not
  a log. Do not log it.

## Risk

Low. The seam is the only judgement call, and it is a strict superset of current behaviour.
