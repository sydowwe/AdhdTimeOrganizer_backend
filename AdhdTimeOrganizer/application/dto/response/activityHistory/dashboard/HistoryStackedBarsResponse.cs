using AdhdTimeOrganizer.application.dto.@enum;

namespace AdhdTimeOrganizer.application.dto.response.activityHistory.dashboard;

public record HistoryStackedBarsResponse
{
    public HistoryGranularity Granularity { get; set; }
    public List<HistoryWindow> Windows { get; set; } = new();
}
