using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Command.application.dto.response.extendable;
using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityHistory;

public record AlarmResponse : NameTextColorResponse, IEntityWithActivityResponse
{
    public required DateTime StartTimestamp { get; init; }
    public required ActivityResponse Activity { get; init; }
    public required bool IsActive { get; init; }
}