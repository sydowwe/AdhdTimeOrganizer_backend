using AdhdTimeOrganizer.Tracking.domain.helper.unified;

namespace AdhdTimeOrganizer.Tracking.application.service.unified;

/// <summary>One item, on one source, over the whole span, in whole seconds.</summary>
public sealed record UnifiedLedgerEntry(
    string Label,
    TrackingSource Source,
    int ActiveSeconds,
    int BackgroundSeconds,
    int Entries)
{
    public int TotalSeconds => ActiveSeconds + BackgroundSeconds;
}

/// <summary>
/// The single rounded ledger every merged figure is read off.
///
/// <para><b>Everything derives from these entries, and nothing recomputes from the fractional merge.</b>
/// That is what makes the contract's two arithmetic invariants hold by construction rather than by
/// coincidence: the source chips are sums of these rows, the pie's totals are sums of these rows, and
/// two surfaces on one screen therefore cannot disagree about the same second. Round twice, from two
/// different pools, and they eventually will.</para>
/// </summary>
public sealed class UnifiedLedger
{
    public required IReadOnlyList<UnifiedLedgerEntry> Entries { get; init; }

    /// <summary>Seconds attributed to each source — the sum of its own entries, exactly.</summary>
    public required IReadOnlyDictionary<TrackingSource, int> CountedSeconds { get; init; }

    /// <summary>Seconds this source recorded that another source was credited with.</summary>
    public required IReadOnlyDictionary<TrackingSource, int> DisplacedSeconds { get; init; }

    /// <summary>Which source took the most of them, as a wire name, or <c>null</c> when none were displaced.</summary>
    public required IReadOnlyDictionary<TrackingSource, string?> DisplacedTo { get; init; }

    /// <summary>
    /// Builds the ledger from a merge and the raw per-source totals the loader read.
    ///
    /// <para><paramref name="rawTotals"/> is what each source's <b>own</b> dashboard reports for the
    /// span, and displacement is derived as the remainder rather than accumulated separately — so
    /// <c>countedSeconds + displacedSeconds</c> equals that dashboard's figure to the second, which is
    /// the check the page prints the two numbers side by side to invite.</para>
    /// </summary>
    public static UnifiedLedger Build(
        UnifiedMergeResult merge,
        IReadOnlyDictionary<TrackingSource, double> rawTotals)
    {
        var entries = new List<UnifiedLedgerEntry>();
        var counted = new Dictionary<TrackingSource, int>();

        foreach (var source in TrackingSourceNames.All)
        {
            var groups = merge.Minutes
                .Where(m => m.Source == source)
                .GroupBy(m => m.Label, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new
                {
                    Label = g.Key,
                    Active = g.Sum(m => m.ActiveSeconds),
                    Background = g.Sum(m => m.BackgroundSeconds),
                    Entries = g.Count()
                })
                .ToList();

            counted[source] = 0;

            if (groups.Count == 0)
                continue;

            // Active and background are allocated out of one pool so the source's own total is exact
            // even where the two are individually fractional.
            var values = new List<double>(groups.Count * 2);

            foreach (var group in groups)
            {
                values.Add(group.Active);
                values.Add(group.Background);
            }

            var allocated = SecondsAllocator.Allocate(values);

            for (var i = 0; i < groups.Count; i++)
                entries.Add(new UnifiedLedgerEntry(
                    groups[i].Label, source, allocated[i * 2], allocated[i * 2 + 1], groups[i].Entries));

            counted[source] = allocated.Sum();
        }

        var displaced = TrackingSourceNames.All.ToDictionary(
            source => source,
            source =>
            {
                var raw = (int)Math.Round(rawTotals.GetValueOrDefault(source), MidpointRounding.AwayFromZero);
                return Math.Max(0, raw - counted[source]);
            });

        return new UnifiedLedger
        {
            Entries = entries,
            CountedSeconds = counted,
            DisplacedSeconds = displaced,
            DisplacedTo = TrackingSourceNames.All.ToDictionary(
                source => source,
                source => displaced[source] > 0 && merge.DisplacedTo.TryGetValue(source, out var target) && target != null
                    ? target.Value.ToWireName()
                    : null)
        };
    }

    /// <summary>The trackers behind an item, as wire names in precedence order.</summary>
    public static List<string> SourceNamesOf(IEnumerable<UnifiedLedgerEntry> entries) =>
        entries
            .Select(e => e.Source)
            .Distinct()
            .OrderBy(source => source)
            .Select(source => source.ToWireName())
            .ToList();
}
