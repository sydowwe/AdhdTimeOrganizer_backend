# P2-06 — Move the refresh-token cleanup onto the scheduler

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first.

## Facts

- `AdhdTimeOrganizer/Program.cs:360` — `services.AddHostedService<RefreshTokenCleanupService>()`.
- `framework/Sydowwe.Framework/infrastructure/extService/user/auth/RefreshTokenCleanupService.cs` is a
  `BackgroundService` loop: `CleanupExpiredTokensAsync()`, then `Task.Delay(24h)`.
- The host summary claims every scheduled job runs on the Scheduler substrate (`AdhdTimeOrganizer/docs/summary.md`,
  *Every scheduled job in the solution now runs on the Scheduler module's substrate*). This one doesn't.

## What that costs

- Not in the scheduler dashboard; no run log.
- No `ScheduledJobFailed` alert — an exception is only a log line — and no `ScheduledJobOverdue` detection.
- The 24 h timer restarts on every boot, so it runs at every deploy and its cadence depends on deploy frequency.
- Multi-instance → runs on every instance.

## Build

1. `RefreshTokenCleanupJobHandler : IScheduledJobHandler, IScopedService` calling
   `IRefreshTokenService.CleanupExpiredTokensAsync()`.
   - Placement: `IScheduledJobHandler` is in Contracts, so the handler *can* live in `Sydowwe.Framework`
     next to the service; the registration stays per host. Keep `RefreshTokenCleanupService` for hosts without the
     Scheduler.
2. Register in `PortalScheduledJobsRegistrar` (daily, e.g. `0 0 4 * * ?` UTC, `DisallowConcurrent`).
3. Remove the `AddHostedService` line.
4. Add the job key to `ModuleWiringTests`.
5. Fix nothing in the summary — it becomes true again.
