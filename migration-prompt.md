# Prompt: move the remaining user/auth endpoints into `Sydowwe.Framework`

Hand this to a fresh session. Read `CLAUDE.md` first (especially **FastEndpoints Base Classes** →
**Auth**), then `auth-state.md`.

---

## The task

`AdhdTimeOrganizer/application/endpoint/user/` holds 33 endpoints. Seven already sit on a
`Sydowwe.Framework` abstract base and are thin `Configure`-less subclasses. The other 26 are
standalone `Endpoint<…>` classes in the portal, and most of them are not portal-specific in any way —
they are the same login/2FA/email-confirmation/session plumbing every solution on this framework
needs.

Work out which of those 26 should become `Base…Endpoint` in Framework with a thin concrete wrapper in
the portal, which should stay portal-only, and what has to move alongside them (DTOs, services,
validators, options) for the bases to compile in an assembly that has never heard of `User` or
`AppDbContext`.

**Deliver a plan before code.** Tranches, ordered, with the moves each tranche implies and its risk.
Then implement it tranche by tranche, building and running the auth tests after each.

## Ground truth (verified 2026-07-30 — re-verify, don't trust)

**Already on a Framework base** (the pattern to copy): `LoginUserEndpoint`, `LogoutEndpoint`,
`RefreshTokenEndpoint`, `ChangePasswordEndpoint`, `ValidateTwoFactorAuthForLoginWebEndpoint`,
`ValidateTwoFactorAuthForLoginExtensionEndpoint`, `GetUserDataEndpoint`.

**Framework already owns these dependencies** — an endpoint using only these is a cheap move:
`IJwtService` / `IJwtService<TUser>`, `IRefreshTokenService`, `ITwoFactorAuthService<TUser>`,
`IUserEmailSenderService<TUser>`, `IGoogleRecaptchaService`, `IAuditService`, `TwoFactorOptions`,
`VerifyUserPreProcessor<TUser, TRequest>`, `AuthCookies`, `UserManager<TUser>`/`SignInManager<TUser>`
where `TUser : BaseUser`, and the `dto/request/user/` + `dto/response/user/` sets.

**Portal-only dependencies** — an endpoint touching one of these needs a decision, not a move:
`AppDbContext`, `IUserDefaultsService`, `IGoogleSignInService`, `IGoogleCalendarService`, the portal
`User` entity's own columns, and the portal DTOs in `application/dto/request/user/` +
`dto/response/user/`.

**Rough grouping of the 26** (yours to confirm):

| Group | Endpoints | First read |
|---|---|---|
| Sessions | `LogoutAll`, `RevokeSession`, `RevokeAllOtherSessions`, `GetUserSessions` | Pure `IRefreshTokenService` + `AuthCookies.SessionHashName`. Cheapest tranche. |
| Email confirmation | `ConfirmEmail`, `ResendEmailConfirmation`, `ChangeEmail`, `ConfirmEmailChange` | Framework deps only, but portal DTOs (`ConfirmEmailRequest`, `EmailRequest`, `ChangeEmailRequest`) |
| Forgot password | `ForgotPassword`, `ResetPassword` | Framework deps; `ForgotPasswordRequest` is portal, `ResetPasswordRequest` may already be Framework's — check which is bound |
| 2FA settings | `GetTwoFactorAuthStatus`, `ToggleTwoFactorAuthentication`, `ResetTwoFactorAuth`, `RegenerateTwoFactorAuthRecoveryCodes` | Framework deps + `VerifyUserPreProcessor`; `AuthenticatorSetupResponse` is portal |
| Extension clients | `ExtensionLogin`, `ExtensionLogout`, `ExtensionRefreshToken` | Is a browser-extension client a framework concept or an ADHD-portal one? Decide before moving. |
| Registration / Google sign-in | `RegisterUser`, `GoogleSignIn` | Both touch `AppDbContext` + `IUserDefaultsService`. Hardest; needs a hook seam. |
| Google Calendar (4) | all | Almost certainly stays portal — an ADHD-organizer integration, not auth |
| Misc | `UpdateUserPreferences`, `DeleteUserAccount`, `GetUserDataExport` | Preferences/export are shaped by portal `User` columns; account deletion may generalize |

