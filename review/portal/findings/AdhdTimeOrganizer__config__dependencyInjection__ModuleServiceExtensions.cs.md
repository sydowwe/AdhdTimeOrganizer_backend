# Review: AdhdTimeOrganizer/config/dependencyInjection/ModuleServiceExtensions.cs
Role: config
Summary: Correctly implements the documented module-wiring contract (explicit ModuleAssemblies, DbContext alias, hand-closed generic NotificationService, name-registered Scheduler XLSX export) with accurate, load-bearing comments.
Coverage: n/a

## Issues
- [Low][Quality] AdhdTimeOrganizer/config/dependencyInjection/ModuleServiceExtensions.cs:56 — `services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>())` silently relies on `AddDbContext<AppDbContext>` having been called earlier in the composition root; nothing in this file enforces or asserts that ordering.
  Why: if a future refactor reorders `Program.cs` registrations, the ~34 module services that depend on plain `DbContext` fail at first resolution with a runtime DI error rather than a startup-time assertion, and the failure is far from this file.
  Fix: add a one-line comment noting the dependency on `AddDbContext<AppDbContext>` running first, or assert via `services.Any(...)` in debug builds — optional, since `ModuleWiringTests` per CLAUDE.md should catch this in practice.
  Confidence: Low

- [Low][Convention] AdhdTimeOrganizer/config/dependencyInjection/ModuleServiceExtensions.cs:82-88 — `INotificationPayloadEnricher` and the `NotificationService<User>` closure carry no lifetime marker by design (per the code comments), so a reader unfamiliar with the convention could mistake this for an oversight and "fix" it by adding a marker to the module type, which would double-register it once picked up by the `AppDomain` sweep.
  Why: the double-registration failure mode described at the top of the file (duplicate `IEnumerable<T>` resolution) is exactly what a well-intentioned marker addition here would trigger for these two services.
  Fix: none needed now; the comments already explain why, this is just a fragility note for future maintainers — no code change required.
  Confidence: Low

No other issues found — the file faithfully implements the Composition Root rules in CLAUDE.md (ModuleAssemblies list, DbContext→AppDbContext alias, hand-closed generic-over-TUser services, AddSchedulerXlsxExport registered by name and correctly excluded from ModuleAssemblies). IQuietHoursReader resolution is not touched by this file, consistent with CLAUDE.md describing it as resolved via the marker scan on Notifications' `QuietHoursReader`, not here.
