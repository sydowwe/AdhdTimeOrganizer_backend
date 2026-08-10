# Review: AdhdTimeOrganizer/infrastructure/persistence/seeder/default/DefaultUsersSeeder.cs
Role: other (IAppWideDefaultSeeder)
Summary: Upserts the root admin from env-sourced credentials (no hard-coded password, no truncation) but has inconsistent error handling and a hard-coded role string.
Coverage: n/a

## Issues
- [High][Security] DefaultUsersSeeder.cs:56-66 — On create, if `userManager.CreateAsync` fails (`!result.Succeeded`), the code logs the error but does not `return`; it falls through to `AddToRoleAsync(adminUser, "Root")` and `userDefaultsService.CreateDefaultsAsync(adminUser.Id)` using an `adminUser` whose `Id` is still the default (0/unset) because the insert never happened.
  Why: role assignment and default-data creation run against a non-existent/wrong user id, producing confusing partial state (e.g. a "Root" role row pointing at user id 0, or an exception swallowed by the outer catch) instead of a clean, diagnosable failure.
  Fix: `return;` immediately after logging when `!result.Succeeded` (same pattern already used in the `existingAdmin` branch).
  Confidence: High

- [Medium][Convention] DefaultUsersSeeder.cs:59 — Role name is the string literal `"Root"` instead of `nameof(UserRoleEnum.Root)` / a shared constant.
  Why: CLAUDE.md is explicit that role names live in `UserRoleEnum` and "never hard-code role strings" — every other seeder/test in this codebase uses `nameof(UserRoleEnum.*)`; a typo here would silently fail to match the seeded `Role.Name` and produce a rootless admin account with no compile-time signal.
  Fix: `await userManager.AddToRoleAsync(adminUser, nameof(UserRoleEnum.Root));`
  Confidence: High

- [Medium][Quality] DefaultUsersSeeder.cs:34-46 — The `overrideData` branch (email/locale/timezone update + password reset) is not wrapped in the `try/catch` that covers the create path (53-71), and both `userManager.UpdateAsync` and `ResetPasswordAsync` results are discarded without checking `.Succeeded`.
  Why: an env var missing (`Helper.GetEnvVar` throws `EnvironmentVariableMissingException`) or a password-policy rejection during override crashes seeding unhandled in one path but is silently logged-and-swallowed in the other — inconsistent failure behavior for the same seeder, and a failed `ResetPasswordAsync` is never surfaced.
  Fix: wrap the override branch in the same try/catch (or extract a shared error-handling helper) and check `IdentityResult.Succeeded` for `UpdateAsync`/`ResetPasswordAsync` before returning.
  Confidence: Med

- [Low][Security] DefaultUsersSeeder.cs:49, 57, 61, 66, 70 — `logger.LogInformation`/`LogError` calls only log generic messages or `IdentityResult.ToString()`/error message, not `adminUser.Email` directly, but `IdentityResult` failure descriptions for duplicate-username/duplicate-email errors from ASP.NET Identity typically embed the offending value (e.g. "UserName 'x@y.com' is already taken").
  Why: this could put the root admin's email into application logs, which CLAUDE.md flags as a GDPR-relevant leak ("never put ... email into a log message").
  Fix: log a fixed identifier (e.g. `adminUser.Id` once known, or just "root admin") instead of the raw `IdentityResult` description, or redact before logging.
  Confidence: Low

- [Nit][Quality] DefaultUsersSeeder.cs:36-39 — Override path updates `Email`/`Locale`/`Timezone`/`EmailConfirmed` but not `HasExtensionAccess`, so a later change to the seeded default won't propagate to an already-existing admin row on `overrideData: true`.
  Why: minor inconsistency between the "these are the current admin defaults" intent and what actually gets refreshed.
  Fix: include `existingAdmin.HasExtensionAccess = adminUser.HasExtensionAccess;` alongside the other field copies.
  Confidence: Low

Note: no truncation of `user`/`user_role` here — this seeder correctly upserts per CLAUDE.md's default-seeder rule. `EmailConfirmed = true` bypasses confirmation but that is the expected shape for a system-seeded root admin, not a general bypass. Password comes from `ROOT_ADMIN_PASSWORD` env var (not hard-coded), consistent with the rest of the codebase's secret handling.
