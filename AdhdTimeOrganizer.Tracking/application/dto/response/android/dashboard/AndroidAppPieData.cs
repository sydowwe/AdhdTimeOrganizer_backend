namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android.dashboard;

public record AndroidAppPieData : IDashboardItem
{
    public required string PackageName { get; init; }
    public required string AppLabel { get; init; }

    /// <inheritdoc />
    public string Key => PackageName;

    /// <inheritdoc />
    public string Label => DashboardItem.LabelOr(AppLabel, PackageName);

    public required long Seconds { get; init; }
    public required long TotalSeconds { get; init; }
}