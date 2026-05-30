using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.timer;

namespace AdhdTimeOrganizer.application.dto.request.timer;

public record PomodoroTimerPresetRequest : IMyRequest<PomodoroTimerPreset>
{
    [Required, StringLength(255)]
    public required string Name { get; init; }

    [Required]
    public required int FocusDuration { get; init; }

    [Required]
    public required int ShortBreakDuration { get; init; }

    [Required]
    public required int LongBreakDuration { get; init; }

    [Required]
    public required int FocusPeriodInCycleCount { get; init; }

    [Required]
    public required int NumberOfCycles { get; init; }

    public long? FocusActivityId { get; init; }

    public long? RestActivityId { get; init; }

    public PomodoroTimerPreset ToEntity => new()
    {
        Name = Name,
        FocusDuration = FocusDuration,
        ShortBreakDuration = ShortBreakDuration,
        LongBreakDuration = LongBreakDuration,
        FocusPeriodInCycleCount = FocusPeriodInCycleCount,
        NumberOfCycles = NumberOfCycles,
        FocusActivityId = FocusActivityId,
        RestActivityId = RestActivityId,
    };

    public void UpdateEntity(PomodoroTimerPreset e)
    {
        e.Name = Name;
        e.FocusDuration = FocusDuration;
        e.ShortBreakDuration = ShortBreakDuration;
        e.LongBreakDuration = LongBreakDuration;
        e.FocusPeriodInCycleCount = FocusPeriodInCycleCount;
        e.NumberOfCycles = NumberOfCycles;
        e.FocusActivityId = FocusActivityId;
        e.RestActivityId = RestActivityId;
    }
}
