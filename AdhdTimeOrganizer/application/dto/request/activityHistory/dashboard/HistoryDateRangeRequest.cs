using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.@enum;

namespace AdhdTimeOrganizer.application.dto.request.activityHistory.dashboard;

public record HistoryDateRangeRequest : DateAndTimeRangeDto
{
    public required HistoryGroupBy GroupBy { get; init; }
}
