# Review: AdhdTimeOrganizer/config/AppCommandDbContextFactory.cs
Role: config
Summary: Design-time factory correctly mirrors Program.cs's PartitionedNpgsqlMigrationsSqlGenerator replacement and pulls the connection string via env vars (no hardcoded credentials); one minor asymmetry around MigrationsAssembly.

## Issues
- [Low][Convention] AdhdTimeOrganizer/config/AppCommandDbContextFactory.cs:16 — unlike Program.cs (`b.MigrationsAssembly(typeof(AdhdTimeOrganizer.Program).Assembly.FullName)`), this factory calls `UseNpgsql` with no `MigrationsAssembly` override, relying on the EF default (the assembly containing `AppDbContext`).
  Why: `AppDbContext` is defined in the same `AdhdTimeOrganizer` assembly as `Program`, so today the default resolves to the same assembly and generated migrations land in the same place — but the two configuration paths are not textually symmetric, and a future change to where `AppDbContext` lives (or a partial class split across assemblies) would silently diverge design-time and runtime migration assembly resolution without any error.
  Fix: add the same `b.MigrationsAssembly(typeof(AdhdTimeOrganizer.Program).Assembly.FullName)` to this factory's `UseNpgsql` call for explicit parity with Program.cs.
  Confidence: Med

- [Nit][Quality] AdhdTimeOrganizer/config/AppCommandDbContextFactory.cs:20 — `ILoggedUserService` and `ILogger<AppDbContext>` are passed as `null!`.
  Why: safe today since `OnModelCreating`'s query-filter lambda only closes over `loggedUserService` (evaluated lazily per-query, not at model-build time) and the logger is only touched inside `SaveChangesAsync`, neither of which design-time tooling invokes — but it's an easy trap if either dependency is ever dereferenced eagerly in the constructor or `OnModelCreating` body.
  Fix: none needed now; if `OnModelCreating` ever starts using these services eagerly, this factory needs no-op stub implementations instead of null.
  Confidence: Low

- [Nit][Quality] AdhdTimeOrganizer/config/AppCommandDbContextFactory.cs:14 — `Env.Load()` uses `DotNetEnv`'s default relative path resolution with no explicit `.env` location or error handling if the file is absent from the current working directory when `dotnet ef` is invoked from a different directory.
  Why: could silently fall back to no env vars loaded, then fail later inside `Helper.GetDatabaseConnectionString()`/`GetEnvVar` with a less obvious error than "`.env` not found".
  Fix: not blocking — same pattern is presumably used elsewhere in the solution; only worth tightening if design-time migration commands are known to be run from non-repo-root working directories.
  Confidence: Low
