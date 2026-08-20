using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.helper.unified;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Tracking.application.service.unified;

/// <summary>
/// A run of one item on one source that the ledger itself considers continuous — an android session as
/// stored, or a stretch of adjacent one-minute rows on the same label.
///
/// <para>It exists only so <c>pie-chart</c>'s <c>totalSessions</c> can count <b>a session the overlap
/// rule split once</b>. Counting the merged runs instead would report a session that lost a minute in
/// the middle as two, which is an artefact of the merge rather than anything the user did.</para>
/// </summary>
public sealed record SourceSessionRun(TrackingSource Source, string Label, IReadOnlyList<DateTime> Minutes);

/// <summary>Everything one ledger contributes to a request, before any of it is merged.</summary>
public sealed class SourceLoad
{
    public required TrackingSource Source { get; init; }

    /// <summary>
    /// Whether this source recorded <b>anything at all</b> in the span — before de-overlapping and
    /// regardless of selection. A source whose every second was displaced still has data, and telling
    /// the user it is "not connected" would hide a real finding.
    /// </summary>
    public required bool HasData { get; init; }

    /// <summary>
    /// What this source's <b>own</b> dashboard reports for the same span. The unified page prints
    /// <c>countedSeconds</c> and <c>displacedSeconds</c> side by side so the sum can be checked against
    /// it, so this figure has to be computed the way that dashboard computes it, not a tidier way.
    /// </summary>
    public required double RawTotalSeconds { get; init; }

    public required IReadOnlyList<SourceMinute> Minutes { get; init; }
    public required IReadOnlyList<SourceSessionRun> Runs { get; init; }
}

/// <summary>
/// Flattens the three ledgers onto one minute grid, which is the shape
/// <see cref="UnifiedMinuteMerger"/> resolves overlap over.
///
/// <para>Every read follows the same pattern as the per-source dashboards: one range predicate over the
/// span's outer envelope, then the nights between the daily windows dropped in memory. Reading the
/// envelope alone still answers 200 and simply folds every excluded night into the totals.</para>
/// </summary>
public static class UnifiedActivityLoader
{
    /// <summary>
    /// Loads exactly the sources asked for. <c>/sources</c> asks for all three because its
    /// <c>hasData</c> is independent of selection; the other five ask only for the selected ones,
    /// because a deselected source must contribute nothing and displace nothing.
    /// </summary>
    public static async Task<IReadOnlyList<SourceLoad>> LoadAsync(
        DbContext db,
        long userId,
        DailyWindowSet windows,
        IReadOnlyCollection<TrackingSource> sources,
        CancellationToken ct)
    {
        var loads = new List<SourceLoad>(sources.Count);

        if (sources.Contains(TrackingSource.WebExtension))
            loads.Add(await LoadWebExtensionAsync(db, userId, windows, ct));

        if (sources.Contains(TrackingSource.Desktop))
            loads.Add(await LoadDesktopAsync(db, userId, windows, ct));

        if (sources.Contains(TrackingSource.Android))
            loads.Add(await LoadAndroidAsync(db, userId, windows, ct));

        Canonicalise(loads);

        return loads;
    }

    /// <summary>
    /// The earliest day any of <paramref name="sources"/> recorded on, on the user's own clock — the
    /// <c>allTime</c> baseline's far edge. <c>null</c> when the user has no history on any of them.
    /// </summary>
    public static async Task<DateOnly?> FirstActivityDayAsync(
        DbContext db,
        long userId,
        IReadOnlyCollection<TrackingSource> sources,
        TimeZoneInfo timeZone,
        CancellationToken ct)
    {
        DateTime? earliest = null;

        if (sources.Contains(TrackingSource.WebExtension))
            earliest = Earlier(earliest, await db.Set<WebExtensionActivityEntry>()
                .Where(x => x.UserId == userId)
                .MinAsync(x => (DateTime?)x.WindowStart, ct));

        if (sources.Contains(TrackingSource.Desktop))
            earliest = Earlier(earliest, await db.Set<DesktopActivityEntry>()
                .Where(x => x.UserId == userId)
                .MinAsync(x => (DateTime?)x.WindowStart, ct));

        if (sources.Contains(TrackingSource.Android))
            earliest = Earlier(earliest, await db.Set<AndroidSessionData>()
                .Where(x => x.UserId == userId)
                .MinAsync(x => (DateTime?)x.SessionStartUtc, ct));

        return earliest.HasValue
            ? DateOnly.FromDateTime(WallClockZone.FromUtc(earliest.Value, timeZone))
            : null;
    }

