namespace AdhdTimeOrganizer.application.dto.response.activityHistory.dashboard;

public record HistoryPieTotals
{
    public long TotalSeconds { get; set; }
    public int TotalEntries { get; set; }
    public int UniqueGroups { get; set; }
}
