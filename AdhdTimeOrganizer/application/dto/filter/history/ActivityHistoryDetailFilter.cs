using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter.history;

public record ActivityHistoryDetailFilter : DateAndTimeRangeDto, IFilterRequest
{
}