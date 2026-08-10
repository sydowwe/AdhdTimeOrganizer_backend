# Review: AdhdTimeOrganizer/application/helper/TaskPlannerHelper.cs
Role: other
Summary: Small, pure, side-effect-free helper (query-shaping extension + time-overlap predicate); no recurrence/date-rollover logic actually lives here.
Coverage: n/a

## Issues
- [Medium][Quality] AdhdTimeOrganizer/application/helper/TaskPlannerHelper.cs:18 — `TasksOverlap` (`task.StartTime < end2 && task.EndTime > start2`) assumes both intervals are same-day and non-wrapping; it silently misbehaves for a task that crosses midnight (e.g. `StartTime=23:00, EndTime=01:00`, where `EndTime < StartTime` numerically), since `TimeOnly` has no day component.
  Why: If any planner task can span midnight, overlap detection against it will produce false negatives/positives, letting conflicting tasks be scheduled or blocking non-conflicting ones.
  Fix: Either document/enforce that `StartTime < EndTime` is a domain invariant (validated on create/update) so this helper's assumption always holds, or add explicit wrap-around handling if cross-midnight tasks are supported.
  Confidence: Medium

- [Nit][Quality] AdhdTimeOrganizer/application/helper/TaskPlannerHelper.cs:8 — `WithIncludes` is a fixed include chain with no way to opt out of `Activity.Role`/`Activity.Category` when a caller only needs `Importance`.
  Why: Callers that don't need the full Activity graph still pay for the joins; minor over-fetch, not a correctness issue.
  Fix: Leave as-is unless a caller profiles this as a hot path; not worth splitting for a 2-include chain.
  Confidence: Low

No other issues found — `WithIncludes` is a straightforward composable `IQueryable` extension (no DbContext reach-in, no in-memory materialization, no user-scoping concern since it doesn't filter), and `TasksOverlap` is a pure predicate suitable for unit testing as-is.
