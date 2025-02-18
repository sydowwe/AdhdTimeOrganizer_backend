using AdhdTimeOrganizer.Common.application.dto.response.generic;

namespace AdhdTimeOrganizer.Command.application.dto.response.activity;

public record ActivityFormSelectOptionsResponse : SelectOptionResponse
{
    public required SelectOptionResponse RoleOption { get; init; }
    public SelectOptionResponse? CategoryOption { get; init; }
    public SelectOptionResponse? TaskUrgencyOption { get; init; }
    public SelectOptionResponse? RoutineTimePeriodOption { get; init; }
}