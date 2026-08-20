namespace AdhdTimeOrganizer.Tracking.domain.helper.unified;

/// <summary>
/// One source's seconds inside one minute of wall clock, on one item. The three ledgers are flattened
/// into this before anything is merged, so the overlap rule is stated once over a single shape rather
/// than three times over three schemas.
///
/// <para><see cref="Detail"/> is the finer string a source happens to carry (the browser URL) and takes
/// no part in any decision — it is only forwarded to the timeline.</para>
/// </summary>
public readonly record struct SourceMinute(
    DateTime Minute,
    TrackingSource Source,
    string Label,
    string? Detail,
    double ActiveSeconds,
    double BackgroundSeconds)
{
    public double TotalSeconds => ActiveSeconds + BackgroundSeconds;
}

/// <summary>What survived the merge: the same shape, with the displaced part already removed.</summary>
public sealed record MergedMinute
{
    public required DateTime Minute { get; init; }
    public required TrackingSource Source { get; init; }
    public required string Label { get; init; }
    public required string? Detail { get; init; }
    public required double ActiveSeconds { get; init; }
    public required double BackgroundSeconds { get; init; }

    public double TotalSeconds => ActiveSeconds + BackgroundSeconds;
}

/// <summary>
/// The de-overlapped day, plus the two figures that make the merged total falsifiable.
///
/// <para>Without <see cref="DisplacedSeconds"/> and <see cref="DisplacedTo"/> a user who sees less time
/// here than the three dashboards add up to cannot tell whether their browser hour was attributed to
/// the extension, halved, or dropped — the difference between a number and a claim.</para>
/// </summary>
public sealed class UnifiedMergeResult
{
    public required IReadOnlyList<MergedMinute> Minutes { get; init; }

    /// <summary>Seconds attributed to each selected source after the rule.</summary>
    public required IReadOnlyDictionary<TrackingSource, double> CountedSeconds { get; init; }

    /// <summary>Seconds this source recorded that another source was credited with.</summary>
    public required IReadOnlyDictionary<TrackingSource, double> DisplacedSeconds { get; init; }

    /// <summary>
    /// Which source took the most of them. One line per source is all the page renders, so a source
    /// displaced by two others names only the larger of the two.
    /// </summary>
    public required IReadOnlyDictionary<TrackingSource, TrackingSource?> DisplacedTo { get; init; }

    /// <summary>
    /// The single source each minute belongs to, for the surfaces that need a strictly exclusive
    /// partition rather than shares — see <see cref="UnifiedMinuteMerger"/>.
    /// </summary>
    public required IReadOnlyDictionary<DateTime, TrackingSource> MinuteOwner { get; init; }
}

/// <summary>
/// The overlap rule of the unified dashboard, in one copy.
///
/// <para><b>The problem.</b> The three ledgers observe the same wall clock and say different things
/// about it. An hour in a browser is logged by the desktop agent as one <c>chrome.exe</c> process and
/// by the extension as a set of domains; a phone session runs while a laptop sits with a browser open
/// on a second monitor. Added up naively the day is longer than a day. So each instant is credited to
/// exactly one source, and the losing source is told how much it lost and to whom.</para>
///
/// <para><b>The rule, in two levels, in this order.</b></para>
/// <list type="number">
/// <item><b>Foreground beats background.</b> Time one source reports as active outranks time another
/// reports as background, whatever their rank. Android reports no background time at all, so all of it
/// is in the active class.</item>
/// <item><b>Within one activity class the more specific source wins</b>, which is the order of
/// <see cref="TrackingSource"/>.</item>
/// </list>
///
/// <para><b>Level 1 exists because level 2 alone gets a real case wrong.</b> A browser left open on a
/// second monitor while the user is on their phone is desktop <i>background</i> against android
/// <i>foreground</i>; rank alone would credit the desktop and quietly delete the phone time.</para>
///
/// <para><b>The minute is the atom, and losing is partial.</b> Both per-minute ledgers store one row
/// per item per minute, so a minute is the finest thing either of them can honestly say. Within a
/// minute a source claims only the wall clock it actually observed — its <i>footprint</i> — and a lower
/// source keeps whatever share of the minute is left over. That is what makes a three-hour desktop
/// session which loses five minutes to the extension keep two hours fifty-five, and, more importantly,
/// what keeps browser time the extension could not see — a PDF viewer, a <c>chrome://</c> page, a
/// window open before the extension started — alive as <c>Google Chrome</c>. Suppressing the browser
/// process wholesale while the extension is selected is the tempting shortcut and it is wrong: it
/// leaves an hour the user spent in a browser showing no browser at all.</para>
///
/// <para><b>Two things this deliberately does not try to be.</b> A source that loses part of a minute
/// gives up that share of <i>every</i> item it saw in the minute, proportionally, because neither
/// ledger records where inside the minute anything happened — the residue is smeared rather than taken
/// from the item most likely to be the duplicate, which would be a guess dressed as a fact. And
/// ownership is a share, not an exclusive claim, so two sources can both hold part of one minute; the
/// surfaces that need a strict partition (the timeline's lanes, the merged focus stream) use
/// <see cref="UnifiedMergeResult.MinuteOwner"/>, which resolves the minute to whoever holds most of
/// it.</para>
///
/// <para><b>A source is never displaced by itself.</b> The desktop ledger alone can report more than
/// sixty seconds in a minute — one foreground process plus several background ones — and that is not an
/// overlap to resolve, it is what its own dashboard already shows. The budget is therefore spent on the
/// source's footprint, not on its summed seconds, so a single-source request displaces nothing.</para>
/// </summary>
public static class UnifiedMinuteMerger
{
    private const double MinuteSeconds = 60;

