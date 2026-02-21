using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.@enum;

namespace AdhdTimeOrganizer.application.dto.request.activityHistory.dashboard.detail;

public record HistoryDetailDateRangeRequest : DateAndTimeRangeDto
{
    public required HistoryGroupBy GroupBy { get; init; }
}
