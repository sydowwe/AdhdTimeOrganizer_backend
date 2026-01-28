using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.timer;

public record PomodoroTimerPresetResponse : IdResponse
{
    public required string Name { get; init; }
    public required int FocusDuration { get; init; }
    public required int ShortBreakDuration { get; init; }
    public required int LongBreakDuration { get; init; }
    public required int FocusPeriodInCycleCount { get; init; }
    public required int NumberOfCycles { get; init; }
    public long? FocusActivityId { get; init; }
    public long? RestActivityId { get; init; }
}
