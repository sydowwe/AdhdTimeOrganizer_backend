namespace AdhdTimeOrganizer.application.dto.response.activityHistory.dashboard;

public record HistoryPeriodComparison
{
    public long PreviousPeriodTotalSeconds { get; set; }
    public long CurrentPeriodTotalSeconds { get; set; }
    public double? PercentChange { get; set; }
}
