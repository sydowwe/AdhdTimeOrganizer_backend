using AdhdTimeOrganizer.application.dto.@enum;
using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking;

public record SummaryCardsRequest : DateAndTimeRangeDto
{
    public int? TopN { get; init; } // Optional, default null

    public BaselineType Baseline { get; init; } = BaselineType.Last7Days;
}