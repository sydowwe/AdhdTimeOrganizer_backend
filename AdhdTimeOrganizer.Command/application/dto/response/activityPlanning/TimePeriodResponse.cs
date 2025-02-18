using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;

public record TimePeriodResponse : TextColorResponse
{
    public required int LengthInDays { get; init; }
    public bool IsHiddenInView { get; init; } = false;
}