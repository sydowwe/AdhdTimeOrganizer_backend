using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;

public record ActivityTrackingDesktopSettingsEntryFormattingRequest : ICreateRequest, IUpdateRequest
{
    public required bool IsSavedToMainHistory { get; init; }
    public required string ProcessKey { get; init; }
    public required string ProcessNice { get; init; }
    public string? TitleSplit { get; init; }
    public required long ActivityTrackingDesktopSettingsId { get; init; }
}
