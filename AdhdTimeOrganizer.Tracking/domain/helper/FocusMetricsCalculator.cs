namespace AdhdTimeOrganizer.Tracking.domain.helper;

/// <summary>
/// One primary-lane session, reduced to what the fragmentation measures need.
///
/// <para><see cref="Key"/> is what "the same item" means for the switch and block measures — the
/// domain, the process name, the package name: exactly what the timeline's primary lane switches
/// between. The caller must not pass a finer cut of it; a URL or a window title here reports in-site
/// navigation as task switching.</para>
///
/// <para><see cref="Label"/> is only the display name the response reports (<c>domain</c> /
/// <c>productName</c> / <c>appLabel</c>, matching <c>summary-cards</c>). It is kept apart from the key
/// because the two are not the same thing on two of the three sources: several processes ship under
/// one product name, and keying on the label would silently merge them into one item and lose every
/// switch between them — while a blank product name would merge everything that has none.</para>
/// </summary>
public readonly record struct FocusSession(
    string Key,
    string Label,
    DateTime StartedAt,
    DateTime EndedAt,
    double DurationSeconds);

/// <summary>The longest unbroken run on one item, tolerated interruptions included.</summary>
public sealed record FocusBlock(string Label, DateTime StartedAt, DateTime EndedAt, double Seconds);

/// <summary>
/// One calendar day's own figures. The span figures are built from these rather than from a flat pool,
/// because every measure but the median is bounded by the day it happened in — see
/// <see cref="FocusMetricsCalculator"/>.
/// </summary>
public sealed record DailyFocusMetrics
{
    public required DateOnly Day { get; init; }
    public required int SessionCount { get; init; }
    public required int SwitchCount { get; init; }
    public required FocusBlock? LongestBlock { get; init; }

    /// <summary><c>null</c> when the day holds fewer than two sessions, so has no interior gap.</summary>
    public required double? LongestGapSeconds { get; init; }

    /// <summary>Every session length in the day, for pooling into a median across the span.</summary>
    public required IReadOnlyList<double> SessionSeconds { get; init; }
}

/// <summary>The whole span, plus the per-day breakdown a baseline is averaged out of.</summary>
public sealed record FocusMetricsResult
{
    public required IReadOnlyList<DailyFocusMetrics> Days { get; init; }
    public required int SessionCount { get; init; }
    public required int DaysWithActivity { get; init; }
    public required int SwitchCount { get; init; }
    public required double MedianSessionSeconds { get; init; }
    public required double? LongestGapSeconds { get; init; }
    public required FocusBlock? LongestBlock { get; init; }
}

/// <summary>
/// The four fragmentation measures of <c>U5</c>, computed over a set of per-day windows.
///
/// <para><b>Every measure but the median is computed inside a single day's window and then rolled
/// up.</b> That is the whole difficulty of the range case and the one thing most likely to go wrong:
/// a user on a 09:00–17:00 window did not spend the sixteen hours between one day's <c>to</c> and the
/// next day's <c>from</c> doing anything the dashboard measured, so a block must not span two days and
/// that overnight interval is not a gap candidate. Pool the sessions flat and the busiest week reports
/// a sixteen-hour "longest break" and a "longest unbroken block" that ran through the night.</para>
///
/// <para>The median is the exception, and legitimately so: it is a statistic of the session
/// distribution, not of any interval between sessions, so pooling every session in the span is what it
/// means.</para>
/// </summary>
public static class FocusMetricsCalculator
{
    /// <summary>
    /// <paramref name="sessionsPerDay"/> is aligned to <paramref name="days"/> index for index, so the
    /// caller has already decided which day each session belongs to. Days with no activity are present
    /// as empty entries — they are what makes <c>DaysWithActivity</c> different from the span length.
    /// </summary>
    public static FocusMetricsResult Compute(
        IReadOnlyList<DailyWindow> days,
        IReadOnlyList<IReadOnlyList<FocusSession>> sessionsPerDay,
        int focusGapSeconds)
    {
        if (sessionsPerDay.Count != days.Count)
            throw new ArgumentException("One session list per day window is required", nameof(sessionsPerDay));

        var daily = new List<DailyFocusMetrics>(days.Count);

        for (var i = 0; i < days.Count; i++)
            daily.Add(ComputeDay(days[i].Day, sessionsPerDay[i], focusGapSeconds));

        var pooled = daily.SelectMany(d => d.SessionSeconds).ToList();

        // The span's longest block is the longest of the days' own, never a run stitched across a
        // night. Ties fall to the earliest, so the figure does not flicker between equal blocks.
        var longestBlock = daily
            .Select(d => d.LongestBlock)
            .Where(b => b != null)
            .OrderByDescending(b => b!.Seconds)
            .ThenBy(b => b!.StartedAt)
            .FirstOrDefault();

        var longestGap = daily
            .Where(d => d.LongestGapSeconds.HasValue)
            .Select(d => d.LongestGapSeconds!.Value)
            .DefaultIfEmpty(double.NaN)
            .Max();

        return new FocusMetricsResult
        {
            Days = daily,
            SessionCount = daily.Sum(d => d.SessionCount),
            DaysWithActivity = daily.Count(d => d.SessionCount > 0),
            SwitchCount = daily.Sum(d => d.SwitchCount),
            MedianSessionSeconds = Median(pooled),
            LongestGapSeconds = double.IsNaN(longestGap) ? null : longestGap,
            LongestBlock = longestBlock
        };
    }

