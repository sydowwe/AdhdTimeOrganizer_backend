using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.timer;

public record PomodoroTimerPresetRequest : IMyRequest
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
}
