# Stays in the portal (6 endpoints)

The test is the one in `migration-prompt.md`: **moves** if the behaviour is the same for any solution
on this framework and the only portal-specific things are the user type and the DTO shape; **stays**
if it encodes a product decision. And: *a base with one subclass, no seams and no second consumer is
worse than leaving it where it is.*

## Reversed — `UpdateUserPreferencesEndpoint` moved after all

- **`UpdateUserPreferencesEndpoint` → `BaseUpdateUserPreferencesEndpoint<TUser, TRequest>`** (moved
  2026-07-30). "Which columns a preferences screen exposes is a product decision" is still true — it
  is expressed by making the base generic over the request rather than by keeping the endpoint.
  Framework owns `UserPreferencesRequest` (the four `BaseUser` fields) and
  `BaseUserPreferencesValidator<TRequest>`; the portal derives both and writes `FirstDayOfWeek` in the
  `ApplyExtraPreferences` hook. The subclass overrides `Configure` only to attach its own validator.

---

## `GoogleSignInEndpoint` — `POST /auth/login/google`

**Decided twice.** Originally listed as "stays" because `IGoogleSignInService` was a portal contract.
It was moved to Framework on 2026-07-30 and **reverted on 2026-07-31**, so the verdict below stands.
Record of what the move actually cost, so this is not re-litigated from guesses:

- The technical objections all dissolved. Only the *implementation* needs Google's SDK, so the
  contract can move alone; the `User` column becomes an `IGoogleOAuthUser` marker off `BaseUser`; the
  `Register(...)` overlap with `RegisterUserEndpoint` collapses to one seam,
  `protected abstract TUser CreateNewUser(GoogleUserInfo, GoogleSignInRequest)`, with the base filling
  in `GoogleOAuthUserId` + `EmailConfirmed` afterwards. It built clean and the full suite passed.
- Making it a *usable* provider means shipping `GoogleSignInService` too, and that is what settles it:
  `Sydowwe.Framework.csproj` then carries `Google.Apis.Auth`, whose closure is
  `Google.Apis` + `Google.Apis.Core` + `Newtonsoft.Json` — measured at **~1.1 MB across 4 DLLs**
  (Newtonsoft alone is 706 KB of it, and nothing else in Framework's closure pulls it). Startup and
  runtime cost is nil: one unused `ServiceDescriptor`, and no Google type appears in any signature so
  the assembly is not even loaded until a sign-in call.
- **The deciding cost is supply chain, not bytes.** Every solution on the framework inherits a
  dependency it must keep patched — Newtonsoft.Json especially — to enable a feature it may not offer.
  1.1 MB is cheap; a standing patch obligation for non-users is not.

Revisit when there is a *second* provider (Microsoft, Apple, …). At that point the right shape is a
separate `Sydowwe.Framework.GoogleAuth` project — an opt-in package reference rather than a hard one —
and the same work lands with no cost to solutions that skip it.

**Update 2026-07-31 (tranche 6):** the endpoint still stays portal — the supply-chain argument above
is untouched — but its `Register(...)` no longer duplicates the create-user sequence. It calls
`UserRegistrationFlow.RunAsync` (`framework/Sydowwe.Framework/application/service/auth/`), which is the shared
piece the `CreateNewUser` seam was going to buy, obtained without moving Google's SDK into Framework.
Its one behavioural difference is kept locally: a duplicate address reports "Could not sign in with
Google." rather than "User already exists", so the route does not confirm addresses to a prober.

Note `IUserDefaultsService` did **not** come back with the revert: it lives in Framework
`domain/serviceContract/` now. It has nothing to do with Google, `RegisterUserEndpoint` needs it too,
and [tranche 6](tranche-6-register-and-delete.md) was going to move it anyway.

---

## The four Google Calendar endpoints

`ConnectGoogleCalendarEndpoint`, `DisconnectGoogleCalendarEndpoint`,
`GetGoogleCalendarAuthUrlEndpoint`, `GetGoogleCalendarStatusEndpoint`

A product integration, not auth. They depend on the portal's `IGoogleCalendarService` and on
`User.GoogleCalendarRefreshToken`, a portal column. Which third-party integrations exist is the
definition of a product decision. They also sit under `endpoint/user/` mostly by filing accident —
consider relocating them out of the user folder as unrelated tidy-up, but that is not this migration.

---

## `GetUserDataExportEndpoint` — `GET /user/data-export`

Every table it reads is portal: `PlannerTasks`, `TodoLists`, `TodoListItems`, `RoutineTodoLists`,
`TaskPlannerDayTemplates`, `Calendars`, `ActivityHistories`, `WebExtensionActivityEntries`,
`DesktopActivityEntries`. The export document shape *is* the product. Even the filename is
`antiprocrastination-export-…json`.

The only general parts are the distributed-cache 1/min throttle and the
`Content-Disposition`-attachment plumbing — both a handful of lines, not worth a base. If a second
solution ever needs a GDPR export, extract *those* into a small helper, not this endpoint.