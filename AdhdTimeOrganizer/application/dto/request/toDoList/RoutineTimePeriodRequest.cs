using AdhdTimeOrganizer.application.dto.request.@base;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record RoutineTimePeriodRequest : TextColorRequest, ICreateRequest<RoutineTimePeriod>, IUpdateRequest<RoutineTimePeriod>
{
    public int LengthInDays { get; init; }
    public bool IsHidden { get; init; } = false;

    /// <summary>
    /// Weekly-aligned periods (LengthInDays &lt;= 7 or % 7 == 0): 0 = rolling, 1 = Mon … 7 = Sun.
    /// All other periods: 0 = rolling, 1–30 = day of month.
    /// </summary>
    public int ResetAnchorDay { get; init; }

    /// <summary>1–100 — minimum completion % to count the period as a streak success.</summary>
    public int StreakThreshold { get; init; } = 100;

    /// <summary>0 to LengthInDays-1 — extra days after the period ends before the streak breaks.</summary>
    public int StreakGraceDays { get; init; }

    /// <summary>1–100 — how many past periods to include in completion history.</summary>
    public int HistoryDepth { get; init; } = 16;


    public RoutineTimePeriod ToEntity => new RoutineTimePeriod
    {
        Text = Text,
        Color = Color,
        LengthInDays = LengthInDays,
        IsHidden = IsHidden,
        ResetAnchorDay = ResetAnchorDay,
        StreakThreshold = StreakThreshold,
        StreakGraceDays = StreakGraceDays,
        HistoryDepth = HistoryDepth,
        UserId = 0
    };
    public void UpdateEntity(RoutineTimePeriod entity)
    {
        throw new NotImplementedException();
    }
}