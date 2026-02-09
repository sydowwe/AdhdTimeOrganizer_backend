using AdhdTimeOrganizer.application.dto.request.activityTracking;

namespace AdhdTimeOrganizer.application.dto.response.activityTracking;

public record TopDomainsResponse
{
    public List<DomainSummaryDto> Domains { get; set; } = new();
    public DayTotalsDto Totals { get; set; } = new();
    public BaselineType Baseline { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}

public class DomainSummaryDto
{
    public string Domain { get; set; } = string.Empty;
    public ActivityStatDto? Active { get; set; }      // null if no active time
    public ActivityStatDto? Background { get; set; }  // null if no background time
    public bool IsNew { get; set; }                   // true if no historical data exists
}

public class ActivityStatDto
{
    public int Seconds { get; set; }
    public int? AverageSeconds { get; set; }          // null if IsNew
    public double? PercentChange { get; set; }        // null if IsNew, e.g., 12.5 for +12.5%, -8.3 for -8.3%
}

public class DayTotalsDto
{
    public int TotalSeconds { get; set; }
    public int ActiveSeconds { get; set; }
    public int BackgroundSeconds { get; set; }
    public int TotalDomains { get; set; }   // Count of unique domains
    public int TotalPages { get; set; }     // Count of unique URLs
    public int TotalEntries { get; set; }   // Count of window records
}
