using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.taskPlanner;
public record TaskPriorityResponse : TextColorResponse
{
    public required int Priority { get; init; }
}