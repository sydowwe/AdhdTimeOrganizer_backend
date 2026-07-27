using Quartz;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Scheduler.infrastructure;

/// <summary>
/// The inputs for one delayed auto-retry: which job, the attempt number (<c>1..MaxRetries</c>) + the max
/// (for the alerting seam), the backoff <see cref="Delay"/> to fire after, the original-run lineage id every
/// retry in a chain links to, and the exact payload the failed attempt ran with (re-run faithfully).
/// </summary>
public sealed record RetryFireRequest(
    string JobKey,
    int Attempt,
    int MaxRetries,
    TimeSpan Delay,
    long LineageRunId,
    string? PayloadJson);

/// <summary>
/// Schedules a delayed off-schedule re-fire of the single durable dispatcher after a failed scheduled run —
/// the substrate's auto-retry-with-backoff. Split behind an interface so the dispatcher stays testable: the
/// real impl arms a Quartz one-shot trigger; tests substitute a recording fake and drive the retry fire
/// deterministically (no real waiting).
/// </summary>
public interface IJobRetryScheduler
{
    /// <summary>
    /// Arm a one-shot fire of the dispatcher for <paramref name="request"/> at <c>now + Delay</c>. Must not
    /// block the calling (worker) thread — it only schedules a future trigger and returns.
    /// </summary>
    Task ScheduleAsync(RetryFireRequest request, CancellationToken ct);
}

/// <summary>
/// The Core.Scheduler implementation: arms a Quartz one-shot trigger (a single fire at <c>now + Delay</c>,
/// no repeat) pointing at the same durable <see cref="SchedulerQuartzConfig.DispatcherJobKey"/> every other
/// fire uses, carrying the retry data map. It touches only the Quartz RAM store, so it is stateless and
/// registered as a singleton.
/// <para>
/// <b>Single-node caveat (documented, not fixed):</b> the pending trigger lives in the RAM job store, so a
/// process restart drops it — a failed occurrence whose retry hadn't fired yet simply waits for the job's
/// next scheduled fire (a one-shot has none; see the module summary). Consistent with the module's other
/// RAM-store gaps (pauses / triggers don't survive restart either).
/// </para>
/// </summary>
public sealed class JobRetryScheduler(ISchedulerFactory schedulerFactory) : IJobRetryScheduler, ISingletonService
{
    public async Task ScheduleAsync(RetryFireRequest request, CancellationToken ct)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var data = SchedulerQuartzConfig.BuildRetryDataMap(
            request.JobKey, request.Attempt, request.MaxRetries, request.LineageRunId, request.PayloadJson);

        // A trigger with only a StartAt and no schedule fires exactly once, then is removed — a clean one-shot.
        // A distinct identity (guid-suffixed) keeps concurrent retry triggers from colliding with each other or
        // with the job's recurring trigger (which lives under the same group keyed by the plain JobKey).
        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{request.JobKey}::retry::{Guid.NewGuid():N}", SchedulerQuartzConfig.TriggerGroup)
            .ForJob(SchedulerQuartzConfig.DispatcherJobKey)
            .StartAt(DateTimeOffset.UtcNow + request.Delay)
            .UsingJobData(data)
            .Build();

        await scheduler.ScheduleJob(trigger, ct);
    }
}