using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.dto.response.extendable;

namespace AdhdTimeOrganizer.application.dto.response.activityHistory;

public record AlarmResponse : NameTextColorIconResponse, IEntityWithActivityResponse
{
    public required DateTime StartTimestamp { get; init; }
    public required ActivityResponse Activity { get; init; }
    public required bool IsActive { get; init; }
}