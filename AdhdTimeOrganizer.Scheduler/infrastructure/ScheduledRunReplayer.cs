using AdhdTimeOrganizer.Scheduler.domain.entity;
using AdhdTimeOrganizer.Scheduler.domain.@enum;
using AdhdTimeOrganizer.Scheduler.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.result;

namespace AdhdTimeOrganizer.Scheduler.infrastructure;

/// <summary>
/// The Core.Scheduler implementation of <see cref="IScheduledRunReplayer"/>. Auto-registered via the
/// <see cref="IScopedService"/> marker (it needs the scoped <c>DbContext</c>); safe with no authenticated
/// user (the run log is not user-scoped). Reuses the phase-02b <c>TriggerJob</c> path + the phase-03
/// dispatcher core, so a replay shares the one audited execution path with scheduled and manual fires.
/// </summary>
public class ScheduledRunReplayer(
    DbContext dbContext,
    ISchedulerFactory schedulerFactory,
    IEnumerable<IScheduledJobHandler> handlers) : IScheduledRunReplayer, IScopedService
{
    public async Task<Result> ReplayRunAsync(long runId, CancellationToken ct)
    {
        var run = await dbContext.Set<ScheduledJobRun>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId, ct);
        if (run is null)
            return Result.Error(ResultErrorType.NotFound, $"Run {runId} was not found.");

        // Validate up front rather than letting the dispatcher silently record a no-op: a replay is only
        // meaningful if the original handler still exists. (The dispatcher resolves the handler off the
        // registry's current HandlerKey; the snapshot is what actually executed, so we gate on the snapshot.)
        if (!ScheduledJobHandlerResolver.Exists(handlers, run.HandlerKeySnapshot))
            return Result.Error(ResultErrorType.Conflict,
                $"Cannot replay run {runId}: handler '{run.HandlerKeySnapshot}' is no longer registered.");

        // The dispatcher refuses to invoke a Removed job's handler (it just records an OrphanedTrigger
        // Skipped row) — so a replay of a Removed job's run would fire, return 204, and silently do nothing.
        // Reject it here instead, mirroring the handler-gone 409 above.
        var job = await dbContext.Set<ScheduledJob>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.JobKey == run.JobKeySnapshot, ct);
        if (job is { Status: JobStatus.Removed })
            return Result.Error(ResultErrorType.Conflict,
                $"Cannot replay run {runId}: job '{run.JobKeySnapshot}' has been removed.");

        // Fire the one durable dispatcher off-schedule. The Replay data map makes the dispatcher (phase 03)
        // reproduce the snapshotted payload and write a new run row linked via ReplaysRunId. ScheduledFireTime
        // stays null (the dispatcher leaves it null for any non-Scheduled fire), so the replay is never
        // matched by the scheduled-occurrence dedup and is never silently skipped against the original run.
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var data = new JobDataMap
        {
            { SchedulerQuartzConfig.JobKeyDataKey, run.JobKeySnapshot },
            { SchedulerQuartzConfig.TriggerSourceDataKey, nameof(TriggerSource.Replay) },
            { SchedulerQuartzConfig.ReplaysRunIdDataKey, run.Id }
        };
        if (!string.IsNullOrEmpty(run.PayloadSnapshotJson))
            data[SchedulerQuartzConfig.PayloadOverrideDataKey] = run.PayloadSnapshotJson;

        await scheduler.TriggerJob(SchedulerQuartzConfig.DispatcherJobKey, data, ct);
        return Result.Successful();
    }
}