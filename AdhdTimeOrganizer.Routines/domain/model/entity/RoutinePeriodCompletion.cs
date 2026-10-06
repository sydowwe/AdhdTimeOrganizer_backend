using Sydowwe.Framework.domain.entity.@base;

namespace AdhdTimeOrganizer.Routines.domain.model.entity.todoList;

/// <summary>
/// Append-only ledger of elapsed periods: one row per closed cycle of a <see cref="RoutineTimePeriod"/>.
/// <para>
/// Deliberately a plain <c>BaseEntity</c> rather than a <c>BaseTableEntity</c> — it carries no <c>UserId</c>
/// (scoping runs transitively through <see cref="TimePeriodId"/>) and it is never updated after the sweep writes
/// it, so it needs neither the user shim nor a concurrency token. The cost is that its configuration cannot call
/// <c>BaseEntityConfigure()</c>; <c>RoutinePeriodCompletionConfiguration</c> spells out exactly what that leaves
/// on the floor.
/// </para>
/// </summary>
public class RoutinePeriodCompletion : BaseEntity
{
    public required long TimePeriodId { get; set; }
    public RoutineTimePeriod RoutineTimePeriod { get; set; } = null!;
    public required DateOnly PeriodStart { get; set; }
    public required DateOnly PeriodEnd { get; set; }
    public required int CompletedCount { get; set; }
    public required int TotalCount { get; set; }
    /// <summary>
    /// This type's own timestamp, not the <c>CreatedTimestamp</c>/<c>ModifiedTimestamp</c> pair a
    /// <c>BaseTableEntity</c> would bring — see the class remarks. Set in code; there is no <c>now()</c> default.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// A streak freeze was spent to cover this period. The counts above are left exactly as they were recorded —
    /// a freeze changes how the period is <i>scored</i>, not what happened in it, so the completion rate the user
    /// sees stays honest and only the streak walk treats the period as neutral.
    /// </summary>
    public bool IsFrozen { get; set; }

    /// <summary>When the freeze was spent. Null whenever <see cref="IsFrozen"/> is false.</summary>
    public DateTime? FrozenAt { get; set; }

    /// <summary>
    /// The cycle was missed while the user was away: at least half of its days were vacation or sick days
    /// (<c>RoutineAwayRule</c>). Like a freeze it changes scoring, not the counts, and the streak walk carries the
    /// run across it — but it costs no budget, and it is set by the server (at reset, or retroactively when the
    /// user marks the days afterwards), never by the user. Exclusive with <see cref="IsFrozen"/>: an excused
    /// cycle is not a miss, so there is nothing to freeze. Only ever set, never cleared — un-excusing would cut a
    /// streak retroactively, the one outcome the feature exists to prevent.
    /// </summary>
    public bool IsExcused { get; set; }
}