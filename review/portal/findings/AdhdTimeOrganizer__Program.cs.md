# Review: AdhdTimeOrganizer/Program.cs
Role: config
Summary: Composition root correctly follows the CLAUDE.md contract (explicit FastEndpoints assembly list, env-over-JSON precedence, all three boot-reconciliation registrars, single interceptor registered), but request/SQL logging leaks sensitive data and a couple of small robustness gaps remain.
Coverage: n/a

## Issues
- [High][Security] AdhdTimeOrganizer/Program.cs:409-419 — `UseSerilogRequestLogging`'s `EnrichDiagnosticContext` buffers and logs up to 1000 chars of the raw request body for every non-GET request, with no field-level redaction.
  Why: This will capture plaintext passwords (login, change-password, register), refresh tokens, and other PII directly into the structured log sink, violating the CLAUDE.md "no PII in logs" rule and Art. 32; `PiiRedactor` isn't wired into Serilog either, so nothing scrubs it downstream.
  Fix: Drop the raw-body enrichment (or restrict it to an explicit allowlist of non-sensitive routes/content-types), and never log auth/password endpoint bodies.
  Confidence: High

- [Medium][Security] AdhdTimeOrganizer/Program.cs:128 — `.LogTo(Console.WriteLine)` on the `AppDbContext` is unconditional (not gated by `isDevelopment`), so every SQL statement — including parameter values, which can contain emails/names/password hashes — is written to console in production.
  Why: Console/stdout logs are typically captured by the hosting platform's log aggregator, so this is another PII leak path plus a per-query performance/allocation cost that runs in prod.
  Fix: Gate `.LogTo(...)` behind `isDevelopment`, or use `EnableSensitiveDataLogging()`-style guarding so it never runs in production.
  Confidence: Med

- [Medium][Quality] AdhdTimeOrganizer/Program.cs:259-274 — `origins` list unconditionally includes `pageUrl`, which is `null` when the `PAGE_URL` env var isn't set (`Helper.GetEnvVar` return isn't null-checked before use).
  Why: `CorsPolicyBuilder.WithOrigins` on an array containing a `null` entry can throw at startup, and if it doesn't throw, a null/empty origin string is a silent misconfiguration.
  Fix: Only add `pageUrl` to `origins` when `!string.IsNullOrEmpty(pageUrl)`, same as the extension-id check right below it.
  Confidence: Med

- [Low][Quality] AdhdTimeOrganizer/Program.cs:414 — `reader.ReadToEndAsync().Result` blocks a threadpool thread inside the Serilog enrichment delegate (which only offers a sync `Action`), on every non-GET request.
  Why: Sync-over-async here adds latency and, under load, contributes to threadpool starvation; combined with the body-logging issue above, this delegate is executing on the hot path of every write request.
  Fix: If the body enrichment is kept at all, prefer `GetAwaiter().GetResult()` (marginally cheaper) or move the body capture to a small middleware that can `await` properly; but the real fix is removing/limiting the enrichment per the finding above.
  Confidence: Low

- [Nit][Quality] AdhdTimeOrganizer/Program.cs:199-203 — Leftover `TEMP: verify manually ... remove after checking logs` trigger (`routine-reset-verify-trigger`) that fires immediately on every boot.
  Why: Dead debug scaffolding left in the composition root; if forgotten it keeps firing `RoutineTodoListResetJob` an extra time on every deploy indefinitely.
  Fix: Remove once the migration has been verified, per the comment's own instruction.
  Confidence: Low

- [Nit][Quality] AdhdTimeOrganizer/Program.cs:167-176 — `TEMP DIAGNOSTIC` removal of FastEndpoints' `ValidationSchemaProcessor` from the dev Swagger pipeline, pending an upstream decision.
  Why: Already self-flagged as needing a real decision (upgrade/report/keep disabled); left as-is it silently degrades the generated Swagger validation info for every dev.
  Fix: Track as a follow-up ticket rather than leaving the workaround inline indefinitely.
  Confidence: Low

- [Nit][Quality] AdhdTimeOrganizer/Program.cs:372-373 — `Console.WriteLine` dumping the full stack trace on `ApplicationStopping`, alongside the structured `logger.LogInformation` on the same line.
  Why: Looks like leftover debugging output; duplicates what the logger already records and adds noise to stdout.
  Fix: Remove the `Console.WriteLine` calls once the shutdown-timing issue they were added to diagnose is resolved.
  Confidence: Low
