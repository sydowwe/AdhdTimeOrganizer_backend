using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.toDoList;

public record RoutineTimePeriodResponse : TextColorResponse
{
    public required int LengthInDays { get; init; }
    public bool IsHidden { get; init; } = false;
    public int ResetAnchorDay { get; init; }
    public int StreakThreshold { get; init; }
    public int StreakGraceDays { get; init; }
    public int Streak { get; init; }
    public int BestStreak { get; init; }
    public DateTime? LastResetAt { get; init; }
}