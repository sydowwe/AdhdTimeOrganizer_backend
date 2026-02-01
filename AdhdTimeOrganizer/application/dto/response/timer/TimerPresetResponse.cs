using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.timer;

public record TimerPresetResponse : IdResponse
{
    public required int Duration { get; init; }
    public ActivityResponse? Activity { get; init; }
}
