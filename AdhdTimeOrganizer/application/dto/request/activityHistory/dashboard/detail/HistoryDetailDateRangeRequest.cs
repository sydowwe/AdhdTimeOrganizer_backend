using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.@enum;
using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.application.dto.request.activityHistory.dashboard.detail;

public record HistoryDetailDateRangeRequest : DateAndTimeRangeDto
{
    public required HistoryGroupBy GroupBy { get; init; }
}