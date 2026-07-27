using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.domain.model.entity.timer;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.timer;

public record PomodoroTimerPresetRequest : IMyRequest<PomodoroTimerPreset>
{
    [Required]
    [StringLength(255)]
    public required string Name { get; init; }


    public required int FocusDuration { get; init; }


    public required int ShortBreakDuration { get; init; }


    public required int LongBreakDuration { get; init; }


    public required int FocusPeriodInCycleCount { get; init; }


    public required int NumberOfCycles { get; init; }

    public long? FocusActivityId { get; init; }

    public long? RestActivityId { get; init; }

    public PomodoroTimerPreset ToEntity => new()
    {
        UserId = 0,
        Name = Name,
        FocusDuration = FocusDuration,
        ShortBreakDuration = ShortBreakDuration,
        LongBreakDuration = LongBreakDuration,
        FocusPeriodInCycleCount = FocusPeriodInCycleCount,
        NumberOfCycles = NumberOfCycles,
        FocusActivityId = FocusActivityId,
        RestActivityId = RestActivityId
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