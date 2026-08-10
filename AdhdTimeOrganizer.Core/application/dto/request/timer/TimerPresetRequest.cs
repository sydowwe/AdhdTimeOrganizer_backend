using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Core.application.dto.request.timer;

public record TimerPresetRequest : IMyRequest<TimerPreset>
{
    public required int Duration { get; init; }

    public long? ActivityId { get; init; }

    public TimerPreset ToEntity => new() { Duration = Duration, ActivityId = ActivityId };

    public void UpdateEntity(TimerPreset e)
    {
        e.Duration = Duration;
        e.ActivityId = ActivityId;
    }
}