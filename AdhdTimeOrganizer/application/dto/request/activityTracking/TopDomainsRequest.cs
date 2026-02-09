using FastEndpoints;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking;

public record TopDomainsRequest
{
    [QueryParam]
    public DateTime From { get; set; }

    [QueryParam]
    public DateTime To { get; set; }

    [QueryParam]
    public int? TopN { get; set; }  // Optional, default null

    [QueryParam]
    public double? MinPercent { get; set; }  // Minimum percentage threshold (e.g., 1.0 for 1%)

    [QueryParam]
    public BaselineType Baseline { get; set; } = BaselineType.Last7Days;
}

public enum BaselineType
{
    Last7Days,
    Last30Days,
    SameWeekday,
    AllTime
}
