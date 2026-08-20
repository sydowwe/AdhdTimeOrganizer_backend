namespace AdhdTimeOrganizer.Tracking.domain.helper;

/// <summary>
/// One minute of one tracked item, as the timeline builder sees it. <see cref="Key"/> is what the
/// primary lane switches between — the domain, the process name, never the URL or window title —
/// and <see cref="Label"/> is the display name carried alongside it (URL / product name), which
/// takes no part in any decision.
/// </summary>
public readonly record struct ActivityMinute(DateTime WindowStart, string Key, string? Label, int Seconds);

/// <summary>
/// A stitched run of adjacent <see cref="ActivityMinute"/>s on one <see cref="Key"/>. The
/// source-specific timeline DTOs are projections of this; nothing here knows about domains,
/// processes or packages.
/// </summary>
public sealed record TimelineSegment
{
    public required string Key { get; set; }
    public string? Label { get; set; }
    public required DateTime StartedAt { get; set; }
    public required DateTime EndedAt { get; set; }

    /// <summary>Wall-clock length of the run, <c>EndedAt - StartedAt</c>.</summary>
    public required int DurationSeconds { get; set; }

    /// <summary>Tracked activity seconds inside the run, which can be less than <see cref="DurationSeconds"/>.</summary>
    public required int TotalSeconds { get; set; }
}

/// <summary>
/// The timeline session algorithm, in one copy.
///
/// <para>It was two: <c>WebExtensionTimelineEndpoint</c> and <c>DesktopTimelineEndpoint</c> each
/// carried their own ~250-line transcription of it, differing only in which string field they keyed
/// on. That was survivable while the timeline was the only reader. It stopped being survivable when
/// the focus-metrics dashboards had to report on <b>the same</b> primary sessions the timeline
/// draws: a fragmentation number computed from a second, drifted transcription of this algorithm is
/// wrong in a way no test on either endpoint alone would show — both would pass, and the strip would
/// simply disagree with the chart above it.</para>
/// </summary>
public static class TimelineSegmentBuilder
{
    /// <summary>Minutes either side that vote on who owns the current minute.</summary>
    public const int ContextWindowRadius = 2;

    /// <summary>An interruption no longer than this is absorbed into the surrounding primary session.</summary>
    public const int ContextSwitchThresholdMinutes = 2;

    /// <summary>Detail and background runs shorter than this are dropped as noise.</summary>
    public const int MinDetailSeconds = 60;

    /// <summary>
    /// Builds the primary and detail lanes.
    ///
    /// <list type="bullet">
    /// <item><b>Primary</b> — the dominant key per minute, decided by a ±<see cref="ContextWindowRadius"/>
    /// minute vote, so a one-minute glance inside a longer run does not break it.</item>
    /// <item><b>Detail</b> — concurrent secondary activity, plus the short switches the primary lane
    /// absorbed.</item>
    /// </list>
    ///
    /// <para>Minutes with no seconds on them are the caller's to exclude; everything passed in is
    /// treated as real activity.</para>
    /// </summary>
    public static (List<TimelineSegment> Primary, List<TimelineSegment> Detail) Build(
        IEnumerable<ActivityMinute> minutes)
    {
        var minuteMap = minutes
            .Where(m => m.Seconds > 0)
            .GroupBy(m => m.WindowStart)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(m => m.Key, StringComparer.Ordinal).ToList());

