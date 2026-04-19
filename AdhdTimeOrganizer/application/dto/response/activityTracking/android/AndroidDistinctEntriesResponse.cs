using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.activityTracking.android;

public record AndroidDistinctEntriesResponse : IIdResponse
{
    public long Id { get; init; } = 0;
    public required string PackageName { get; set; }
    public string? AppLabel { get; set; }
}
