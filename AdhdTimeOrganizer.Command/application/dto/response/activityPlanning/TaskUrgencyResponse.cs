using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
public record TaskUrgencyResponse : TextColorResponse
{
    public required int Priority { get; init; }
}