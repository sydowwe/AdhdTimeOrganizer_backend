using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Sydowwe.Framework.domain.audit;

namespace AdhdTimeOrganizer.domain.model.entity.activityTracking;

public class WebExtensionActivityEntry : BaseEntityWithUser
{
    private DateTime _windowStart;

    /// <summary>
    /// Partition key. Derived from <see cref="WindowStart"/> rather than set by callers, so the two can
    /// never diverge and misfile a row into the wrong partition (same guarantee as
    /// <c>DesktopActivityEntry</c>).
    /// </summary>
    public DateOnly RecordDate { get; private set; }

    /// <summary>
    /// Start of the one-minute tracking window, always aligned to the minute (zero seconds and
    /// sub-second). This is load-bearing, not cosmetic: <c>WebExtensionTimelineEndpoint</c> derives each
    /// window's end as <c>WindowStart.AddMinutes(1)</c> and stitches adjacent windows by testing
    /// <c>WindowStart == previous.EndedAt</c>, so an unaligned value silently produces gapped or
    /// overlapping timeline segments rather than an error.
    /// <para>
    /// Enforced at the ingest boundary by <c>WebExtensionHeartbeatValidator</c> (a 400, so a
    /// misbehaving client is told). Not enforced by a throwing setter here on purpose — EF would run it
    /// while materializing rows written before the rule existed.
    /// </para>
    /// </summary>
    public required DateTime WindowStart
    {
        get => _windowStart;
        init
        {
            _windowStart = value;
            RecordDate = DateOnly.FromDateTime(value);
        }
    }

    [AuditIgnore]
    public required string Domain { get; set; }

    // Nullable is intentional, not an omission: domain-only tracking (no page URL captured) is a
    // legitimate entry — see WebExtensionDataHeartbeatEndpoint, which only sets Url when non-empty.
    [AuditIgnore]
    public required string? Url { get; set; }

    public required int ActiveSeconds { get; set; }
    public required int BackgroundSeconds { get; set; }
    public required bool IsFinal { get; set; }
}