# Tranche 2 — Email confirmation & change (4 endpoints)

**Verdict: move, generic over `TUser : BaseUser`.**

| Portal endpoint | Route | New Framework base |
|---|---|---|
| `command/auth/emailConfirmation/ConfirmEmailEndpoint.cs` | `POST /auth/confirm-email` | `BaseConfirmEmailEndpoint<TUser>` |
| `command/auth/emailConfirmation/ResendEmailConfirmationEndpoint.cs` (type: `ResendConfirmationEmailEndpoint`) | `POST /auth/resend-confirmation-email` | `BaseResendConfirmationEmailEndpoint<TUser>` |
| `command/settings/email/ChangeEmailEndpoint.cs` | `PATCH user/change-email` | `BaseChangeEmailEndpoint<TUser>` |
| `command/settings/email/ConfirmEmailChangeEndpoint.cs` | `POST /user/change-email/confirm` | `BaseConfirmEmailChangeEndpoint<TUser>` |

> Note the file/type name mismatch on the second one — the file is `ResendEmailConfirmationEndpoint.cs`
> but the class is `ResendConfirmationEmailEndpoint`. Sweep both ways when checking for collisions.

## Why it moves

Every dependency is already Framework's: `UserManager<TUser>`, `IUserEmailSenderService<TUser>`
(`SendConfirmationLinkAsync`, `SendEmailChangeConfirmationAsync`), `IRefreshTokenService`,
`IDistributedCache`, `TrustedIpMiddleware.ClientIpHeaderName`, `VerifyUserPreProcessor<TUser,
VerifyUserRequest>`. Nothing here encodes a product decision — this is the same confirmation
plumbing every solution on the framework needs.

`ChangeEmailEndpoint` calls the portal's `HttpContext.GetVerifiedUser()`. Framework already has the
generic original — `VerifiedUserAccessor.GetVerifiedUser<TUser>()` in
`framework/Sydowwe.Framework/application/preprocessor/VerifyUserPreProcessor.cs:83`. Straight swap; the portal
helper stays as the non-generic convenience wrapper it is.

## What moves alongside

All four request DTOs are shape-only, nothing portal in them:

| DTO | Notes |
|---|---|
| `ConfirmEmailRequest` | move **with** its `ConfirmEmailValidator` (same file) |
| `EmailRequest` | check Framework for an existing `EmailRequest` **and** `EmailResponse` (`dto/response/user/EmailResponse.cs` exists) before adding |
| `ConfirmEmailChangeRequest` | `{UserId, NewEmail, Token}` |
| `ChangeEmailRequest` | already derives from Framework's `VerifyUserRequest` |

## Preserve exactly

- `ConfirmEmailEndpoint`: `AllowAnonymous()`, `Throttle(30, 60, "X-Forwarded-For")` — note this one
  uses the **raw header string**, not `TrustedIpMiddleware.ClientIpHeaderName` like its siblings.
  Preserve as-is in the move; normalizing it is a separate, deliberate change.
- `ResendConfirmationEmailEndpoint`: `AllowAnonymous()`, `Throttle(3, 60,
  TrustedIpMiddleware.ClientIpHeaderName)`, plus its own per-email distributed-cache throttle.
- `ConfirmEmailChangeEndpoint`: `AllowAnonymous()`, `Throttle(5, 60, …)`.
- `ChangeEmailEndpoint`: **not** anonymous, `PreProcessor<VerifyUserPreProcessor<User,
  VerifyUserRequest>>()`.

## Behaviour that must survive the move

- **Anti-enumeration**: `ResendConfirmationEmailEndpoint` returns 204 whether or not the user exists
  or is already confirmed. Do not "improve" this into a real error.
- `ConfirmEmailChangeEndpoint` also sets the username to the new email, bumps the security stamp, and
  revokes all refresh tokens. All three are security-relevant; keep them together and in order.
- **PII**: the throttle key is built from `req.Email.ToLowerInvariant()`. That is a cache key, not a
  log — keep it out of any logging you add.

## Risk

Low. Four anonymous/throttled routes make a dropped attribute the main hazard — diff `Configure()`
line by line.
