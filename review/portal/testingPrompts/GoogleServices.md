# TEST-8 / TEST-19 — Google sign-in + Google Calendar coverage

## Context

Same infra as the other portal integration tests: `PostgresTestBase`, `CreateUserRoleClient()` /
`CreateFactory(TestRoles.X)`. These endpoints live in the host project (`AdhdTimeOrganizer`), not a
slice: `application/endpoint/user/command/auth/GoogleSignInEndpoint.cs` and
`application/endpoint/user/command/googleCalendar/{Connect,Disconnect,GetGoogleCalendarAuthUrl,
GetGoogleCalendarStatus}Endpoint.cs`, backed by
`infrastructure/extService/user/GoogleSignInService.cs` and
`infrastructure/extService/GoogleCalendarService.cs` behind `domain/extServiceContract/
{IGoogleSignInService,IGoogleCalendarService}.cs`.

Both services almost certainly call out to Google's OAuth/Calendar APIs — check whether the DI
container already has a fake/mock registered for tests (search `TestWebApplicationFactory` /
`Program.cs` test overrides for `IGoogleSignInService` / `IGoogleCalendarService`) before writing
anything; if no fake exists, add one via a Moq-based service substitution registered only in the test
factory, mirroring however other external-service seams in this project are already faked (check
`Modules/ModuleWiringTests.cs` or similar for the pattern this codebase already uses for external
services).

## What exists today

`Endpoints/SyncCalendarToGoogleTests.cs` has 3 facts, described as "partial" by the prior review pass —
read it first to see exactly what it covers before adding overlapping cases.

## What to write

Extend or add alongside `SyncCalendarToGoogleTests.cs`:
- `GoogleSignInEndpoint`: new-user vs existing-user linking, and what happens when the Google token
  validation fails (should not silently create/link an account).
- `ConnectGoogleCalendarEndpoint` / `DisconnectGoogleCalendarEndpoint`: auth (must require a logged-in
  user, must not let one user disconnect another's calendar link), and the connect/disconnect round
  trip actually flips `GetGoogleCalendarStatusEndpoint`'s reported state.
- `GetGoogleCalendarAuthUrlEndpoint`: returns a URL, doesn't leak secrets (e.g. client secret) into the
  response.
- Failure-injection: what happens when `IGoogleCalendarService`'s call to Google fails mid-sync — does
  it partially write, log-and-swallow, or surface an error to the caller? This project's stated pattern
  (see `CLAUDE.md` "Silent failures") is that cross-cutting failures often fail silently — verify this
  one behaves the way you'd want it to, not just the way it happens to.

## Out of scope

Don't build real Google API integration tests — fake the external service boundary. Don't touch
2FA/password auth endpoints; this is Google-specific only.
