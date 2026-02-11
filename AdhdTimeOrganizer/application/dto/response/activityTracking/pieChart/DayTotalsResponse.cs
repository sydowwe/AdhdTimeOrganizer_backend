namespace AdhdTimeOrganizer.application.dto.response.activityTracking.pieChart;

public record DayTotalsResponse
{
    public int TotalSeconds { get; set; }
    public int ActiveSeconds { get; set; }
    public int BackgroundSeconds { get; set; }
    public int TotalDomains { get; set; }   // Count of unique domains
    public int TotalPages { get; set; }     // Count of unique URLs
    public int TotalEntries { get; set; }   // Count of window records
}
