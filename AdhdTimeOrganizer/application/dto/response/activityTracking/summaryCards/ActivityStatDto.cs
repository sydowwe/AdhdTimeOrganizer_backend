namespace AdhdTimeOrganizer.application.dto.response.activityTracking.summaryCards;

public record ActivityStatDto
{
    public int Seconds { get; set; }
    public int? AverageSeconds { get; set; }          // null if IsNew
    public double? PercentChange { get; set; }        // null if IsNew, e.g., 12.5 for +12.5%, -8.3 for -8.3%
}