    /// <summary>
    /// <paramref name="contributions"/> must already be restricted to the request's daily windows and
    /// to the selected sources: a source that is not selected contributes nothing and displaces
    /// nothing, so the merge never sees it.
    /// </summary>
    public static UnifiedMergeResult Merge(IEnumerable<SourceMinute> contributions)
    {
        var merged = new List<MergedMinute>();
        var counted = NewSourceMap();
        var displaced = NewSourceMap();
        var displacedTo = new Dictionary<TrackingSource, Dictionary<TrackingSource, double>>();
        var minuteOwner = new Dictionary<DateTime, TrackingSource>();

        foreach (var minute in contributions.Where(c => c.TotalSeconds > 0).GroupBy(c => c.Minute))
        {
            var bySource = minute
                .GroupBy(c => c.Source)
                .ToDictionary(g => g.Key, g => g.ToList());

            var footprints = bySource.ToDictionary(kv => kv.Key, kv => FootprintOf(kv.Value));
            var order = OwnershipOrder(bySource, footprints);

            var taken = 0.0;
            var owned = new Dictionary<TrackingSource, double>();

            foreach (var source in order)
            {
                var own = Math.Min(footprints[source], MinuteSeconds - taken);
                if (own < 0)
                    own = 0;

                owned[source] = own;
                taken += own;
            }

            // Whoever holds most of the minute owns it outright for the strict-partition surfaces; a
            // tie falls to precedence, which `order` already encodes.
            minuteOwner[minute.Key] = order.OrderByDescending(s => owned[s]).First();

            foreach (var source in order)
            {
                var footprint = footprints[source];
                var keep = footprint > 0 ? owned[source] / footprint : 0;

                foreach (var row in bySource[source])
                {
                    var keptActive = row.ActiveSeconds * keep;
                    var keptBackground = row.BackgroundSeconds * keep;
                    var lost = row.TotalSeconds - keptActive - keptBackground;

                    counted[source] += keptActive + keptBackground;

                    if (keptActive + keptBackground > 0)
                        merged.Add(new MergedMinute
                        {
                            Minute = row.Minute,
                            Source = source,
                            Label = row.Label,
                            Detail = row.Detail,
                            ActiveSeconds = keptActive,
                            BackgroundSeconds = keptBackground
                        });

                    if (lost <= 0)
                        continue;

                    displaced[source] += lost;

                    // Who took it: the first source in precedence order that holds any of this minute
                    // and is not this one. `keep < 1` only happens when the budget ran out on someone
                    // else, so in practice there is always such a source — but the seconds are counted
                    // as displaced either way, so a missing one leaves the total honest and only the
                    // "who took it" line unsaid.
                    var winner = order
                        .Cast<TrackingSource?>()
                        .FirstOrDefault(s => s != source && owned[s!.Value] > 0);

                    if (winner == null)
                        continue;

                    if (!displacedTo.TryGetValue(source, out var targets))
                        displacedTo[source] = targets = new Dictionary<TrackingSource, double>();

                    targets.TryAdd(winner.Value, 0);
                    targets[winner.Value] += lost;
                }
            }
        }

        return new UnifiedMergeResult
        {
            Minutes = merged,
            CountedSeconds = counted,
            DisplacedSeconds = displaced,
            DisplacedTo = TrackingSourceNames.All.ToDictionary(
                source => source,
                source => displacedTo.TryGetValue(source, out var targets) && targets.Count > 0
                    ? targets.OrderByDescending(t => t.Value).ThenBy(t => t.Key).First().Key
                    : (TrackingSource?)null),
            MinuteOwner = minuteOwner
        };
    }

    /// <summary>
    /// The wall clock of the minute a source observed, which is what it may claim — never the seconds
    /// it recorded, which several concurrent background processes can push well past sixty.
    ///
    /// <para>Active seconds are mutually exclusive within a source (one window is in front at a time)
    /// so they sum; background rows overlap each other freely so the widest one stands for all of them.
    /// The two are combined with <c>max</c> rather than added, which under-states a source whose
    /// background app ran only in the part of the minute its foreground app did not — conservative in
    /// the right direction, since a footprint is a licence to displace someone else.</para>
    /// </summary>
    private static double FootprintOf(List<SourceMinute> rows)
    {
        var active = Math.Min(MinuteSeconds, rows.Sum(r => r.ActiveSeconds));
        var background = Math.Min(MinuteSeconds, rows.Count == 0 ? 0 : rows.Max(r => r.BackgroundSeconds));

        return Math.Min(MinuteSeconds, Math.Max(active, background));
    }

    /// <summary>
    /// The two levels of the rule, as one ordering: every source with foreground time first in rank
    /// order, then the background-only ones in rank order.
    /// </summary>
    private static List<TrackingSource> OwnershipOrder(
        Dictionary<TrackingSource, List<SourceMinute>> bySource,
        Dictionary<TrackingSource, double> footprints) =>
        bySource.Keys
            .Where(source => footprints[source] > 0)
            .OrderBy(source => bySource[source].Sum(r => r.ActiveSeconds) > 0 ? 0 : 1)
            .ThenBy(source => source)
            .ToList();

    private static Dictionary<TrackingSource, double> NewSourceMap() =>
        TrackingSourceNames.All.ToDictionary(source => source, _ => 0.0);
}
