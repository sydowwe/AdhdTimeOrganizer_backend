using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.activityPlanning;
public record TaskPriorityResponse : TextColorResponse
{
    public required int Priority { get; init; }
}