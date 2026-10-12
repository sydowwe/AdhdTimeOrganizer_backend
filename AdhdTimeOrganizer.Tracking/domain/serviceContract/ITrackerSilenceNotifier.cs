using AdhdTimeOrganizer.Tracking.domain.helper.unified;

namespace AdhdTimeOrganizer.Tracking.domain.serviceContract;

/// <summary>
/// P2-04: the Tracking slice's notification producer — the one place that names <c>TrackerSilent</c> and its payload.
/// </summary>
public interface ITrackerSilenceNotifier
{
    /// <summary>
    /// Tells <paramref name="userId"/> that <paramref name="source"/> has gone quiet. Best effort: a failed dispatch is
    /// logged and reported as <c>false</c>, so the caller leaves the silence unmarked and a later sweep retries.
    /// </summary>
    Task<bool> NotifyAsync(long userId, TrackingSource source, DateTime lastSeenAt, CancellationToken ct);
}
