using AdhdTimeOrganizer.application.dto.dto;

namespace AdhdTimeOrganizer.application.dto.request.activityHistory.dashboard.summary;

public record HistorySummaryStackedBarsRequest : HistorySummaryDateRangeRequest
{
    public int? WindowMinutes { get; init; }
    public TimeDto? WindowStartTime { get; init; }
    public TimeDto? WindowEndTime { get; init; }
}
