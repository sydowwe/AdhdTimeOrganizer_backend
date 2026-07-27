using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;

public record TrackerDesktopDistinctEntriesFilter : IFilterRequest
{
    public string? ProcessName { get; set; }
    public PatternMatchType? ProcessNameMatchType { get; set; }
    public string? ProductName { get; set; }
    public PatternMatchType? ProductNameMatchType { get; set; }

    public string? WindowTitle { get; set; } // "*.github.com", "slack.exe", "code.exe"
    public PatternMatchType? WindowTitleMatchType { get; set; }
}