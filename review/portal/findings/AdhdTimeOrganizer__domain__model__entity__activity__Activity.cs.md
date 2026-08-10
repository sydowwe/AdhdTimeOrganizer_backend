# Review: AdhdTimeOrganizer/domain/model/entity/activity/Activity.cs
Role: entity
Summary: Correctly derives from the portal's closing BaseEntityWithUser shim; the main risk is the shallow MemberwiseClone helper sharing navigation-collection references with the source entity.
Coverage: n/a

## Issues
- [Medium][Quality] AdhdTimeOrganizer/domain/model/entity/activity/Activity.cs:39-44 — `Clone()` uses `MemberwiseClone`, which copies reference-type navigation properties (collections, `Role`, `Category`, profile refs) by reference rather than deep-copying; the cloned entity shares the exact same `TodoListItems`/`ActivityHistoryList`/`PlannerTaskList`/etc. collection instances as the original.
  Why: If either entity's collection is later mutated while both are tracked (or if the source entity had loaded collections), changes leak across the two aggregates and EF's change tracker can get confused about which entity owns which children; this is already called out as a known gotcha in docs/summary.md, so the current single call site (`CloneActivityEndpoint`) is safe only because the entity is fetched via `FindAsync` with no `Include`, leaving collections at their default empty-list state.
  Fix: Either explicitly null out/reset the collection navigation properties after `MemberwiseClone`, or clone only scalar fields via a constructor/mapper instead of `MemberwiseClone`, so future callers that `Include` navigations don't inherit shared references.
  Confidence: Med

- [Low][Security] AdhdTimeOrganizer/domain/model/entity/activity/Activity.cs:16 — `Text` is a free-form, unbounded user-entered field with no `[AuditIgnore]`/redaction marker.
  Why: If/when the audit interceptor (currently unwired per CLAUDE.md) is turned on, or if this field is ever included in a log statement, user-authored free text could leak PII (per the project's own logging/audit guidance) since it can't be regex-scrubbed.
  Fix: No action needed today since auditing is off; when wiring auditing, consider whether `Text` warrants `[AuditIgnore]`, and ensure call sites never log this field directly.
  Confidence: Low

- [Nit][Quality] AdhdTimeOrganizer/domain/model/entity/activity/Activity.cs:26-29,37 — inconsistent collection initialization style: some use collection-expression `[]`, others `new List<T>()`, with no functional difference.
  Why: Minor consistency nit only, no runtime impact.
  Fix: Standardize on `[]` throughout the class.
  Confidence: Low
