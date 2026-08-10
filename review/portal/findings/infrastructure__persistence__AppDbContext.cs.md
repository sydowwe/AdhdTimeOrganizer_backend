# Review: AdhdTimeOrganizer/infrastructure/persistence/AppDbContext.cs
Role: config
Summary: Well-documented composition of BaseDbContext's model-building pipeline; the manual WebExtensionActivityEntry query filter bypasses the UserScopingOptions.Enabled switch that gates every other entity, and OnConfiguring duplicates logging already set up in Program.cs.
Coverage: n/a

## Issues
- [Medium][Security] AdhdTimeOrganizer/infrastructure/persistence/AppDbContext.cs:141-149 — the hand-written `WebExtensionActivityEntry` query filter is gated only on `loggedUserService != null`, not on `UserScopingOptions.Enabled` (the switch every other `IEntityWithUser` respects via `ApplyUserQueryFilters`).
  Why: if a deployment ever sets `UserScoping:Enabled = false` (the documented, supported per-deployment override an admin/HR tier would use to read across users), every other user-owned entity becomes unscoped but `WebExtensionActivityEntry` stays silently user-filtered — the single feature flag this codebase's docs treat as the one source of truth for scoping no longer governs this entity, and the inconsistency isn't visible anywhere near the `Enabled` config.
  Fix: read `scopingOptions?.Enabled` (the same value `ApplyUserScopingIfEnabled` computes) before adding the `UserId` half of the combined filter, e.g. pass it down from `OnModelCreating` alongside `loggedUserService`, or drop the manual user check when `Enabled` is false and rely on `!IsAuthenticated || …` becoming a no-op via config instead of code.
  Confidence: Medium

- [Low][Quality] AdhdTimeOrganizer/infrastructure/persistence/AppDbContext.cs:157-160 — `OnConfiguring` unconditionally calls `optionsBuilder.LogTo(Console.WriteLine, LogLevel.Information)`, duplicating the `LogTo(Console.WriteLine)` already wired in `Program.cs`'s `AddDbContext` callback.
  Why: because options are already configured via DI (constructor-supplied `DbContextOptions<AppDbContext>`), this `LogTo` call is additive, not a replacement, so every SQL command gets logged twice to the console at `Information` level in every environment including production — extra I/O per query and noisy/duplicated logs, and it bypasses the Serilog pipeline entirely (per CLAUDE.md, nothing here is PII-redacted either way, but at minimum it's dead weight).
  Fix: drop the override (Program.cs already configures logging) or make it conditional on `Environment.IsDevelopment()` and delete the duplicate `.LogTo` in Program.cs.
  Confidence: Medium

- [Nit][Quality] AdhdTimeOrganizer/infrastructure/persistence/AppDbContext.cs:156 — leftover tutorial-style comment `// In your DbContext configuration` above `OnConfiguring`.
  Why: reads as copy-pasted boilerplate, adds no information.
  Fix: remove it.
  Confidence: High

No other issues found — `ApplyHostConfigurations` ordering, the `ApplyFrameworkConfigurations` audit-log ignore/BusinessAuditLog mapping, `ConfigureRefreshTokenUserFk` no-op, and `UserScopingExcludedTypes` all match the documented design and are consistent with `BaseDbContext<TUser>` and `UserDbContextExtensions.ApplyUserQueryFilters`.
