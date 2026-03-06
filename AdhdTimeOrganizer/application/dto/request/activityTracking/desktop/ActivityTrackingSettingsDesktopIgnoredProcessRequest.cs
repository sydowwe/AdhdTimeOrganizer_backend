using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;

public record ActivityTrackingSettingsDesktopIgnoredProcessRequest : ICreateRequest, IUpdateRequest
{
    public required string ProcessKey { get; init; }
    public bool? TitleContainsToggle { get; init; }
    public string? TitleContains { get; init; }
}
