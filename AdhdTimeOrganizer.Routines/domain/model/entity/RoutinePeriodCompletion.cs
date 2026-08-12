using Sydowwe.Framework.domain.entity.@base;

namespace AdhdTimeOrganizer.Routines.domain.model.entity.todoList;

public class RoutinePeriodCompletion : BaseEntity
{
    public required long TimePeriodId { get; set; }
    public RoutineTimePeriod RoutineTimePeriod { get; set; } = null!;
    public required DateOnly PeriodStart { get; set; }
    public required DateOnly PeriodEnd { get; set; }
    public required int CompletedCount { get; set; }
    public required int TotalCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// A streak freeze was spent to cover this period. The counts above are left exactly as they were recorded —
    /// a freeze changes how the period is <i>scored</i>, not what happened in it, so the completion rate the user
    /// sees stays honest and only the streak walk treats the period as neutral.
    /// </summary>
    public bool IsFrozen { get; set; }

    /// <summary>When the freeze was spent. Null whenever <see cref="IsFrozen"/> is false.</summary>
    public DateTime? FrozenAt { get; set; }
}