    private static DailyFocusMetrics ComputeDay(DateOnly day, IReadOnlyList<FocusSession> sessions, int focusGapSeconds)
    {
        var ordered = sessions
            .OrderBy(s => s.StartedAt)
            .ThenBy(s => s.EndedAt)
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .ToList();

        return new DailyFocusMetrics
        {
            Day = day,
            SessionCount = ordered.Count,
            SwitchCount = CountSwitches(ordered),
            LongestBlock = LongestBlockOf(ordered, focusGapSeconds),
            LongestGapSeconds = LongestInteriorGapOf(ordered),
            SessionSeconds = ordered.Select(s => s.DurationSeconds).ToList()
        };
    }

    /// <summary>
    /// Points where the foreground item differs from the one before it. Consecutive sessions on the
    /// <b>same</b> item are not a switch: a tracker that splits one continuous run into three records
    /// must not read as two.
    ///
    /// <para>Deliberately not gap-tolerant, unlike <see cref="LongestBlockOf"/>. A short detour is
    /// honestly both a switch and something that failed to break a block, and the two measures answer
    /// different questions.</para>
    /// </summary>
    private static int CountSwitches(IReadOnlyList<FocusSession> ordered)
    {
        var switches = 0;

        for (var i = 1; i < ordered.Count; i++)
            if (!string.Equals(ordered[i].Key, ordered[i - 1].Key, StringComparison.Ordinal))
                switches++;

        return switches;
    }

    /// <summary>
    /// The longest run on a single item, where two of that item's sessions join if separated by at most
    /// <paramref name="focusGapSeconds"/> of <i>anything</i> — another item or untracked time alike.
    /// That is why this walks each label's own sessions rather than the day in order: an eight-minute
    /// detour to a chat window sits between two halves of the same block and must not end it.
    ///
    /// <para>The reported seconds are wall clock from the run's first start to its last end, tolerated
    /// interruptions included, so it names the stretch of time it actually spans.</para>
    /// </summary>
    private static FocusBlock? LongestBlockOf(IReadOnlyList<FocusSession> ordered, int focusGapSeconds)
    {
        FocusBlock? longest = null;

        foreach (var group in ordered.GroupBy(s => s.Key, StringComparer.Ordinal))
        {
            var label = group.First().Label;
            var runStart = DateTime.MinValue;
            var runEnd = DateTime.MinValue;
            var open = false;

            foreach (var session in group)
            {
                if (open && (session.StartedAt - runEnd).TotalSeconds <= focusGapSeconds)
                {
                    if (session.EndedAt > runEnd)
                        runEnd = session.EndedAt;
                    continue;
                }

                if (open)
                    longest = Longer(longest, label, runStart, runEnd);

                runStart = session.StartedAt;
                runEnd = session.EndedAt;
                open = true;
            }

            if (open)
                longest = Longer(longest, label, runStart, runEnd);
        }

        return longest;
    }

    private static FocusBlock Longer(FocusBlock? current, string label, DateTime start, DateTime end)
    {
        var seconds = (end - start).TotalSeconds;

        if (current == null || seconds > current.Seconds || (seconds == current.Seconds && start < current.StartedAt))
            return new FocusBlock(label, start, end, seconds);

        return current;
    }

    /// <summary>
    /// The longest interval between two sessions. <b>Interior only</b>: neither the run up to the first
    /// session nor the run after the last one counts. Those are the edges of the day's window rather
    /// than breaks in anything, and counting the trailing one would report the remainder of an
    /// unfinished day as that day's longest break.
    ///
    /// <para>Overlapping sessions clamp to zero rather than to a negative gap — the background and
    /// detail lanes overlap by design, and while the primary lane should not, a tracker that emits a
    /// one-second overlap must not turn into a negative maximum.</para>
    /// </summary>
    private static double? LongestInteriorGapOf(IReadOnlyList<FocusSession> ordered)
    {
        if (ordered.Count < 2)
            return null;

        double longest = 0;
        var runningEnd = ordered[0].EndedAt;

        for (var i = 1; i < ordered.Count; i++)
        {
            var gap = (ordered[i].StartedAt - runningEnd).TotalSeconds;
            if (gap > longest)
                longest = gap;

            if (ordered[i].EndedAt > runningEnd)
                runningEnd = ordered[i].EndedAt;
        }

        return longest;
    }

    /// <summary>
    /// Median, not mean. The session-length distribution is heavily right-skewed and a mean is
    /// dominated by a handful of long sessions, which is exactly the shape of day this measure exists
    /// to describe. Even count is the mean of the two middle values, so the result can be fractional.
    /// </summary>
    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 0;

        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;

        return sorted.Count % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
