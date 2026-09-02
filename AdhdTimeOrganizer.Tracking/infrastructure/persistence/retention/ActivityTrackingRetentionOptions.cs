using Sydowwe.Framework.infrastructure.persistence.retention;

namespace AdhdTimeOrganizer.Tracking.infrastructure.persistence.retention;

/// <summary>
/// Retention policy for the portal's own activity-tracking ledgers (<c>desktop_activity_entry</c>,
/// <c>web_extension_activity_entry</c>, <c>android_session_data</c>), bound from the
/// "ActivityTrackingRetention" config section. All three are per-heartbeat/per-minute/per-session ledgers
/// with no natural "keep last N per job" grouping, so the floor defaults to 0 (pure age purge) unlike
/// Scheduler's per-job KeepLastN default.
/// </summary>
public sealed class ActivityTrackingRetentionOptions : RetentionOptions
{
    public const string SectionName = "ActivityTrackingRetention";

    public ActivityTrackingRetentionOptions()
    {
        KeepLastN = 0;
    }
}
