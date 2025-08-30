using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.activityPlanning;
public record TaskUrgencyResponse : TextColorResponse
{
    public required int Priority { get; init; }
}