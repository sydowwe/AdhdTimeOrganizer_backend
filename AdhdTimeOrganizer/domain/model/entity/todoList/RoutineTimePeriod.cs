using AdhdTimeOrganizer.domain.model.entity.@base;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.todoList;

public class RoutineTimePeriod : BaseTextColorEntity, IEntityWithIsHidden
{
    public bool IsHidden { get; set; }
    public required int LengthInDays { get; set; }
    public int ResetAnchorDay { get; set; }

    public int Streak { get; set; }
    public int BestStreak { get; set; }
    public int StreakThreshold { get; set; }
    public int StreakGraceDays { get; set; }
    public DateTime? LastResetAt { get; set; }
    public DateTime? StreakGraceUntil { get; set; }

    public ICollection<RoutineTodoList> RoutineTodoListColl { get; set; } = [];
}