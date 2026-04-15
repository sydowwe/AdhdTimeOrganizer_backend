using AdhdTimeOrganizer.domain.model.entity.@base;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.todoList;

public class RoutineTimePeriod : BaseTextColorEntity, IEntityWithIsHidden
{
    public bool IsHidden { get; set; }
    public required int LengthInDays { get; set; }
    public required int ResetAnchorDay { get; set; }

    public int Streak { get; set; } = 0;
    public int BestStreak { get; set; } = 0;
    public required int StreakThreshold { get; set; }
    public required int StreakGraceDays { get; set; }
    public DateTime? LastResetAt { get; set; }
    public DateTime? StreakGraceUntil { get; set; }

    public int HistoryDepth { get; set; } = 16;

    public ICollection<RoutineTodoList> RoutineTodoListColl { get; set; } = [];
    public ICollection<RoutinePeriodCompletion> CompletionHistoryColl { get; set; } = [];
}