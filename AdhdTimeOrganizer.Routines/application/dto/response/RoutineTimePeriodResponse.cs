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
    /// The full period shape the client renders — the projected columns plus the two derived members
    /// (<see cref="NextResetAt"/>, <see cref="CompletionHistory"/>) that <see cref="Projection"/> cannot compute
    /// in the database. Every endpoint returning a whole period goes through here so the grouped read and the
    /// freeze endpoint cannot drift apart; the client re-reads both with the same parser.
    /// <para><paramref name="historyAsc"/> is oldest-first and already trimmed to
    /// <see cref="RoutineTimePeriod.HistoryDepth"/> by the caller.</para>
    /// </summary>
    public static RoutineTimePeriodResponse From(
        RoutineTimePeriod period,
        IEnumerable<RoutinePeriodCompletion> historyAsc,
        DateTime now) =>
        Projection(new[] { period }.AsQueryable()).Single() with
        {
            NextResetAt = RoutineResetService.ComputeNextReset(period, now),
            CompletionHistory = historyAsc.Select(PeriodCompletionRecord.From).ToList()
        };

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
            ReminderLeadDays = entity.ReminderLeadDays,
            FreezesRemaining = entity.FreezesRemaining,
            FreezeBudget = entity.FreezeBudget,
            FreezeBudgetResetsAt = entity.FreezeBudgetResetsAt
        });
    }
}