# Review: AdhdTimeOrganizer/config/DatabaseStringsHelper.cs
Role: config
Summary: Thin, correct wrapper around `Helper.GetDatabaseConnectionString` for the default and log DBs; no secrets are logged or embedded in exceptions from this file itself, but it inherits a verbose-error setting that can leak data downstream.
Coverage: n/a

## Issues
- [Medium][Security] AdhdTimeOrganizer/config/DatabaseStringsHelper.cs:7,10 — both connection strings are built via `Helper.GetDatabaseConnectionString`, which hardcodes `Include Error Detail=true` on the Npgsql connection string; this makes Npgsql include actual parameter values (potentially user PII) in exception messages raised for query failures.
  Why: Since this app has no PII redaction wired into Serilog (per CLAUDE.md, `PiiRedactor` exists but isn't used) and no audit interceptor active, any unhandled exception that gets logged with `Include Error Detail=true` could leak user data straight into log files, which is a GDPR Art. 32 concern — same class of risk CLAUDE.md flags for free-text logging.
  Fix: Consider disabling `Include Error Detail` in production (or gating it behind `IsDevelopment()`), since this file has no way to override it per-environment today.
  Confidence: Med

- [Low][Quality] AdhdTimeOrganizer/config/DatabaseStringsHelper.cs:10 — `GetLogDatabaseConnectionString` hardcodes `"log_db"` as the database name with no override parameter, unlike the default connection string which lets `DB_NAME` drive naming; minor inconsistency but not a bug since the log DB name isn't expected to vary.
  Why: Slight asymmetry in the two properties' flexibility; harmless today but a future multi-tenant/multi-env split would need a code change here rather than an env var.
  Fix: Optionally source the log DB name from an env var (e.g., `LOG_DB_NAME`) for symmetry with the default connection, though not urgent.
  Confidence: Low

- [Nit][Convention] AdhdTimeOrganizer/config/DatabaseStringsHelper.cs:7 — blank line at line 8 between the two one-line properties is stray whitespace with no grouping purpose.
  Why: Purely cosmetic.
  Fix: Remove the extra blank line.
  Confidence: Low