## How to decide "moves" vs "stays"

Moves if the behaviour is the same for any solution on this framework and the only portal-specific
things are the user type and the DTO shape. Stays if it encodes a product decision (which defaults a
new user gets, which integrations exist, which columns a preferences screen exposes).

When something is 90% general with a portal-specific tail, prefer a **hook**: put the flow in the
base and expose a `protected virtual` seam, the way `BaseChangePasswordEndpoint` exposes
`AfterPasswordChangedAsync` and `RevokeAllSessionsAsync`. `RegisterUserEndpoint`'s "create user, then
seed their defaults" is the obvious candidate — the create is general, `IUserDefaultsService` is not.

Don't move something just because it compiles after moving. A base with one subclass, no seams and no
second consumer is worse than leaving it where it is.

## Traps

1. **Framework is excluded from FastEndpoints discovery.** `Program.cs` sets
   `DisableAutoDiscovery = true` and pins `o.Assemblies` to the portal + three modules. Every
   endpoint you add to Framework **must be `abstract`** or it is dead code. Do not widen
   `o.Assemblies` — the comment there explains why, and three endpoints were already stranded that
   way once.
2. **`TUser` can't be inferred from a constraint.** Bases are generic over `TUser : BaseUser`; the
   portal closes them (`ChangePasswordEndpoint : BaseChangePasswordEndpoint<User>`). Non-generic is
   only an option when no user object is touched (`BaseLogoutEndpoint`, `BaseRefreshTokenEndpoint`).
3. **DTOs are the real work.** Several portal request/response types have a Framework near-twin
   (`PasswordRegistrationRequest` vs `RegistrationRequest`; check `ResetPasswordRequest`,
   `EmailRequest`). For each: move it, reuse Framework's, or make the base generic over the request
   type. **Do not create a second copy of a DTO** — the reconciliation that just finished was 153
   duplicate pairs, and this is exactly how they got there. Watch for the SPA contract: renaming a
   field breaks the frontend.
4. **Routes and auth are declared in the base's `Configure()`.** Preserve the exact route string,
   `AllowAnonymous()`, `Throttle(...)` args, `PreProcessor<…>` and `Roles(...)` of the portal
   original. A dropped `AllowAnonymous()` is not a compile error — it is a runtime 401 nobody
   notices. See `BaseLogoutEndpoint`, which carries a comment explaining exactly that, and the test
   pinning it.
5. **`ApplyUserScoping` is a no-op** on the shared read bases. Anything you move that reads per-user
   rows must scope explicitly.
6. **No PII in logs** (`CLAUDE.md` → Logging). Auth endpoints are the highest-risk place for this —
   log `{UserId}`, never the email.
7. **Name collisions.** Check the type name doesn't already exist in Framework before adding it.
   Both sweeps — filename *and* declared type name — because they disagree.

## Definition of done, per tranche

- `dotnet build AdhdTimeOrganizer.sln` clean.
- `dotnet test --filter "FullyQualifiedName~Auth"` green (26 tests as of 2026-07-30).
- Portal wrappers are `Configure`-less unless they genuinely need to override something.
- No duplicated type or DTO between the two projects.
- `CLAUDE.md`'s auth-bases table updated with every new base and its portal subclass.
- Routes unchanged — diff the Swagger route list before/after, or the SPA breaks silently.

## Questions to answer in the plan, before writing code

- Is "extension client" a Framework concept? It leaks into `IJwtService`, `IRefreshTokenService` and
  the refresh-token entity already, which suggests yes — but confirm rather than assume.
- Should `BaseSetupTwoFactorForLoginEndpoint` get its missing portal subclass as part of this work?
  It is why `TwoFactorMode.Required` is currently unusable (`auth-state.md`).
- Does anything here need `IAuditService`, given auditing is **not wired up** in this solution
  (`CLAUDE.md` → Auditing)? Don't build on it as if it records anything today.
- Are the two `docs/` copies (`framework/Sydowwe.Framework/docs/architecture.md`, root `docs/`) going to need
  updating, and is the root one still the untrusted foreign copy?
