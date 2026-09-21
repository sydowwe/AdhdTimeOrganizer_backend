using FastEndpoints;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;

/// <summary>
/// The android twin of <see cref="desktop.DesktopProcessDetailsRequest"/>: the same instant envelope and
/// optional daily window, keyed on the package the pie slice was drawn for.
/// </summary>
public record AndroidAppDetailsRequest : DailyWindowMaskRequest
{
    [QueryParam]
    public string PackageName { get; set; } = string.Empty;
}
