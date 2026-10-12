using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.rule;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.Routines.domain.service;

/// <summary>
/// F2-06: how long one routine item usually takes, from the time logged against the item itself. Pure — the caller
/// reads the period's ledger and the recordings, this only buckets and takes the median.
/// </summary>
/// <remarks>
/// <para>
/// <b>One sample is one occurrence</b>: everything logged against the item within one cycle of its period, summed.
/// Not one sitting — that is the unit mismatch F3-01 fixed on the to-do side, where an item done in three sittings
/// read as a third of its length. A routine item is done once per cycle, so the cycle is the occurrence.
/// </para>
/// <para>
/// <b>The cycles are the ones the period actually had</b>, taken from its own ledger rather than re-derived: each
/// <see cref="RoutinePeriodCompletion.PeriodEnd"/> is a reset instant that really happened, and
/// <see cref="RoutineTimePeriod.LastResetAt"/> opens the current cycle. Consecutive boundaries are a cycle exactly,
/// even across an edit to the period's length or anchor, and even for calendar months, whose ledger
/// <c>PeriodStart</c> is only <c>PeriodEnd − LengthInDays</c>. That approximation is used once, as the lower edge of
/// the oldest cycle read. Where a long dormancy left the ledger with a gap (a catch-up writes at most
/// <c>HistoryDepth</c> rows), the gap folds into the cycle after it; nobody was ticking that routine then, so nothing
/// was being timed against it either.
/// </para>
/// <para>
/// <b>The open cycle counts only once the item is done in it.</b> Until then its total is a running figure, and
/// averaging it in would pull the typical length down every morning. Its end is the period's next reset, not
/// <c>now</c>: on a read that does not reset (the flat reads), a done item whose reset is already due is the cycle
/// it was done in, and still counts.
/// </para>
/// <para>
/// A cycle with no time logged against the item is not a sample — timing is optional, so the samples are the
/// occurrences the user chose to time. The newest <see cref="SampleCycles"/> of those are kept, and below
/// <see cref="EstimateFitRule.RoutineItemMinimumSamples"/> the answer is nothing at all (<see cref="EstimateFitRule"/>).
/// The figure is never written back to <c>SuggestedTime</c>, which also decides tracker auto-completion.
/// </para>
/// </remarks>
public static class RoutineItemTypicalDuration
{
    /// <summary>Newest timed occurrences the median is taken over — recent enough to follow a routine that changed.</summary>
    public const int SampleCycles = 10;

    /// <summary>
    /// Ledger rows read per period: how far back an occurrence can still be a sample. Sixty days of a daily routine,
    /// a year and a bit of a weekly one.
    /// </summary>
    public const int LookbackCycles = 60;

    /// <summary>A half-open <c>[StartUtc, EndUtc)</c> window.</summary>
    public readonly record struct Cycle(DateTime StartUtc, DateTime EndUtc)
    {
        public bool Contains(DateTime instant) => instant >= StartUtc && instant < EndUtc;
    }

    /// <summary>A period's closed cycles, newest first, and the cycle still open.</summary>
    public sealed record PeriodCycles(IReadOnlyList<Cycle> Closed, Cycle Current)
    {
        /// <summary>The earliest instant any of these cycles covers — the lower bound of the recordings worth reading.</summary>
        public DateTime EarliestStartUtc => Closed.Count > 0 ? Closed[^1].StartUtc : Current.StartUtc;
    }

    /// <summary>The typical length in seconds, and how many occurrences it was taken over.</summary>
    public readonly record struct Typical(long Seconds, int SampleCount);

    /// <summary>
    /// The cycles of <paramref name="period"/>, from its ledger rows (any order, any number — the caller caps them at
    /// <see cref="LookbackCycles"/>) and its own <c>LastResetAt</c>.
    /// </summary>
    public static PeriodCycles CyclesOf(
        RoutineTimePeriod period, IReadOnlyCollection<(DateOnly PeriodStart, DateOnly PeriodEnd)> ledger, DateTime now)
    {
        var currentStart = period.LastResetAt ?? period.CreatedTimestamp;
        var current = new Cycle(currentStart, RoutineResetService.ComputeNextReset(period, now));

        if (ledger.Count == 0)
            return new PeriodCycles([], current);

        // Every recorded reset instant up to the current cycle's start, which closes the newest of them. A row
        // claiming a later end than the period's own LastResetAt is inconsistent data, and is left out rather than
        // allowed to split the open cycle.
        var boundaries = ledger
            .Select(r => AtMidnightUtc(r.PeriodEnd))
            .Where(b => b < currentStart)
            .Append(currentStart)
            .ToHashSet();

        var oldestStart = AtMidnightUtc(ledger.Min(r => r.PeriodStart));
        if (oldestStart < boundaries.Min())
            boundaries.Add(oldestStart);

        var ordered = boundaries.Order().ToList();
        var closed = new List<Cycle>(ordered.Count - 1);
        for (var i = ordered.Count - 1; i > 0; i--)
            closed.Add(new Cycle(ordered[i - 1], ordered[i]));

        return new PeriodCycles(closed, current);
    }

    /// <summary>
    /// The item's typical occurrence, or <c>null</c> below the threshold. <paramref name="recordings"/> may hold other
    /// items' rows and rows outside every cycle; both are ignored.
    /// </summary>
    /// <param name="isDoneThisCycle">Whether the item is done in the open cycle, which is what lets that cycle count.</param>
    public static Typical? For(
        long itemId, PeriodCycles cycles, bool isDoneThisCycle, IEnumerable<RoutineItemLoggedTime> recordings)
    {
        var windows = isDoneThisCycle
            ? cycles.Closed.Prepend(cycles.Current).ToList()
            : cycles.Closed.ToList();
        if (windows.Count == 0)
            return null;

        var totals = new long[windows.Count];
        foreach (var recording in recordings)
        {
            if (recording.RoutineTodoListId != itemId)
                continue;
            var index = windows.FindIndex(w => w.Contains(recording.StartUtc));
            if (index >= 0)
                totals[index] += recording.Seconds;
        }

        // Newest first already, so the first SampleCycles timed occurrences are the most recent ones.
        var samples = totals
            .Where(total => total > 0)
            .Take(SampleCycles)
            .Select(total => (double)total)
            .ToList();

        return EstimateFitRule.MedianOrSilent(samples, EstimateFitRule.RoutineItemMinimumSamples) is { } median
            ? new Typical((long)Math.Round(median), samples.Count)
            : null;
    }

    private static DateTime AtMidnightUtc(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}
