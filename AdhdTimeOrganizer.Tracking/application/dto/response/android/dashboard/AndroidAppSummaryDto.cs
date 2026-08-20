namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android.dashboard;

public record AndroidAppSummaryDto : IDashboardItem
{
    public required string PackageName { get; init; }
    public required string AppLabel { get; init; }

    /// <inheritdoc />
    public string Key => PackageName;

    /// <inheritdoc />
    public string Label => DashboardItem.LabelOr(AppLabel, PackageName);

    public required bool IsNew { get; init; }
    public AndroidActivityStatDto? Stat { get; init; }
}