using Microsoft.Extensions.Logging;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Scheduler.infrastructure;

/// <summary>
/// Fallback <see cref="IJobFailureNotifier"/> for a host that wires the Scheduler substrate but ships <b>no</b>
/// delivery module (no Core.Notifications): the alert is logged and dropped rather than delivered. Deliberately
/// kept out of the DI marker scan — it is registered only via a <c>TryAdd</c> fallback in <c>AddCore</c>, so the
/// real Notifications adapter (auto-scanned) always wins wherever it is present.
/// <para>
/// A production host that actually runs recurring jobs MUST have the real consumer plugged in — a seam wired
/// only to this no-op alerts nobody (scheduler follow-up 05, "don't ship an unplugged seam"). The payload is
/// non-PII, so logging its fields here is safe.
/// </para>
/// </summary>
public sealed class NoOpJobFailureNotifier(ILogger<NoOpJobFailureNotifier> logger) : IJobFailureNotifier
{
    public Task NotifyJobFailedAsync(JobFailureAlert alert, CancellationToken ct = default)
    {
        logger.LogWarning(
            "Job {JobKey} ({OwnerModule}) failed (run {RunId}, {ErrorType}) but no IJobFailureNotifier consumer is wired; alert dropped.",
            alert.JobKey, alert.OwnerModule, alert.RunId, alert.ErrorType);
        return Task.CompletedTask;
    }
}