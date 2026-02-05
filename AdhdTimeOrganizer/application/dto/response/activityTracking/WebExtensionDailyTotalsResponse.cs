namespace AdhdTimeOrganizer.application.dto.response.activityTracking;

public record WebExtensionDailyTotalsResponse
{
    public DateTime Date { get; set; }
    public int TotalActiveSeconds { get; set; }
    public int TotalBackgroundSeconds { get; set; }
    public List<DomainTotal> TopDomains { get; set; } = new();
}

public class DomainTotal
{
    public string Domain { get; set; } = string.Empty;
    public int ActiveSeconds { get; set; }
    public int BackgroundSeconds { get; set; }
    public int TotalSeconds => ActiveSeconds + BackgroundSeconds;
}
