# Tranche 1 — Sessions (4 endpoints)

**Verdict: move. Cheapest tranche — zero design decisions, zero seams.**

| Portal endpoint | Route | New Framework base |
|---|---|---|
| `command/auth/LogoutAllEndpoint.cs` | `POST /auth/logout-all` | `BaseLogoutAllEndpoint` |
| `command/settings/RevokeSessionEndpoint.cs` | `DELETE user/sessions/{id:long:required}` | `BaseRevokeSessionEndpoint` |
| `command/settings/RevokeAllOtherSessionsEndpoint.cs` | `DELETE user/sessions/all` | `BaseRevokeAllOtherSessionsEndpoint` |
| `read/GetUserSessionsEndpoint.cs` | `GET /user/sessions` | `BaseGetUserSessionsEndpoint` |

## Why it moves

Session management is identical for any solution on this framework. The only dependencies are
already Framework's:

- `IRefreshTokenService` — `RevokeAllUserTokensAsync`, `RevokeSessionByIdAsync`,
  `RevokeAllExceptCurrentAsync`, `GetUserSessionsAsync`
- `AuthCookies.SessionHashName`
- `User.GetId()` and `HttpContext.Response.ClearSessionCookies()`

## Generic or not

**Non-generic**, all four — no user *object* is touched, only `User.GetId()`. Same shape as the
existing `BaseLogoutEndpoint` / `BaseRefreshTokenEndpoint`. This removes the `TUser` inference
problem entirely for this tranche.

## What moves alongside

- `AdhdTimeOrganizer/application/dto/response/user/UserSessionResponse.cs` — `{Id, Device, Browser,
  Ip, LastUsedAt, CreatedAt, IsCurrent}`. Nothing portal-specific.
- `AdhdTimeOrganizer/infrastructure/helpers/UserAgentParser.cs` — a pure user-agent string parser.
  Check for a name collision in Framework before adding (filename *and* declared type name sweeps).

## Preserve exactly

- No `AllowAnonymous()` on any of the four — they are authenticated endpoints.
- No `Throttle(...)` on any of the four.
- The route strings are inconsistent about the leading slash (`user/sessions/all` vs
  `/user/sessions`). **Copy them character for character** — FastEndpoints normalizes, but do not
  "tidy" them and risk finding out otherwise.
- `RevokeSessionEndpoint`'s two distinct failure codes: 404 when not found, 400 when it is the
  current session. Both are behaviour the SPA branches on.

## Risk

Lowest of all tranches. No DTO reconciliation, no seams, no `TUser`, no anonymous routes.

## Done when

- Four `Configure`-less portal subclasses.
- `UserSessionResponse` and `UserAgentParser` exist once, in Framework.
- Build clean, auth tests green, Swagger route list unchanged.
