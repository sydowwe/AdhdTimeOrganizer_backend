using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.@enum;

namespace AdhdTimeOrganizer.application.dto.request.activityHistory.dashboard.summary;

public record HistorySummaryDateRangeRequest : DateRangeDto
{
    public required HistoryGroupBy GroupBy { get; init; }
}
