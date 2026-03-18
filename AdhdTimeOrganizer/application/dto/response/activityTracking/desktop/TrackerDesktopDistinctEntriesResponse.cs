using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;

public record TrackerDesktopDistinctEntriesResponse : IIdResponse
{
    public required string ProcessName { get; set; }
    public string? ProductName { get; init; }
    public string? WindowTitle { get; set; }
    public long Id { get; init; } = 0;
}