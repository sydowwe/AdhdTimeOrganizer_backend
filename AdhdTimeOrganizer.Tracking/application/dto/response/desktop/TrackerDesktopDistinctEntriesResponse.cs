using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.desktop;

public record TrackerDesktopDistinctEntriesResponse : IIdResponse, IMappingCoverageRow
{
    public required string ProcessName { get; set; }
    public string? ProductName { get; init; }
    public string? WindowTitle { get; set; }
    public long Id { get; init; } = 0;

    /// <summary>
    /// The active rule the ingest would match this tuple to today, or null when none does (B24). Computed
    /// after the query, not a column — not a sort key.
    /// </summary>
    public MappingCoverageResponse? CoveredBy { get; set; }
}