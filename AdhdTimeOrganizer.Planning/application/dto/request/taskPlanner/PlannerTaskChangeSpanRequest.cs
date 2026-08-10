using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

public record PlannerTaskChangeSpanRequest : IPatchRequest
{
    public required TimeDto StartTime { get; init; }


    public required TimeDto EndTime { get; init; }
}