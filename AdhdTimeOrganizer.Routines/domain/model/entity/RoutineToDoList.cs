using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.Routines.domain.model.entity.todoList;

public class RoutineTodoList : BaseTodoListItem
{
    public required long TimePeriodId { get; set; }
    public RoutineTimePeriod RoutineTimePeriod { get; set; } = null!;
    public DateOnly? LastResetDate { get; set; }
    public int Streak { get; set; }
    public int BestStreak { get; set; }
    public DateTime LastCompletedAt { get; set; }

    /// <summary>
    /// Non-null once the item has scored in the cycle that is still open, and then holds <see cref="BestStreak"/>
    /// as it stood just before that score — the one thing an un-tick inside the same cycle cannot otherwise
    /// recover, since <c>BestStreak == Streak</c> afterwards reads the same whether the tick set a record or
    /// merely reached one. Null means "not scored this cycle": the next completing toggle counts, and an
    /// un-tick has nothing to give back. Every reset path clears it.
    /// </summary>
    public int? BestStreakBeforeScore { get; set; }

    public List<DayOfWeek> SuggestedDays { get; set; } = [];
    public int? SuggestedDayOfMonth { get; set; }
}