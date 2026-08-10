# Review: AdhdTimeOrganizer/infrastructure/jobs/RoutinePeriodNudgeJob.cs
Role: other (Quartz background job)
Summary: Well-documented sweep job with correct rationale for unauthenticated cross-user scope, but a single failing notification mid-loop can lose every already-sent notification's idempotency marker for that run, risking duplicate notifications on the next fire.
Coverage: n/a

## Issues
- [High][Quality] RoutinePeriodNudgeJob.cs:49-75 — All per-period mutations (`EndingSoonNotifiedFor`, `GraceNotifiedFor`) are only persisted by one `SaveChangesAsync` after the whole loop; there is no try/catch around `notifier.NotifyEndingSoonAsync`/`NotifyGraceExpiringAsync` or around the loop body.
  Why: if any single period's notification call throws (e.g. a transient push/email failure), the exception propagates out of `Execute`, the loop aborts, and none of the already-sent notifications for that run get their "notified for" marker saved — so every user who was successfully notified earlier in the same sweep will be re-notified the next day (duplicate nudges), and any periods later in the enumeration are silently skipped for that run.
  Fix: wrap each period's notify+mark block in its own try/catch (log the exception with period id, not PII) and continue the loop, or `SaveChangesAsync` incrementally/per-period so a later failure doesn't roll back earlier successes.
  Confidence: High

- [Medium][Concurrency] RoutinePeriodNudgeJob.cs:45 — `now` is captured once as `DateTime.UtcNow` and used to evaluate `EvaluateEndingSoon`/`ShouldWarnGraceExpiring` for every user's period regardless of the user's own timezone; the job itself is documented to run "at 09:00" implying a wall-clock intent.
  Why: if `RoutineTimePeriod` boundaries (`NextReset`, `StreakGraceUntil`) are meant to reflect each user's local day, a single UTC `now` computed once at sweep start can misclassify periods for users in other offsets, and DST transitions could shift the effective local trigger time by an hour twice a year.
  Fix: confirm whether `EvaluateEndingSoon`/`ShouldWarnGraceExpiring` already normalize against per-user/per-period local time internally; if not, thread the period's own timezone through the evaluation instead of a single shared UTC instant.
  Confidence: Low (evaluation logic lives in `RoutineResetService`, not visible in this file)

- [Low][Performance] RoutinePeriodNudgeJob.cs:49-69 — Notifications are sent sequentially, one `await` per period, inside a single-threaded `foreach`.
  Why: on an install with many active routine periods this serializes what could be independent I/O-bound sends, lengthening the sweep window (though at daily-batch cadence this is unlikely to matter in practice).
  Fix: only worth addressing if period counts grow large; e.g. bounded `Task.WhenAll` batching.
  Confidence: Low

No other issues found — the unauthenticated cross-user query (no `.Where(UserId == ...)`) is correctly intentional and documented per CLAUDE.md's "module reads have no safety net" guidance, since this is a background sweep meant to see all users; no user-scoped inserts occur (only updates to existing tracked `RoutineTimePeriod` rows), so the `UserId == 0` FK-violation-on-insert pitfall does not apply here.
