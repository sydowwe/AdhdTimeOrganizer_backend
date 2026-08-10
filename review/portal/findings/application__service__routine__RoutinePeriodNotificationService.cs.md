# Review: application/service/routine/RoutinePeriodNotificationService.cs
Role: handler
Summary: Thin, well-reasoned adapter onto INotificationService — single-recipient (owning user), best-effort dispatch, deliberately keeps user-authored text out of logs.
Coverage: n/a

## Issues
- [Low][Quality] application/service/routine/RoutinePeriodNotificationService.cs:76 — `catch (Exception ex)` also swallows `OperationCanceledException`/`TaskCanceledException` (e.g. from a request-scoped `ct` during shutdown or client disconnect) and logs it as a `LogWarning` "failed" event.
  Why: A benign cancellation gets recorded as a notification failure, which is noisy/misleading in logs and could mask real failures during triage.
  Fix: Add a leading `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }` (or just let it propagate) before the general catch.
  Confidence: Low

No other issues found. Quiet-hours deferral to the Notifications module, single-user recipient scoping, and PII-free logging (only `period.Id` and payload type name logged, `period.Text` never logged) are all correctly handled and explained in the doc comments; no N+1 risk since each call handles exactly one recipient.
