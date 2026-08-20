using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.helper.unified;

namespace AdhdTimeOrganizer.Tracking.application.service.unified;

/// <summary>
/// One request's worth of merged day: what the ledgers held, what survived the overlap rule, and the
/// rounded ledger every figure is read off.
///
/// <para>The six dashboards differ only in how they project this. Assembling it once means the overlap
/// rule and the rounding are stated once — a second transcription of either would let the pie and the
/// filter chips disagree about the same second while both endpoints' tests passed.</para>
/// </summary>
public sealed class UnifiedSpan
{
    public required IReadOnlyList<SourceLoad> Loads { get; init; }
    public required UnifiedMergeResult Merge { get; init; }
    public required UnifiedLedger Ledger { get; init; }

    /// <summary>
    /// Loads the selected sources and merges them. <paramref name="sources"/> is the selection: a
    /// source outside it is never read, so it contributes nothing and displaces nothing.
    /// </summary>
    public static async Task<UnifiedSpan> BuildAsync(
        DbContext db,
        long userId,
        DailyWindowSet windows,
        IReadOnlyCollection<TrackingSource> sources,
        CancellationToken ct) =>
        From(await UnifiedActivityLoader.LoadAsync(db, userId, windows, sources, ct), sources);

    /// <summary>
    /// The same thing from loads the caller already has — <c>/sources</c> reads all three ledgers,
    /// because its <c>hasData</c> is independent of selection, and then merges only the selected ones.
    /// </summary>
    public static UnifiedSpan From(IReadOnlyList<SourceLoad> loads, IReadOnlyCollection<TrackingSource> selected)
    {
        var selectedLoads = loads.Where(load => selected.Contains(load.Source)).ToList();

        var merge = UnifiedMinuteMerger.Merge(selectedLoads.SelectMany(load => load.Minutes));

        var rawTotals = TrackingSourceNames.All.ToDictionary(
            source => source,
            source => selectedLoads.FirstOrDefault(load => load.Source == source)?.RawTotalSeconds ?? 0);

        return new UnifiedSpan
        {
            Loads = loads,
            Merge = merge,
            Ledger = UnifiedLedger.Build(merge, rawTotals)
        };
    }

    /// <summary>
    /// The minutes each source ends up owning outright, with one label per minute — the strict
    /// partition the timeline's lanes and the merged focus stream need, as opposed to the shares the
    /// totals are built from.
    ///
    /// <para>Where a minute is split between sources it goes whole to whoever holds most of it; a lane
    /// that showed the same minute twice would break the one thing the merged timeline is for, which is
    /// being read top to bottom as a single day rather than as three transparencies.</para>
    /// </summary>
    public List<(DateTime Minute, TrackingSource Source, string Label, string? Detail, double Seconds)> ExclusiveMinutes()
    {
        var result = new List<(DateTime, TrackingSource, string, string?, double)>();

        foreach (var minute in Merge.Minutes.GroupBy(m => m.Minute))
        {
            if (!Merge.MinuteOwner.TryGetValue(minute.Key, out var owner))
                continue;

            // Within the owning source, the item it spent most of the minute on. A minute can hold a
            // foreground item and several background ones; the lane draws what was in front.
            var winner = minute
                .Where(m => m.Source == owner)
                .GroupBy(m => m.Label, StringComparer.Ordinal)
                .Select(g => new
                {
                    Label = g.Key,
                    Detail = g.Select(m => m.Detail).FirstOrDefault(d => !string.IsNullOrEmpty(d)),
                    Active = g.Sum(m => m.ActiveSeconds),
                    Seconds = g.Sum(m => m.TotalSeconds)
                })
                .OrderByDescending(g => g.Active)
                .ThenByDescending(g => g.Seconds)
                .ThenBy(g => g.Label, StringComparer.Ordinal)
                .FirstOrDefault();

            if (winner == null || winner.Seconds <= 0)
                continue;

            result.Add((minute.Key, owner, winner.Label, winner.Detail, winner.Seconds));
        }

        return result.OrderBy(r => r.Item1).ToList();
    }
}
