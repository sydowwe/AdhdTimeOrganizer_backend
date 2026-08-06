# Tranche 6 — Registration & account deletion (2 endpoints) — **DONE (2026-07-31)**

| Portal endpoint | Route | New Framework base |
|---|---|---|
| `command/settings/DeleteUserAccountEndpoint.cs` | `DELETE user/account` | `BaseDeleteUserAccountEndpoint<TUser>` |
| `command/auth/passwordAuth/RegisterUserEndpoint.cs` | `POST /auth/register` | `BaseRegisterUserEndpoint<TUser, TRequest>` |

Both portal endpoints are now empty subclasses. What was actually built, and the four decisions that
differ from the plan below, are recorded here so they are not re-litigated.

## What landed

- `framework/Sydowwe.Framework/application/service/auth/UserRegistrationFlow.cs` — **the real deliverable.**
  Identity insert → `User` role → optional in-transaction step → `IUserDefaultsService` → commit, in
  one transaction, with `UserRegistrationResult.StatusCode` owning the 409/400/500 mapping.
- `framework/Sydowwe.Framework/application/endpoint/user/command/auth/BaseRegisterUserEndpoint.cs` — captcha,
  duplicate pre-check, the flow (2FA provisioning passed as the in-transaction step), confirmation
  email after commit. Hook: `AfterUserCreatedAsync`.
- `framework/Sydowwe.Framework/application/endpoint/user/command/settings/BaseDeleteUserAccountEndpoint.cs` —
  hooks `BeforeDeleteAsync(user)` / `AfterDeleteAsync(userId)`, both no-ops today.
- `framework/Sydowwe.Framework/application/dto/request/user/BasePasswordRegistrationRequest.cs` —
  `RegistrationRequest` + `Password` + `RecaptchaToken` + abstract `ToEntity`.
- `AdhdTimeOrganizer.IntegrationTests/Auth/RegistrationTests.cs` — first coverage this route has had.

## Decisions taken (differing from the analysis below)

1. **The shared piece is a flow service, not a base-class body.** `GoogleSignInEndpoint.Register()`
   was byte-for-byte the same sequence. Google sign-in **stays portal** (see `stays-portal.md` — the
   `Google.Apis.Auth` supply-chain cost is unchanged by this), but it now calls
   `UserRegistrationFlow.RunAsync` instead of holding a second copy. Any future provider gets it free.
   Its one genuine difference is preserved: a duplicate is reported as "Could not sign in with
   Google." rather than "User already exists", so the route does not confirm an address to a prober.
2. **Seam 2 was already solved.** `ModuleServiceExtensions.cs` has aliased `AppDbContext` as
   `DbContext` since the modules needed it, so injecting `DbContext` cost zero new lines. The
   `IRegistrationTransaction` and `InTransactionAsync` alternatives are dead — do not revive them.
3. **Seam 1 dissolved.** `IUserDefaultsService` moved to Framework with the Google revert, so the
   flow calls the contract directly rather than through a hook. `AfterUserCreatedAsync` survives for
   what defaults are *not* (analytics, welcome side effects) and runs inside the transaction.
4. **The DTO is a base record, not an interface.** `PasswordRegistrationRequest` already derived from
   `RegistrationRequest`, so there was no field diff to reconcile and no SPA break. `RecaptchaToken`
   sits on the Framework base deliberately: every host on this framework gets a captcha on the
   anonymous sign-up route, rather than each opting in.

Preserved exactly: `AllowAnonymous()`, `Throttle(5, 60, TrustedIpMiddleware.ClientIpHeaderName)`,
both routes, the delete endpoint's `VerifyUserPreProcessor` gate and its irreversibility `Summary`,
the confirmation email going out **after** commit and only when `!EmailConfirmed`, and the status
codes (409 duplicate / 403 captcha / 400 other identity errors / 500 role · 2FA · defaults).

Fixed in passing, as agreed: the hard-coded `"User"` role string is now `nameof(UserRoleEnum.User)`
**in the moved code only** — `GoogleSignInEndpoint` inherited the fix by calling the flow.

## Test-fixture change this forced

