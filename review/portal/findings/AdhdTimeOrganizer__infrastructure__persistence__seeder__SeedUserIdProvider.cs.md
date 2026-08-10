# Review: AdhdTimeOrganizer/infrastructure/persistence/seeder/SeedUserIdProvider.cs
Role: other
Summary: Small, correct host-side implementation of the two seed-user seams; AsNoTracking used throughout and no query filter on `User` interferes, but the root-admin lookup can throw instead of returning null on a plausible misconfiguration.

## Issues
- [Medium][Quality] SeedUserIdProvider.cs:40 — `Helper.GetEnvVar("ROOT_ADMIN_EMAIL")` throws `EnvironmentVariableMissingException` if the var is unset, but the sole caller (`PerUserDevSeederManager.SeedAllForRootAdminAsync`) only branches on `rootAdminUserId.HasValue` being false and logs-and-skips for that case; a missing env var is a different failure mode that isn't caught around this call and will crash dev seeding instead of the intended graceful "no root admin yet" skip.
  Why: A misconfigured `.env` (missing `ROOT_ADMIN_EMAIL`) turns what the caller's comment describes as an expected, recoverable state ("fresh database... simply nobody to hang fixtures off") into an unhandled exception during boot/dev-seeding.
  Fix: Either read the env var defensively here (e.g. `Environment.GetEnvironmentVariable` and return null/no rows when absent) so "not configured" and "not seeded yet" both resolve to `null`, or have `PerUserDevSeederManager` explicitly catch/log the missing-config case separately from the null case.
  Confidence: Medium

- [Low][Quality] SeedUserIdProvider.cs:44 — `GetRootAdminUserIdAsync` doesn't guard against `rootAdminEmail` matching multiple rows (e.g. a duplicate `UserName`); `FirstOrDefaultAsync` will silently pick one rather than surfacing an anomaly.
  Why: `UserName` isn't guaranteed unique at this layer independent of Identity's own constraints, so if that invariant were ever violated, seeding would silently target the wrong user rather than failing loudly.
  Fix: Not worth extra code given Identity enforces unique usernames elsewhere; noted only as a minor confidence gap, no action needed.
  Confidence: Low

No confirmed issues with global query filters or `IgnoreQueryFilters` — `User` carries no `HasQueryFilter` in `AppDbContext` (only the two partitioned activity-entry filters do), so `dbContext.Users` here correctly sees all rows unfiltered without needing `IgnoreQueryFilters`.
