# Review: AdhdTimeOrganizer/domain/model/entity/activityPlanning/BasePlannerTask.cs
Role: entity
Summary: Small abstract base is mostly fine, but overnight-task support was started and abandoned, and "optional" status is encoded as a magic number.
Coverage: n/a

## Issues
- [Medium][Quality] BasePlannerTask.cs:7-9 — `IsNextDay` is commented out while `StartTime`/`EndTime` remain plain `TimeOnly` with no way to express a task that spans midnight (e.g. 23:00→01:00).
  Why: Any duration calculation (`EndTime - StartTime`) for an overnight task silently wraps/goes negative since `TimeOnly` has no date component and no day-crossing flag exists; `PlannerTask`/`RepeatingPlannerTask`/`TemplatePlannerTask` all inherit this gap.
  Fix: Either restore `IsNextDay`/`IsOvernight` as a real, migrated column and use it wherever duration is computed, or delete the dead comment if overnight tasks are genuinely out of scope.
  Confidence: Med

- [Medium][Quality] BasePlannerTask.cs:19 — `IsOptional` derives from `Importance?.Importance == 666`, a magic sentinel value with no named constant.
  Why: `666` as "this importance level means optional" is undiscoverable from the entity or `TaskImportance` alone; a future seed/reorder of importance levels silently breaks `IsOptional` with no compiler or runtime signal.
  Fix: Replace with a named constant (e.g. `TaskImportance.OptionalMarkerValue`) or a dedicated `IsOptional` flag on `TaskImportance` itself.
  Confidence: Med

- [Nit][Quality] BasePlannerTask.cs:17 — `TaskImportance? Importance { get; set; } = null!;` combines a nullable type with a null-forgiving default, which is contradictory/confusing.
  Why: The `null!` suggests the author expected this to be always-populated, but the property type says otherwise; readers can't tell whether nullability is real.
  Fix: Drop `= null!` (a nullable property already defaults to `null`), or make it `required` if it's actually mandatory.
  Confidence: Low
