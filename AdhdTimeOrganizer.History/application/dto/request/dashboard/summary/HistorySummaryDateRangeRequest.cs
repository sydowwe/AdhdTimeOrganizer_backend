using AdhdTimeOrganizer.Core.application.dto.dto;
using AdhdTimeOrganizer.History.application.dto.@enum;

namespace AdhdTimeOrganizer.History.application.dto.request.activityHistory.dashboard.summary;

public record HistorySummaryDateRangeRequest : DateRangeDto
{
    public required HistoryGroupBy GroupBy { get; init; }
}