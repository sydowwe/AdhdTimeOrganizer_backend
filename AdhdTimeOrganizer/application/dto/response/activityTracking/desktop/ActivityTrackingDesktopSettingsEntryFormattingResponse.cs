using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;

public record ActivityTrackingDesktopSettingsEntryFormattingResponse : IdResponse
{
    public required bool IsSavedToMainHistory { get; init; }
    public required string ProcessKey { get; init; }
    public required string ProcessNice { get; init; }
    public string? TitleSplit { get; init; }
}
