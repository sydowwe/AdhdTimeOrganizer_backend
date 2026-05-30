using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.application.dto.response.todoList;

public record RoutineTimePeriodResponse : TextColorResponse, IProjectionResponse<RoutineTimePeriodResponse, RoutineTimePeriod>
{
    public required int LengthInDays { get; init; }
    public bool IsHidden { get; init; } = false;
    public int ResetAnchorDay { get; init; }
    public int StreakThreshold { get; init; }
    public int StreakGraceDays { get; init; }
    public int Streak { get; init; }
    public int BestStreak { get; init; }
    public DateTime? LastResetAt { get; init; }
    public int HistoryDepth { get; init; }
    public DateTime NextResetAt { get; init; }
    public List<PeriodCompletionRecord> CompletionHistory { get; init; } = [];

    public static IQueryable<RoutineTimePeriodResponse> Projection(IQueryable<RoutineTimePeriod> query) =>
        query.Select(e => FromEntity(e));

    public static RoutineTimePeriodResponse FromEntity(RoutineTimePeriod entity) => new()
    {
        Id = entity.Id,
        Text = entity.Text,
        Color = entity.Color,
        LengthInDays = entity.LengthInDays,
        IsHidden = entity.IsHidden,
        ResetAnchorDay = entity.ResetAnchorDay,
        StreakThreshold = entity.StreakThreshold,
        StreakGraceDays = entity.StreakGraceDays,
        Streak = entity.Streak,
        BestStreak = entity.BestStreak,
        LastResetAt = entity.LastResetAt,
        HistoryDepth = entity.HistoryDepth,
    };
}
