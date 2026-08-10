# Review: AdhdTimeOrganizer/infrastructure/jobs/RoutineTodoListResetJob.cs
Role: other (Quartz background job)
Summary: Correctly avoids the ExecuteUpdate/Audit pitfall by using tracked SaveChangesAsync, but silently drops grace-expiry streak breaks on runs with no actual period reset, and never resets sub-item `Steps` because they aren't included in the query.

## Issues
- [High][Quality] RoutineTodoListResetJob.cs:43-47 — `CheckGrace` (line 33) mutates every period's `Streak`/`StreakGraceUntil` when a grace window has lapsed, but the method returns before `SaveChangesAsync` (line 50) whenever `reset.Count == 0` (no period is actually due for a full reset this run).
  Why: Any period whose streak grace expired on a day when no period happened to hit its reset boundary has its streak silently reset in memory and then discarded — the DB still shows the old (stale) `Streak`/`StreakGraceUntil`, so the grace break is effectively lost and the next run recomputes it from stale data, potentially warning/breaking the streak incorrectly later or never persisting the break at all.
  Fix: Track whether `CheckGrace` returned `true` for any period and call `SaveChangesAsync` if either that or `reset.Count > 0` is true, e.g. `var graceChanged = periods.Aggregate(false, (acc, p) => RoutineResetService.CheckGrace(p, now) || acc);` then gate the early return on `!graceChanged && reset.Count == 0`.
  Confidence: High

- [High][Quality] RoutineTodoListResetJob.cs:21-23 — the query only does `.Include(tp => tp.RoutineTodoListColl)`, with no `.ThenInclude(item => item.Steps)`, yet `RoutineResetService.TryReset` (both overloads) iterates `item.Steps` to reset `step.IsDone = false`.
  Why: With no lazy-loading proxies configured in this project (confirmed via grep — `UseLazyLoadingProxies` not found anywhere), `item.Steps` is always the entity's empty default collection here, so sub-steps of a routine todo list item never get reset on period rollover; only the parent item's `IsDone`/`DoneCount` are cleared.
  Fix: Add `.ThenInclude(t => t.Steps)` to the query, e.g. `.Include(tp => tp.RoutineTodoListColl).ThenInclude(t => t.Steps)`.
  Confidence: High

- [Low][Quality] RoutineTodoListResetJob.cs (whole file) / Program.cs:199-203 — an extra `StartNow()` trigger labeled "TEMP: verify manually … remove after checking logs" fires this job on every app restart in addition to the daily 2AM cron.
  Why: Currently harmless because the reset logic is idempotent on `LastResetAt` (a second run in the same window is a no-op), but it's dead debug scaffolding left in production wiring and a future edit to the reset logic could make double-firing on every deploy unsafe.
  Fix: Remove the TEMP trigger from `Program.cs` now that the migration/behavior has presumably been verified.
  Confidence: Med

No other issues found — the global per-user query filter is correctly bypassed here (background scope has no authenticated `HttpContext`, so `!IsAuthenticated` lets all users' periods through, which is the intended behavior for a sweep job), the reset + completion-record insert is committed in one `SaveChangesAsync` (transactional), notification dispatch is deliberately ordered after the commit so a notifier failure can't roll back the reset, and `[DisallowConcurrentExecution]` guards against overlapping runs on the same node.