    // ---- per source ------------------------------------------------------

    private static async Task<SourceLoad> LoadWebExtensionAsync(
        DbContext db, long userId, DailyWindowSet windows, CancellationToken ct)
    {
        var from = windows.EnvelopeFrom;
        var to = windows.EnvelopeTo;

        var rows = windows.Restrict(
            await db.Set<WebExtensionActivityEntry>()
                .Where(x => x.UserId == userId)
                .Where(x => x.WindowStart >= from && x.WindowStart < to)
                .OrderBy(x => x.WindowStart)
                .ToListAsync(ct),
            x => x.WindowStart);

        var minutes = rows
            .Select(r => new SourceMinute(
                r.WindowStart,
                TrackingSource.WebExtension,
                UnifiedLabelResolver.ForWebExtension(r.Domain),
                r.Url,
                r.ActiveSeconds,
                r.BackgroundSeconds))
            .ToList();

        return new SourceLoad
        {
            Source = TrackingSource.WebExtension,
            HasData = rows.Count > 0,
            RawTotalSeconds = rows.Sum(r => (double)(r.ActiveSeconds + r.BackgroundSeconds)),
            Minutes = minutes,
            Runs = StitchRuns(minutes)
        };
    }

    private static async Task<SourceLoad> LoadDesktopAsync(
        DbContext db, long userId, DailyWindowSet windows, CancellationToken ct)
    {
        var from = windows.EnvelopeFrom;
        var to = windows.EnvelopeTo;

        var rows = windows.Restrict(
            await db.Set<DesktopActivityEntry>()
                .Where(x => x.UserId == userId)
                .Where(x => x.WindowStart >= from && x.WindowStart < to)
                .OrderBy(x => x.WindowStart)
                .ToListAsync(ct),
            x => x.WindowStart);

        var minutes = rows
            .Select(r => new SourceMinute(
                r.WindowStart,
                TrackingSource.Desktop,
                UnifiedLabelResolver.ForDesktop(r.ProcessName, r.ProductName),
                // No detail string: the window title is free text a user did not choose to publish, and
                // the merged timeline has nowhere to show it anyway.
                null,
                r.ActiveSeconds,
                r.BackgroundSeconds))
            .ToList();

        return new SourceLoad
        {
            Source = TrackingSource.Desktop,
            HasData = rows.Count > 0,
            RawTotalSeconds = rows.Sum(r => (double)(r.ActiveSeconds + r.BackgroundSeconds)),
            Minutes = minutes,
            Runs = StitchRuns(minutes)
        };
    }

    /// <summary>
    /// Android stores real sessions rather than per-minute rows, so each one is spread over the minutes
    /// it covers, weighted by how much of each minute it occupies.
    ///
    /// <para>A session is filed under the day its <b>start</b> falls in — matching the android
    /// dashboards — and its <b>whole</b> duration is spread over that day's window even where the
    /// session runs past the window's edge. Dropping the overhang instead would make
    /// <c>countedSeconds + displacedSeconds</c> disagree with the android pie chart for the same span,
    /// which is the one arithmetic check the unified page invites the user to perform.</para>
    /// </summary>
    private static async Task<SourceLoad> LoadAndroidAsync(
        DbContext db, long userId, DailyWindowSet windows, CancellationToken ct)
    {
        var from = windows.EnvelopeFrom;
        var to = windows.EnvelopeTo;

        var sessions = windows.Restrict(
            await db.Set<AndroidSessionData>()
                .Where(x => x.UserId == userId)
                .Where(x => x.SessionStartUtc >= from && x.SessionStartUtc < to)
                .OrderBy(x => x.SessionStartUtc)
                .ToListAsync(ct),
            x => x.SessionStartUtc);

        var minutes = new List<SourceMinute>();
        var runs = new List<SourceSessionRun>();

        foreach (var session in sessions)
        {
            var label = UnifiedLabelResolver.ForAndroid(session.PackageName, session.AppLabel);
            var window = WindowOf(windows, session.SessionStartUtc);

            var start = session.SessionStartUtc;
            var end = session.SessionEndUtc > window.To ? window.To : session.SessionEndUtc;
            if (end < start)
                end = start;

            var slices = SliceIntoMinutes(start, end);
            var weightTotal = slices.Sum(s => s.Weight);
            var covered = new List<DateTime>(slices.Count);

            foreach (var slice in slices)
            {
                var seconds = weightTotal > 0
                    ? session.DurationSeconds * slice.Weight / weightTotal
                    : session.DurationSeconds;

                minutes.Add(new SourceMinute(
                    slice.Minute, TrackingSource.Android, label, null, seconds, 0));

                covered.Add(slice.Minute);
            }

            runs.Add(new SourceSessionRun(TrackingSource.Android, label, covered));
        }

        return new SourceLoad
        {
            Source = TrackingSource.Android,
            HasData = sessions.Count > 0,
            RawTotalSeconds = sessions.Sum(s => (double)s.DurationSeconds),
            Minutes = minutes,
            Runs = runs
        };
    }

