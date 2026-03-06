using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public class ActivityTrackingDesktopSettingsIgnoredProcessFilterRequest : IFilterRequest
{
    public string? ProcessKey { get; set; }
}
