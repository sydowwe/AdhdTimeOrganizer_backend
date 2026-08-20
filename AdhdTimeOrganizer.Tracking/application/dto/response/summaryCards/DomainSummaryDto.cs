namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.summaryCards;

public record DomainSummaryDto : IDashboardItem
{
    public string Domain { get; set; } = string.Empty;

    /// <inheritdoc />
    public string Key => Domain;

    /// <inheritdoc />
    public string Label => Domain;

    public ActivityStatDto? Active { get; set; } // null if no active time
    public ActivityStatDto? Background { get; set; } // null if no background time
    public bool IsNew { get; set; } // true if no historical data exists
}