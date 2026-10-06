using System.Linq.Expressions;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.Routines.application.dto.response.todoList;

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

    /// <summary>
    /// Freezes spendable right now. Always a number once the server supports freezes — the client reads
    /// <c>null</c> as "this server has no freeze feature" and hides every affordance, so a period that grants
    /// none reports <c>0</c> rather than null.
    /// </summary>
    public int FreezesRemaining { get; init; }

    /// <summary>The full per-window allowance, shown as context next to the remaining count.</summary>
    public int FreezeBudget { get; init; }

    /// <summary>When the allowance next refills. Null only on a period no read has refreshed yet.</summary>
    public DateTime? FreezeBudgetResetsAt { get; init; }

    public DateTime NextResetAt { get; init; }
    public List<PeriodCompletionRecord> CompletionHistory { get; init; } = [];

    /// <summary>
    /// Closed cycles in <see cref="CompletionHistory"/> — the denominator of the consistency figure, and the
    /// client's gate for every surface that needs a period to have elapsed at least once (the review card,
    /// the personal-bests panel). Counted over the same <see cref="RoutineTimePeriod.HistoryDepth"/> window
    /// the heatmap shows, so the figure beside the heatmap describes the cells in it. Not a column: the ledger
    /// is the source, and a counter beside it is one more thing to keep in step.
    /// <para>
    /// An excused cycle (the user was away for most of it) is in the heatmap but <b>not</b> counted here: "away,
    /// doesn't count against you" has to hold for the consistency figure too, or a sick fortnight drags it down
    /// for as long as those cycles stay in the window.
    /// </para>
    /// </summary>
    public int TotalPeriodsElapsed { get; init; }

    /// <summary>
    /// Of <see cref="TotalPeriodsElapsed"/>, the cycles that met <see cref="StreakThreshold"/>. A frozen cycle
    /// is not one of them — a freeze carries the streak, it does not make the period count as done — and a
    /// cycle with no items is neither, matching <c>RoutineStreakFreezeService.IsSuccess</c>.
    /// </summary>
    public int TotalPeriodsCompleted { get; init; }

    /// <summary>
    /// The full period shape the client renders — the projected columns plus the derived members
    /// (<see cref="NextResetAt"/>, <see cref="CompletionHistory"/> and the two totals over it) that
    /// <see cref="Projection"/> cannot compute in the database. Every endpoint returning a whole period goes
    /// through here so the grouped read and the freeze endpoint cannot drift apart; the client re-reads both
    /// with the same parser.
    /// <para><paramref name="historyAsc"/> is oldest-first and already trimmed to
    /// <see cref="RoutineTimePeriod.HistoryDepth"/> by the caller.</para>
    /// </summary>
    public static RoutineTimePeriodResponse From(
        RoutineTimePeriod period,
        IEnumerable<RoutinePeriodCompletion> historyAsc,
        DateTime now)
    {
        var history = historyAsc.ToList();

        return Map(period) with
        {
            NextResetAt = RoutineResetService.ComputeNextReset(period, now),
            CompletionHistory = history.Select(PeriodCompletionRecord.From).ToList(),
            TotalPeriodsElapsed = history.Count(completion => !completion.IsExcused),
            TotalPeriodsCompleted = history.Count(completion =>
                RoutineStreakFreezeService.IsSuccess(completion, period.StreakThreshold))
        };
    }

    public static IQueryable<RoutineTimePeriodResponse> Projection(IQueryable<RoutineTimePeriod> query)
    {
        return query.Select(ProjectionExpression);
    }

    private static readonly Expression<Func<RoutineTimePeriod, RoutineTimePeriodResponse>> ProjectionExpression =
        entity => new RoutineTimePeriodResponse
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
            ReminderLeadDays = entity.ReminderLeadDays,
            FreezesRemaining = entity.FreezesRemaining,
            FreezeBudget = entity.FreezeBudget,
            FreezeBudgetResetsAt = entity.FreezeBudgetResetsAt
        };

    /// <summary>
    /// The same mapping, compiled once and reused — <see cref="From"/> runs it as a delegate against an
    /// already loaded entity instead of interpreting the expression tree per row through a one-element
    /// <c>IQueryable</c>. Declared after <see cref="ProjectionExpression"/> on purpose: static field
    /// initializers run in textual order, so compiling above it would capture a null expression.
    /// </summary>
    private static readonly Func<RoutineTimePeriod, RoutineTimePeriodResponse> Map = ProjectionExpression.Compile();
}