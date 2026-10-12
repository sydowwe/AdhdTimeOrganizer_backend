using AdhdTimeOrganizer.Tracking.domain.helper.unified;

namespace AdhdTimeOrganizer.Tracking.domain.serviceContract;

/// <summary>
/// P2-04: records that a tracker reported. Called first thing by each of the three ingest endpoints — a request that
/// reached the handler proves the tracker is alive, whether or not its batch turns out to hold anything storable.
/// </summary>
public interface ITrackerLastSeenStamp
{
    /// <summary>
    /// Moves <c>TrackerSourceStatus.LastSeenAt</c> to now and clears a silence mark, at most once per
    /// <c>TrackerSilenceRule.StampInterval</c> per user and source. <b>Never throws</b> (cancellation aside): the
    /// ingest it rides on is the user's data, and a missed stamp is an hour's precision lost, not a report.
    /// </summary>
    Task StampAsync(long userId, TrackingSource source, CancellationToken ct);
}
