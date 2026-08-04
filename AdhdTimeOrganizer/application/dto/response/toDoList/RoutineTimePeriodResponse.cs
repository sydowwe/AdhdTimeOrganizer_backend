using AdhdTimeOrganizer.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;

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

    /// <summary>Days before the reset to nudge about unfinished items; null = this period sends no nudge.</summary>
    public int? ReminderLeadDays { get; init; }

    public DateTime NextResetAt { get; init; }
    public List<PeriodCompletionRecord> CompletionHistory { get; init; } = [];

    public static IQueryable<RoutineTimePeriodResponse> Projection(IQueryable<RoutineTimePeriod> query)
    {
        return query.Select(entity => new RoutineTimePeriodResponse
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
            ReminderLeadDays = entity.ReminderLeadDays
        });
    }
}