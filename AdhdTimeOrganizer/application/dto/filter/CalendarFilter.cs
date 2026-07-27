using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public record CalendarFilter : IFilterRequest
{
    public required DateOnly From { get; init; }


    public required DateOnly Until { get; init; }
}