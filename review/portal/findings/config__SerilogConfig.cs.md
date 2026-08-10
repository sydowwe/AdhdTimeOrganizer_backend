# Review: AdhdTimeOrganizer/config/SerilogConfig.cs
Role: config
Summary: Postgres log sink's catch-all `properties` JSONB column persists every enriched property — including request bodies, client IPs and user agents set by `Program.cs`'s `UseSerilogRequestLogging` — with no `PiiRedactor` wiring and no retention purge, in direct conflict with CLAUDE.md's logging rule.
Coverage: n/a

## Issues
- [Critical][Security] AdhdTimeOrganizer/config/SerilogConfig.cs:28 — `PropertiesColumnWriter(NpgsqlDbType.Jsonb)` serializes *all* Serilog properties for every event into the `properties` column, and `Program.cs`'s `EnrichDiagnosticContext` (lines ~402-420) sets `RequestBody` (raw, up to 1000 chars, for every non-GET request) via `diagnosticContext.Set`. Login/register/change-password bodies contain plaintext passwords, and other bodies carry emails/names.
  Why: Plaintext credentials and PII get durably persisted in a Postgres table in Production with no redaction — `PiiRedactor` exists in the framework but is not called anywhere in this pipeline, exactly the gap CLAUDE.md's "Logging (no PII at the call site)" section warns about. This is a GDPR Art. 5/32 exposure and a credential-leak risk if the log DB is ever breached or over-permissioned.
  Fix: Do not log request bodies at all, or redact known-sensitive routes (auth/*) before calling `diagnosticContext.Set`; if `properties` must stay, allow-list which diagnostic keys are persisted instead of writing everything.
  Confidence: High

- [High][Security] AdhdTimeOrganizer/config/SerilogConfig.cs:46-51 — `user_agent`, `client_ip`, `auth_method`, `user_id`, `role` columns are commented out, giving the impression those fields are excluded from the Postgres sink.
  Why: `PropertiesColumnWriter` still serializes those same properties (set in `Program.cs` as `UserAgent`/`ClientIP`) into the `properties` JSONB blob regardless of whether a dedicated typed column exists for them — commenting the column out only removes the convenience column, not the data. A reviewer reading this file would reasonably conclude PII columns were deliberately disabled.
  Fix: If the intent is to exclude these fields, don't just comment the column — strip them from the diagnostic context/LogContext before the sink, or note explicitly in a comment that `properties` still captures them.
  Confidence: Med

- [Medium][Compliance] AdhdTimeOrganizer/config/SerilogConfig.cs:54-71 — The Postgres `warning_logs` sink has no retention/purge policy; the file sink two lines up sets `retainedFileCountLimit: 30` but nothing analogous exists for the DB table (no `RetentionOptions`-style cutoff, no purge job).
  Why: Combined with the PII noted above, log rows accumulate indefinitely in Production, at odds with GDPR Art. 5(1)(e) data-minimization and the retention discipline CLAUDE.md documents elsewhere for other ledgers (`scheduled_job_run`, `reminder_dispatch`, etc.).
  Fix: Add a scheduled purge (or a Postgres native retention policy / partitioning by date) for `warning_logs`, mirroring the pattern in `framework/Sydowwe.Framework/infrastructure/persistence/retention/`.
  Confidence: Med

- [Medium][Quality] AdhdTimeOrganizer/config/SerilogConfig.cs:54 — Production detection uses `Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production"` instead of the standard `context.HostingEnvironment.IsProduction()` available in the `UseSerilog((context, config) => …)` callback.
  Why: Bypasses ASP.NET Core's normal environment-resolution logic (config overrides, `DOTNET_ENVIRONMENT` fallback, casing), so the Postgres sink could silently fail to activate (or activate unexpectedly) if environment is set through means other than that exact env var.
  Fix: Replace with `context.HostingEnvironment.IsProduction()`.
  Confidence: Med

- [Low][Quality] AdhdTimeOrganizer/config/SerilogConfig.cs:57,59 — Table is named `warning_logs` but the sink's minimum level is `LogEventLevel.Information`, so routine info-level traffic (including the PII noted above) is stored, not just warnings.
  Why: Misleading name for anyone auditing what's in that table or writing a retention/PII remediation policy against it.
  Fix: Rename to something level-agnostic (`app_logs`) or raise the minimum level to actually match "warning".
  Confidence: Low

- [Nit][Quality] AdhdTimeOrganizer/config/SerilogConfig.cs:55-71 — `WriteTo.PostgreSQL(...)` is called with 11 unnamed positional arguments (several `bool`/`int`/`string` in a row, e.g. `30, int.MaxValue, null, false, "command", true, true`).
  Why: Hard to review or modify safely — a reordering or an upstream package signature change would silently reassign meaning (e.g., swap `needAutoCreateSchema`/`useCopy`).
  Fix: Use named arguments for at least the boolean/positional-ambiguous parameters.
  Confidence: Low
