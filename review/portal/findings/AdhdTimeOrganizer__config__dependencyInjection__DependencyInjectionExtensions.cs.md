# Review: AdhdTimeOrganizer/config/dependencyInjection/DependencyInjectionExtensions.cs
Role: config
Summary: Marker-interface DI scan correctly excludes ModuleAssemblies to avoid double-registration; well-documented and matches CLAUDE.md's Composition Root contract.
Coverage: n/a

## Issues
- [Low][Quality] AdhdTimeOrganizer/config/dependencyInjection/DependencyInjectionExtensions.cs:32-37 — `ScannedAssemblies` recomputes `AppDomain.CurrentDomain.GetAssemblies()` plus a `Distinct`/`Except`/`ToArray` pipeline every time it's accessed, and `AddDependencyInjection` is presumably only called once at startup so this is cheap today, but the property has no caching if ever called twice (e.g. in tests building multiple service providers).
  Why: A second invocation (e.g. per-test host spin-up) repeats the reflection-heavy scan for no benefit; not a bug, just avoidable cost.
  Fix: If profiling ever shows this matters, memoize `ScannedAssemblies` in a static field computed once.
  Confidence: Low
- [Nit][Quality] AdhdTimeOrganizer/config/dependencyInjection/DependencyInjectionExtensions.cs:39-87 — No ordering assertion/comment relative to `AddModuleServices` beyond the inline docs; correctness relies on `ModuleServiceExtensions.ModuleAssemblies` staying accurate and both methods being called (order between the two doesn't actually matter here since `Except` operates on the static list, not on load state), but a future reader might assume call order matters.
  Why: Minor readability/maintainability nit only; the current code is actually order-independent, which is good, but that fact isn't stated.
  Fix: Optionally add a one-line comment noting the two calls are order-independent since `ModuleAssemblies` is a static list, not a scan result.
  Confidence: Low

No other issues found — the `Except(ModuleServiceExtensions.ModuleAssemblies)` guard required by CLAUDE.md's Composition Root section is present and correctly implemented (line 36), matching the accompanying doc comment.
