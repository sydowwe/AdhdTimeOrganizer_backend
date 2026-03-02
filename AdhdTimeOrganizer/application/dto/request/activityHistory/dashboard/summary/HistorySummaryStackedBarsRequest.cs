using AdhdTimeOrganizer.application.dto.dto;

namespace AdhdTimeOrganizer.application.dto.request.activityHistory.dashboard.summary;

public record HistorySummaryStackedBarsRequest : HistorySummaryDateRangeRequest
{
    public required int WindowMinutes { get; init; }
    public required TimeDto WindowStartTime { get; init; }
    public required TimeDto WindowEndTime { get; init; }
}
