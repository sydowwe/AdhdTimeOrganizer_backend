using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Planning.application.dto.filter;

public record PlannerTaskFilter : IFilterRequest
{
    public required long CalendarId { get; init; }

    public required TimeDto From { get; init; }


    public required TimeDto Until { get; init; }
}