using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.timer;

namespace AdhdTimeOrganizer.application.dto.request.timer;

public record TimerPresetRequest : IMyRequest<TimerPreset>
{
    [Required]
    public required int Duration { get; init; }

    public long? ActivityId { get; init; }

    public TimerPreset ToEntity => new() { Duration = Duration, ActivityId = ActivityId };

    public void UpdateEntity(TimerPreset e)
    {
        e.Duration = Duration;
        e.ActivityId = ActivityId;
    }
}
