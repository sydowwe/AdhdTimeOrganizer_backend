using AdhdTimeOrganizer.History.application.dto.@enum;
using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.History.application.dto.request.activityHistory.dashboard.detail;

public record HistoryDetailDateRangeRequest : DateAndTimeRangeDto
{
    public required HistoryGroupBy GroupBy { get; init; }
}