Registration seeds a per-user `Calendar`, which trips `SuggestionPatternRefreshInterceptor` into
`REFRESH MATERIALIZED VIEW` — and the test schema (`EnsureCreated`) never had the three matviews,
because they are hand-written SQL rather than migration output. So *any* HTTP registration 500'd in
tests, before and after this tranche; the route simply had no coverage to reveal it.
`AppDbContextFixture.OnSchemaCreatedAsync` now applies
`AdhdTimeOrganizer/infrastructure/persistence/sqlScripts/*.sql`, copied to the test output by a
`Content` item in the test csproj. Note the same gap exists for anything else that writes
`PlannerTask` / `ActivityHistory` / `Calendar` through HTTP in tests — that is now unblocked too.

## Verification

`dotnet build AdhdTimeOrganizer.sln` clean; full suite **111 passed / 6 skipped / 0 failed**,
including the two new registration tests (200 + user row + `User` role assignment; 409 on a
duplicate address). Both routes confirmed live — the 409 test proves `/auth/register` is mapped
through the base's `Configure`.

---

# Original analysis (2026-07-30), kept for the reasoning

**Verdict: move, but only behind hooks. Highest-risk tranche — do it last.**

## `DeleteUserAccountEndpoint` — the easy half

Today it is already fully generic: `userManager.DeleteAsync(user)` on the verified user, then
`HttpContext.Response.ClearSessionCookies()`, relying on DB cascade for refresh tokens.

Move as-is, but **add hooks anyway**:

```csharp
protected virtual Task BeforeDeleteAsync(TUser user, CancellationToken ct);
protected virtual Task AfterDeleteAsync(long userId, CancellationToken ct);
```

Rationale: real GDPR erasure per solution needs cleanup this endpoint does not currently do —
external integrations to revoke, ledgers to anonymize, blobs to drop. A base with no seam here will
be forked by the first solution that needs one. Both default to no-op, so the portal wrapper stays
`Configure`-less today.

Preserve: `PreProcessor<VerifyUserPreProcessor<User, VerifyUserRequest>>()`, the `Delete
user/account` route, the `Summary`/`Description` (it warns the action is irreversible — that text
reaches Swagger and possibly the UI).

## `RegisterUserEndpoint` — the hard half

### What is general

reCAPTCHA verify → duplicate-email pre-check (409) → open transaction →
`userManager.CreateAsync(user, password)` (409 on `DuplicateUserName`/`DuplicateEmail`, 400
otherwise) → `AddToRoleAsync(user, "User")` → `twoFactorAuthService.SetUpTwoFactorAuth` → commit →
send confirmation email → return `TwoFactorAuthResponse`.

That is the same flow for any solution on this framework.

### What is portal

1. **`IUserDefaultsService.CreateDefaultsAsync(newUser.Id, ct)`** — which defaults a new user gets is
   the definition of a product decision. *(Superseded: the contract now lives in Framework.)*
2. **`AppDbContext`** — used only for `Database.BeginTransactionAsync(ct)`.
3. **`PasswordRegistrationRequest`** — portal, with a Framework near-twin `RegistrationRequest`.

### The three seams

**1. The defaults hook** — the obvious one, modelled on `BaseChangePasswordEndpoint`'s
`AfterPasswordChangedAsync`. It must run **inside** the transaction and before commit — the current
code rolls the user creation back if defaults fail, and that atomicity is the point.

**2. The transaction.** Framework already references EF Core, so the base can inject the `DbContext`
base type — but that needs the portal to register `AppDbContext` *as* `DbContext` in DI. Alternatives
considered: an `IRegistrationTransaction` seam, or a `protected virtual InTransactionAsync`.

**3. The request DTO.** **Do not copy** — either make the base generic over the request type, or
reconcile the two types into one. Diff them field by field first; a renamed field is a silent SPA
break.

### Behaviour that must survive

- The confirmation email is sent **after** commit, outside the transaction, and only when
  `!user.EmailConfirmed`.
- The hard-coded `"User"` role string — a pre-existing violation; fix it as part of the move.
- **PII**: this endpoint handles an email and a password. Add no logging.

## Risk

Highest of the six. Three seams, a DTO reconciliation, a transaction-ownership decision, and the
registration path is the one endpoint where a mistake blocks all new sign-ups.