        var sortedMinutes = minuteMap
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => kv.Key)
            .OrderBy(k => k)
            .ToList();

        var primaryMinutes = new List<ActivityMinute>();
        var secondaryMinutes = new List<ActivityMinute>();

        for (var i = 0; i < sortedMinutes.Count; i++)
        {
            var currentMinute = sortedMinutes[i];
            var currentRecords = minuteMap[currentMinute];

            // Score every key seen in the neighbourhood. The index walk is over minutes that exist
            // rather than over the clock, so the explicit distance check is what keeps a neighbour on
            // the far side of an untracked hour from voting.
            var contextScores = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var offset = -ContextWindowRadius; offset <= ContextWindowRadius; offset++)
            {
                var idx = i + offset;
                if (idx < 0 || idx >= sortedMinutes.Count)
                    continue;

                var neighborMinute = sortedMinutes[idx];

                if (Math.Abs((neighborMinute - currentMinute).TotalMinutes) > ContextWindowRadius)
                    continue;

                foreach (var record in minuteMap[neighborMinute])
                {
                    contextScores.TryAdd(record.Key, 0);
                    contextScores[record.Key] += record.Seconds;
                }
            }

            // Stable ordering, so a tie falls to the ordinally first key rather than to whatever the
            // database happened to return first.
            var ordered = currentRecords
                .OrderByDescending(r => contextScores.GetValueOrDefault(r.Key, 0))
                .ThenByDescending(r => r.Seconds)
                .ToList();

            primaryMinutes.Add(ordered[0]);
            secondaryMinutes.AddRange(ordered.Skip(1));
        }

        var primary = Stitch(primaryMinutes);
        var absorbed = AbsorbContextSwitches(primary, ContextSwitchThresholdMinutes);

        var detail = Stitch(secondaryMinutes);
        detail.AddRange(absorbed);
        detail = MergeAdjacent(detail);
        detail = detail.Where(s => s.TotalSeconds >= MinDetailSeconds).ToList();

        return (primary, detail);
    }

    /// <summary>
    /// The background lane: one set of runs per key, overlapping freely between keys, with short gaps
    /// inside a single key bridged. Callers pass the seconds they consider background.
    /// </summary>
    public static List<TimelineSegment> BuildBackground(IEnumerable<ActivityMinute> minutes)
    {
        var segments = new List<TimelineSegment>();

        foreach (var group in minutes.Where(m => m.Seconds > 0).GroupBy(m => m.Key, StringComparer.Ordinal))
            segments.AddRange(BridgeGaps(Stitch(group.OrderBy(m => m.WindowStart).ToList())));

        return segments
            .Where(s => s.TotalSeconds >= MinDetailSeconds)
            .OrderBy(s => s.StartedAt)
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Merges chronologically adjacent same-key minutes into runs.</summary>
    private static List<TimelineSegment> Stitch(List<ActivityMinute> minutes)
    {
        var segments = new List<TimelineSegment>();
        TimelineSegment? current = null;

        foreach (var minute in minutes.OrderBy(m => m.WindowStart).ThenBy(m => m.Key, StringComparer.Ordinal))
        {
            var minuteEnd = minute.WindowStart.AddMinutes(1);

            if (current != null && current.Key == minute.Key && minute.WindowStart == current.EndedAt)
            {
                current.EndedAt = minuteEnd;
                current.DurationSeconds = (int)(current.EndedAt - current.StartedAt).TotalSeconds;
                current.TotalSeconds += minute.Seconds;
                current.Label ??= minute.Label;
            }
            else
            {
                if (current != null)
                    segments.Add(current);

                current = new TimelineSegment
                {
                    Key = minute.Key,
                    Label = minute.Label,
                    StartedAt = minute.WindowStart,
                    EndedAt = minuteEnd,
                    DurationSeconds = 60,
                    TotalSeconds = minute.Seconds
                };
            }
        }

        if (current != null)
            segments.Add(current);

        return segments;
    }

    /// <summary>
    /// Merges same-key primary runs separated by a short interruption, and hands the interrupting runs
    /// back for the detail lane. Looks up to three runs ahead so an A → B → C → A detour is caught.
    /// </summary>
    private static List<TimelineSegment> AbsorbContextSwitches(List<TimelineSegment> segments, int thresholdMinutes)
    {
        var absorbed = new List<TimelineSegment>();
        bool merged;

        do
        {
            merged = false;

            for (var i = 0; i < segments.Count; i++)
            {
                for (var j = i + 1; j < Math.Min(i + 4, segments.Count); j++)
                {
                    if (!string.Equals(segments[j].Key, segments[i].Key, StringComparison.Ordinal))
                        continue;

                    var gapDuration = 0;
                    for (var k = i + 1; k < j; k++)
                        gapDuration += segments[k].DurationSeconds;

                    if (gapDuration > thresholdMinutes * 60)
                        continue;

                    for (var k = i + 1; k < j; k++)
                        absorbed.Add(segments[k]);

                    segments[i] = segments[i] with
                    {
                        EndedAt = segments[j].EndedAt,
                        DurationSeconds = (int)(segments[j].EndedAt - segments[i].StartedAt).TotalSeconds,
                        TotalSeconds = segments[i].TotalSeconds + segments[j].TotalSeconds,
                        Label = segments[i].Label ?? segments[j].Label
                    };

                    segments.RemoveRange(i + 1, j - i);
                    merged = true;
                    break;
                }

                if (merged)
                    break;
            }
        } while (merged);

        return absorbed;
    }

    /// <summary>Merges overlapping or touching same-key runs into one.</summary>
    private static List<TimelineSegment> MergeAdjacent(List<TimelineSegment> segments)
    {
        if (segments.Count == 0)
            return segments;

        segments = segments.OrderBy(s => s.StartedAt).ThenBy(s => s.Key, StringComparer.Ordinal).ToList();
        var result = new List<TimelineSegment> { segments[0] };

        for (var i = 1; i < segments.Count; i++)
        {
            var last = result[^1];
            var current = segments[i];

            if (string.Equals(last.Key, current.Key, StringComparison.Ordinal) && current.StartedAt <= last.EndedAt)
            {
                var newEnd = current.EndedAt > last.EndedAt ? current.EndedAt : last.EndedAt;
                result[^1] = last with
                {
                    EndedAt = newEnd,
                    DurationSeconds = (int)(newEnd - last.StartedAt).TotalSeconds,
                    TotalSeconds = last.TotalSeconds + current.TotalSeconds,
                    Label = last.Label ?? current.Label
                };
            }
            else
            {
                result.Add(current);
            }
        }

        return result;
    }

    /// <summary>Joins same-key runs separated by no more than the context-switch threshold.</summary>
    private static List<TimelineSegment> BridgeGaps(List<TimelineSegment> segments)
    {
        if (segments.Count <= 1)
            return segments;

        var result = new List<TimelineSegment> { segments[0] };

        for (var i = 1; i < segments.Count; i++)
        {
            var last = result[^1];
            var current = segments[i];

            if ((current.StartedAt - last.EndedAt).TotalMinutes <= ContextSwitchThresholdMinutes)
                result[^1] = last with
                {
                    EndedAt = current.EndedAt,
                    DurationSeconds = (int)(current.EndedAt - last.StartedAt).TotalSeconds,
                    TotalSeconds = last.TotalSeconds + current.TotalSeconds,
                    Label = last.Label ?? current.Label
                };
            else
                result.Add(current);
        }

        return result;
    }
}
