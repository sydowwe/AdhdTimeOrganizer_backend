namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.stackedBars;

public record WebExtensionStackedBarsEntry : IDashboardItem
{
    public string Domain { get; set; } = string.Empty;

    /// <inheritdoc />
    public string Key => Domain;

    /// <inheritdoc />
    public string Label => Domain;

    public string? Url { get; set; } // Most visited URL in this window
    public int ActiveSeconds { get; set; }
    public int BackgroundSeconds { get; set; }
    public int TotalSeconds => ActiveSeconds + BackgroundSeconds;
}