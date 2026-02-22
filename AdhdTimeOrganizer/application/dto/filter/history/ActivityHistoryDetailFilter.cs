using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter.history;

public record ActivityHistoryDetailFilter : DateAndTimeRangeDto, IFilterRequest
{

}