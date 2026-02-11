namespace AdhdTimeOrganizer.application.dto.response.activityTracking.summaryCards;

public record DomainSummaryDto
{
    public string Domain { get; set; } = string.Empty;
    public ActivityStatDto? Active { get; set; }      // null if no active time
    public ActivityStatDto? Background { get; set; }  // null if no background time
    public bool IsNew { get; set; }                   // true if no historical data exists
}