# Review: AdhdTimeOrganizer/infrastructure/extService/googleCalendar/GoogleCalendarService.cs
Role: other (external service integration)
Summary: Functional Google OAuth/Calendar wrapper, but the long-lived refresh token it hands back is persisted in plaintext (`User.GoogleCalendarRefreshToken`), and the service has no error handling around Google API failures.
Coverage: n/a

## Issues
- [High][Security] domain/model/entity/user/User.cs:21 (consumed by GoogleCalendarService.cs:41-47) — `GoogleCalendarRefreshToken` is stored as a plain `varchar(500)` column (see `UserEntityConfiguration.cs:16`), not wrapped in Framework's `EncryptedColumn`, even though CLAUDE.md flags `EncryptedColumn` as the designated mechanism for exactly this kind of high-sensitivity secret and notes it is "currently unused by any entity."
  Why: a Google OAuth refresh token grants standing access to the user's Google Calendar; a DB dump, backup leak, or SQL-injection read exposes it directly, and it is a long-lived credential (doesn't expire like an access token) so the blast radius of a leak is large and durable.
  Fix: wrap the column with `builder.EncryptedColumn(u => u.GoogleCalendarRefreshToken)` in `UserEntityConfiguration.Configure` and add a migration; note `EncryptedColumn` makes the field non-filterable/non-unique, which is fine here since it's only ever read by user id.
  Confidence: High

- [Medium][Quality] GoogleCalendarService.cs:41-47,49-58 — no try/catch around `flow.ExchangeCodeForTokenAsync` or the returned `CalendarService`'s calls (the latter's exceptions surface from callers like `SyncCalendarToGoogleEndpoint`); a Google API error (invalid/revoked code, `invalid_grant`, network failure, rate limit) throws an unhandled `TokenResponseException`/`GoogleApiException` that propagates as a raw 500 instead of a mapped Result/error response.
  Why: callers (e.g. `ConnectGoogleCalendarEndpoint`, `SyncCalendarToGoogleEndpoint`) get no structured failure signal for expected external-service failure modes (revoked consent, expired refresh token), so the UI can't distinguish "reconnect needed" from a genuine bug, and unhandled exception middleware may leak Google's raw error payload.
  Fix: catch `TokenResponseException`/`GoogleApiException` in this service (or wrap calls in the callers) and translate to a domain Result/error, especially to detect `invalid_grant` and prompt the user to reconnect.
  Confidence: Med

- [Low][Security] GoogleCalendarService.cs:41-47 — `ExchangeCodeForRefreshToken` swallows the full `TokenResponse` and returns only `RefreshToken`; if this method (or a future logging addition, e.g. via an HTTP logging handler on the Google client) ever logs the token response, the refresh token would land in logs. Currently no logging call exists in this file, so this is a latent risk rather than an active leak.
  Why: refresh tokens in logs are a durable secret leak per the CLAUDE.md logging PII rule (extended to secrets).
  Fix: if diagnostics are ever added here, redact/omit the token value explicitly; don't log `tokenResponse` as a whole object.
  Confidence: Low

- [Low][Quality] GoogleCalendarService.cs:49-58 — `GetCalendarService` constructs a new `GoogleAuthorizationCodeFlow` and `CalendarService` (and therefore a new underlying `HttpClient`) on every call, with no disposal (`CalendarService`/`UserCredential` are `IDisposable`/wrap an `HttpClient`); this is a singleton service (`ISingletonService`) so these objects are created per-request for the lifetime of the app with nothing pooling or reusing the client.
  Why: under load this risks socket exhaustion / unnecessary TLS handshake overhead per Google Calendar sync call, and undisposed `HttpClient`-wrapping objects are a minor resource leak.
  Fix: consider caching `GoogleAuthorizationCodeFlow` per client config (it has no per-user state) and/or disposing the returned `CalendarService` in the caller with `using`.
  Confidence: Med

- [Low][Security] GoogleCalendarService.cs:26-39 — `GetAuthUrl` builds the OAuth URL without a `state` parameter.
  Why: omitting `state` weakens CSRF protection on the OAuth authorization-code flow (an attacker could trick a victim into completing a code exchange bound to the attacker's session, though impact here is limited since the code is exchanged server-side against the authenticated user's own session in `ConnectGoogleCalendarEndpoint`).
  Fix: generate and validate a per-session `state` value round-tripped through the redirect.
  Confidence: Low
