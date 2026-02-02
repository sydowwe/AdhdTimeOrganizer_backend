using AdhdTimeOrganizer.application.dto.@enum;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking;

public record ActivityEventDto
{
    public ActivityEventType Type { get; init; }
    public string Domain { get; init; } = string.Empty;
    public string? Url { get; init; }
    public bool IsBackground { get; init; }
    public DateTime At { get; init; }
}
