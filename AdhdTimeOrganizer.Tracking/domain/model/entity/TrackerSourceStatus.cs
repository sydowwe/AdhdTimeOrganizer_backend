using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.helper.unified;

namespace AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;

/// <summary>
/// P2-04: when each of a user's trackers last reported, one row per <c>(user, source)</c>. Kept beside the three
/// ledgers instead of read off them, because "the newest row per user and source" over two RANGE-partitioned tables
/// and an unpartitioned one is the wrong question to ask every hour.
/// <para>
/// Written by <c>TrackerLastSeenStamp</c> on every ingest request, throttled to once per
/// <see cref="TrackerSilenceRule.StampInterval"/>, so <see cref="LastSeenAt"/> is accurate to the hour and no better.
/// Read by <c>TrackerSilenceJobHandler</c>, which raises <c>TrackerSilent</c> once per silence and marks it in
/// <see cref="SilenceNotifiedAt"/>; the next report clears the mark.
/// </para>
/// </summary>
public class TrackerSourceStatus : BaseEntityWithUser
{
    public TrackingSource Source { get; set; }

    /// <summary>The last ingest request from this source (UTC), to the hour.</summary>
    public DateTime LastSeenAt { get; set; }

    /// <summary>When the current silence was notified (UTC); null while the source reports, or before anyone noticed.</summary>
    public DateTime? SilenceNotifiedAt { get; set; }
}