    // ---- shared ----------------------------------------------------------

    /// <summary>
    /// Applies the cross-source join of <see cref="UnifiedLabelResolver"/> to every contribution at
    /// once, before anything downstream groups, sums or colours by label. Doing it per endpoint would
    /// let the pie and the timeline pick different spellings for one application.
    /// </summary>
    private static void Canonicalise(List<SourceLoad> loads)
    {
        var canonical = UnifiedLabelResolver.BuildCanonicalSpellings(loads.SelectMany(l => l.Minutes));

        for (var i = 0; i < loads.Count; i++)
        {
            var load = loads[i];

            loads[i] = new SourceLoad
            {
                Source = load.Source,
                HasData = load.HasData,
                RawTotalSeconds = load.RawTotalSeconds,
                Minutes = load.Minutes.Select(m => m with { Label = canonical[m.Label] }).ToList(),
                Runs = load.Runs.Select(r => r with { Label = canonical[r.Label] }).ToList()
            };
        }
    }

    /// <summary>
    /// Adjacent one-minute rows on the same label, as one run. A gap of a single minute ends the run —
    /// this is the ledger's own idea of continuity, not the timeline's context-aware one, because the
    /// only question it answers is how many things there were to count.
    /// </summary>
    private static List<SourceSessionRun> StitchRuns(List<SourceMinute> minutes)
    {
        var runs = new List<SourceSessionRun>();

        foreach (var group in minutes
                     .Where(m => m.TotalSeconds > 0)
                     .GroupBy(m => m.Label, StringComparer.Ordinal))
        {
            var ordered = group.Select(m => m.Minute).Distinct().OrderBy(m => m).ToList();
            var current = new List<DateTime>();

            foreach (var minute in ordered)
            {
                if (current.Count > 0 && minute != current[^1].AddMinutes(1))
                {
                    runs.Add(new SourceSessionRun(group.First().Source, group.Key, current));
                    current = [];
                }

                current.Add(minute);
            }

            if (current.Count > 0)
                runs.Add(new SourceSessionRun(group.First().Source, group.Key, current));
        }

        return runs;
    }

    private static List<(DateTime Minute, double Weight)> SliceIntoMinutes(DateTime start, DateTime end)
    {
        var slices = new List<(DateTime Minute, double Weight)>();
        var minute = FloorToMinute(start);

        if (end <= start)
            return [(minute, 0)];

        while (minute < end)
        {
            var minuteEnd = minute.AddMinutes(1);
            var overlapFrom = start > minute ? start : minute;
            var overlapTo = end < minuteEnd ? end : minuteEnd;

            slices.Add((minute, (overlapTo - overlapFrom).TotalSeconds));
            minute = minuteEnd;
        }

        return slices;
    }

    private static DateTime FloorToMinute(DateTime instant) =>
        new(instant.Year, instant.Month, instant.Day, instant.Hour, instant.Minute, 0, instant.Kind);

    /// <summary>
    /// The daily window <paramref name="instant"/> falls in. Callers have already dropped the rows that
    /// fall in none, so the last window is a safe fallback rather than a real case.
    /// </summary>
    private static DailyWindow WindowOf(DailyWindowSet windows, DateTime instant)
    {
        var lo = 0;
        var hi = windows.Windows.Count - 1;

        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var window = windows.Windows[mid];

            if (instant < window.From)
                hi = mid - 1;
            else if (instant >= window.To)
                lo = mid + 1;
            else
                return window;
        }

        return windows.Windows[^1];
    }

    private static DateTime? Earlier(DateTime? current, DateTime? candidate)
    {
        if (!candidate.HasValue)
            return current;

        return current.HasValue && current.Value <= candidate.Value ? current : candidate;
    }
}
