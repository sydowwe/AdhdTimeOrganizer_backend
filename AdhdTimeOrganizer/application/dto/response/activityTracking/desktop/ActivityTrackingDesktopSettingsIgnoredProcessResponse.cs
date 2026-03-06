using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;

public record ActivityTrackingDesktopSettingsIgnoredProcessResponse : IdResponse
{
    public required string ProcessKey { get; init; }
    public bool? TitleContainsToggle { get; init; }
    public string? TitleContains { get; init; }
}
