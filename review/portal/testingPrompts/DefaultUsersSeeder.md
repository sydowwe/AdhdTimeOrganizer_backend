# TEST-17 — Tests for `DefaultUsersSeeder`

## Context you need (do not rediscover)

**Repo:** `C:/Users/jakub/RiderProjects/AdhdTimeOrganizer`
**Under test:** `AdhdTimeOrganizer/infrastructure/persistence/seeder/default/DefaultUsersSeeder.cs`
— an `IAppWideDefaultSeeder` (`Seed(bool overrideData)`), upserting the root admin from env vars
(`ROOT_ADMIN_EMAIL`, `ROOT_ADMIN_PASSWORD` via `Helper.GetEnvVar`, which **throws** when unset).

**Important:** per CLAUDE.md, `Program.SeedDatabase`'s four ordered passes are **all commented out**,
so nothing seeds on startup today. This seeder must therefore be invoked **directly** in the test —
resolve it (or `IAppWideDefaultSeederManager`) from the host provider and call it. Do not expect the
app boot to have run it.

**Test project:** `AdhdTimeOrganizer.IntegrationTests`, new file `Seeding/DefaultUsersSeederTests.cs`
next to the existing `Seeding/PerUserDefaultMatcherTests.cs`. xunit v3, FluentAssertions,
`[Collection("Postgres")]`, `CreateDbContext()`, `SeedAsync(db)` override. You will need
`UserManager<User>` from the provider to assert role membership.

## Rules this seeder must obey (CLAUDE.md → Seeding)

- **Default seeders upsert; they never truncate.** `overrideData: true` means "update existing rows
  in place", never "wipe and re-insert". Truncating `user` / `user_role` cascades away every
  user↔role assignment.
- Role names come from `UserRoleEnum` — never hard-coded strings.
- Default seeder managers let exceptions **propagate** (dev managers log and continue).

## Scenarios to write

### A. `CQ-1` — a failed `CreateAsync` must stop the seeder (should FAIL today)

When `userManager.CreateAsync` fails, the code logs but **does not return** — it proceeds to
`AddToRoleAsync(adminUser, "Root")` and `userDefaultsService.CreateDefaultsAsync(adminUser.Id)` with
`adminUser.Id` still at its default `0`. The `existingAdmin` branch already returns correctly.

1. Force `CreateAsync` to fail deterministically. The cleanest lever is the password policy from
   `config/IdentityServiceExtensions.cs` (`RequiredLength = 8` plus complexity rules) — set
   `ROOT_ADMIN_PASSWORD` to something that violates it. A duplicate-email collision works too: seed
   a user with `ROOT_ADMIN_EMAIL` already taken by a different username.
2. Run the seeder.
3. Assert **no** `user_role` row exists for user id `0` (or for any nonexistent user id), and that
   no per-user default rows were created for id `0`.
4. Assert the failure is visible — either a thrown exception or a logged error — rather than a
   silent success.

Then the same for the `overrideData: true` path: `CQ-30` notes that branch sits **outside** the
try/catch covering the create path and discards `IdentityResult.Succeeded` from both `UpdateAsync`
and `ResetPasswordAsync`. Assert a failed password reset does not silently report success.

### B. Idempotency — the core upsert contract

1. Run `Seed(overrideData: false)` twice.
2. Assert exactly **one** root-admin user exists, with exactly **one** `Root` role assignment.
3. Assert per-user defaults were not duplicated (count rows for the seeded lookup tables before and
   after the second run).

### C. `overrideData: true` updates in place, and destroys nothing

1. Seed once. Capture the admin's `Id`.
2. Change `ROOT_ADMIN_EMAIL`-adjacent fields (locale, timezone) and run with `overrideData: true`.
3. Assert the **same** `Id` (no delete-and-reinsert), updated fields, and — critically — that the
   `Root` role assignment **survives**. This is the regression that CLAUDE.md's truncation warning
   exists for.
4. Seed a second, unrelated user with rows in a user-keyed table; run with `overrideData: true`;
   assert that user and their rows are untouched.

`CQ-31` notes `HasExtensionAccess` is *not* refreshed on the override path while other fields are.
Write a test asserting whichever behavior is intended and record the decision.

### D. `CQ-29` — role name must come from `UserRoleEnum`

The seeder hard-codes `"Root"`. A typo would silently produce a rootless admin with no compile-time
signal. Assert the created admin is in `nameof(UserRoleEnum.Root)` — resolving the name from the
enum in the **test**, so a future rename breaks the test rather than the behavior.

### E. Missing configuration

`Helper.GetEnvVar` throws `EnvironmentVariableMissingException` when a var is unset. Run the seeder
with `ROOT_ADMIN_PASSWORD` absent and assert the failure is clean and diagnosable — not a partially
created user. Note the related `CQ-22`: `SeedUserIdProvider.GetRootAdminUserIdAsync` has the same
throwing dependency on `ROOT_ADMIN_EMAIL`, while its caller only handles the null case; a test here
that also exercises `SeedUserIdProvider` with the var unset is worth adding.

### F. `SEC-10` — no email in logs

Force a duplicate-email failure and inspect what is logged. ASP.NET Identity's duplicate errors embed
the offending value (`"UserName 'x@y.com' is already taken"`), and the seeder logs
`IdentityResult` descriptions. Assert the captured log output contains no raw email address.
Capture logs with an `ILoggerProvider` test double registered into the host.

### G. `EmailConfirmed` is deliberately true

The seeder sets `EmailConfirmed = true` for the root admin. That is the expected shape for a
system-seeded account, **not** a general confirmation bypass. Assert it, so the intent is pinned and
nobody "fixes" it — and add the negative: a normally registered user must **not** get
`EmailConfirmed = true` (that path is `Auth/RegistrationTests.cs`; cross-check rather than duplicate).

## Conventions

- Manipulate env vars per-test in a way that cannot leak across the shared collection — set and
  restore in a `finally`, or drive config through the test host's configuration rather than the
  process environment if `Helper.GetEnvVar` allows it. Check first; leaking `ROOT_ADMIN_*` across
  tests in `[Collection("Postgres")]` will cause confusing cross-test failures.
- Assert from a fresh `CreateDbContext()`.
- Scenario **A** is expected to fail against current `main`. Tag `[Trait("Status","KnownGap")]`
  referencing `CQ-1`; remove when fixed.
- Never assert on a plaintext password value.
