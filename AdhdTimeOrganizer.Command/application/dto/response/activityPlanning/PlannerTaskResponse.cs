using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Command.application.dto.response.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;

public record PlannerTaskResponse : WithIsDoneResponse
{
    public required DateTime StartTimestamp { get; init; }
    public required int MinuteLength { get; init; }
    public required string Color { get; init; }
}