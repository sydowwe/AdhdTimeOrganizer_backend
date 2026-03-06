using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public class ActivityTrackingDesktopSettingsEntryFormattingFilterRequest : IFilterRequest
{
    public string? ProcessKey { get; set; }
}
