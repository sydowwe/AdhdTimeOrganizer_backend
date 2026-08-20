using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Tracking.domain.helper;

/// <summary>
/// One calendar day's slice of a dashboard request: the UTC instants that the user's time-of-day
/// window names on <see cref="Day"/>.
/// </summary>
public readonly record struct DailyWindow(DateOnly Day, DateTime From, DateTime To);

/// <summary>
/// A bucket the stacked-bars dashboards aggregate into, as the half-open UTC instant range
/// <c>[Start, End)</c> that the client draws its axis from.
/// </summary>
public readonly record struct AggregationWindow(DateTime Start, DateTime End);

/// <summary>
/// The set of per-day windows a dashboard request names.
///
/// <para><b>A time-of-day window repeated on every day of the span — not one continuous block.</b> With
/// a 09:00–17:00 window over a week, the seven nights in between are <i>not</i> in the range; a
/// continuous reading would silently fold them in and roughly triple every number on the page. The
/// past-midnight rule of <see cref="Sydowwe.Framework.application.dto.dto.DateAndTimeRangeDto"/> is
/// unchanged and applies per day: a <c>to</c> at or before <c>from</c> ends on the following calendar
/// day, so <c>to == from</c> is the full 24 hours of each day rather than nothing.</para>
///
/// <para>A single-day request (<c>dateFrom == dateTo</c>) resolves to exactly one window, which is the
/// window the single-<c>date</c> endpoints produced before the span existed.</para>
/// </summary>
public sealed class DailyWindowSet
{
    private const int MinutesPerDay = 24 * 60;

    private readonly TimeOnly _fromTime;
    private readonly TimeOnly _toTime;
    private readonly TimeZoneInfo _timeZone;
    private readonly DailyWindow[] _windows;

    private DailyWindowSet(DailyWindow[] windows, TimeOnly fromTime, TimeOnly toTime, TimeZoneInfo timeZone)
    {
        _windows = windows;
        _fromTime = fromTime;
        _toTime = toTime;
        _timeZone = timeZone;
    }

    public IReadOnlyList<DailyWindow> Windows => _windows;

    /// <summary>Number of calendar days in the span — the divisor/multiplier for any per-day average.</summary>
    public int DayCount => _windows.Length;

    /// <summary>
    /// The outer bounds of the whole span, <c>[EnvelopeFrom, EnvelopeTo)</c>. This is what the database
    /// is asked for: one range predicate keeps the read index- and partition-friendly, and the nights
    /// the daily window excludes are dropped afterwards by <see cref="Contains"/>. When the window is a
    /// full 24 hours the two are the same thing and nothing is dropped.
    /// </summary>
    public DateTime EnvelopeFrom => _windows[0].From;

    public DateTime EnvelopeTo => _windows[^1].To;

    /// <summary>
    /// Resolves <paramref name="dateFrom"/>..<paramref name="dateTo"/> (both inclusive) against the
    /// per-day window <paramref name="fromTime"/>..<paramref name="toTime"/> on the clocks of
    /// <paramref name="timeZone"/>, which must be the requesting user's own zone.
    /// </summary>
    public static DailyWindowSet Resolve(
        DateOnly dateFrom,
        DateOnly dateTo,
        TimeOnly fromTime,
        TimeOnly toTime,
        TimeZoneInfo timeZone)
    {
        if (dateTo < dateFrom)
            dateTo = dateFrom;

        var dayCount = dateTo.DayNumber - dateFrom.DayNumber + 1;
        var windows = new DailyWindow[dayCount];

        for (var i = 0; i < dayCount; i++)
        {
            var day = dateFrom.AddDays(i);
            windows[i] = new DailyWindow(
                day,
                WallClockZone.ToUtc(day, fromTime, timeZone),
                WallClockZone.ToUtc(EndWallClock(day, fromTime, toTime), timeZone));
        }

        return new DailyWindowSet(windows, fromTime, toTime, timeZone);
    }

    /// <summary>
    /// The same set, recovered from the outer envelope a caller computed as "<c>dateFrom</c> at
    /// <paramref name="fromTime"/> through <c>dateTo</c> at <paramref name="toTime"/>" — the shape the
    /// details endpoints receive, because they were built to take instants and their callers hand them
    /// the envelope of whatever span is on screen.
    ///
    /// <para>Without this the details panel over-counts against the pie slice it was opened from: the
    /// envelope covers the nights the daily window excludes, so a user on a 09:00–17:00 window sees a
    /// breakdown roughly three times the slice it is meant to explain.</para>
    /// </summary>
    public static DailyWindowSet FromEnvelope(
        DateTime envelopeFrom,
        DateTime envelopeTo,
        TimeOnly fromTime,
        TimeOnly toTime,
        TimeZoneInfo timeZone)
    {
        var dateFrom = DateOnly.FromDateTime(WallClockZone.FromUtc(envelopeFrom, timeZone));
        var lastWallDay = DateOnly.FromDateTime(WallClockZone.FromUtc(envelopeTo, timeZone));

        // An over-midnight window ends on the day after the span's last, so the envelope's far end is
        // one calendar day past dateTo.
        var dateTo = toTime <= fromTime ? lastWallDay.AddDays(-1) : lastWallDay;

        return Resolve(dateFrom, dateTo, fromTime, toTime, timeZone);
    }

    /// <summary>
    /// Whether <paramref name="instant"/> falls inside one of the daily windows. Binary search rather
    /// than a scan because this is asked once per ledger row, and a month of one-minute rows is tens of
    /// thousands of them.
    /// </summary>
    public bool Contains(DateTime instant)
    {
        if (instant < EnvelopeFrom || instant >= EnvelopeTo)
            return false;

        var lo = 0;
        var hi = _windows.Length - 1;

        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var window = _windows[mid];

            if (instant < window.From)
                hi = mid - 1;
            else if (instant >= window.To)
                lo = mid + 1;
            else
                return true;
        }

        return false;
    }

    /// <summary>
    /// Drops the rows the envelope read picked up from the gaps between the daily windows. Keeps
    /// everything on a full-24-hour window, where there are no gaps.
    /// </summary>
    public List<T> Restrict<T>(IEnumerable<T> rows, Func<T, DateTime> instant) =>
        rows.Where(row => Contains(instant(row))).ToList();

    /// <summary>
    /// The buckets a stacked-bars response is made of, in chronological order and never overlapping.
    ///
    /// <list type="bullet">
    /// <item><b>Under a day</b> — buckets tile <i>each day's</i> time-of-day window, starting at
    /// <c>from</c> on that day. The day's last bucket is truncated at <c>to</c> instead of spilling into
    /// the next day's window, so a 07:00–00:00 window at 480 minutes gives 480 + 480 + 60 and never a
    /// bucket straddling two days.</item>
    /// <item><b>A day or more</b> — buckets tile the <i>span</i> in whole days, starting at
    /// <c>dateFrom</c>; the last is truncated at <c>dateTo</c>. Such a bucket still holds only time
    /// inside the daily window, so a 1440-minute bucket over a 07:00–00:00 setting carries at most 17
    /// hours of activity.</item>
    /// </list>
    ///
    /// <para>Both bounds are resolved through the zone in their own right rather than the end being the
    /// start plus the bucket width, so a bucket containing a DST transition covers the wall clock it
    /// claims to.</para>
    /// </summary>
    public IReadOnlyList<AggregationWindow> Tile(int windowMinutes)
    {
        if (windowMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowMinutes));

        return windowMinutes >= MinutesPerDay ? TileWholeDays(windowMinutes) : TileWithinDays(windowMinutes);
    }

    /// <summary>
    /// The index of the bucket <paramref name="instant"/> belongs to, or <c>-1</c> when it falls in none
    /// — which for rows already passed through <see cref="Restrict{T}"/> cannot happen, because the
    /// buckets tile the daily windows exactly.
    /// </summary>
    public static int BucketIndexOf(IReadOnlyList<AggregationWindow> buckets, DateTime instant)
    {
        var lo = 0;
        var hi = buckets.Count - 1;

        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var bucket = buckets[mid];

            if (instant < bucket.Start)
                hi = mid - 1;
            else if (instant >= bucket.End)
                lo = mid + 1;
            else
                return mid;
        }

        return -1;
    }

    private IReadOnlyList<AggregationWindow> TileWholeDays(int windowMinutes)
    {
        var daysPerBucket = windowMinutes / MinutesPerDay;
        var buckets = new List<AggregationWindow>();

        for (var i = 0; i < _windows.Length; i += daysPerBucket)
        {
            var last = Math.Min(i + daysPerBucket, _windows.Length) - 1;
            buckets.Add(new AggregationWindow(_windows[i].From, _windows[last].To));
        }

        return buckets;
    }

    private IReadOnlyList<AggregationWindow> TileWithinDays(int windowMinutes)
    {
        var buckets = new List<AggregationWindow>();

        foreach (var window in _windows)
        {
            var dayStartWall = window.Day.ToDateTime(_fromTime);
            var dayEndWall = EndWallClock(window.Day, _fromTime, _toTime);

            for (var startWall = dayStartWall; startWall < dayEndWall; startWall = startWall.AddMinutes(windowMinutes))
            {
                var endWall = startWall.AddMinutes(windowMinutes);
                if (endWall > dayEndWall)
                    endWall = dayEndWall;

                buckets.Add(new AggregationWindow(
                    WallClockZone.ToUtc(startWall, _timeZone),
                    WallClockZone.ToUtc(endWall, _timeZone)));
            }
        }

        return buckets;
    }

    /// <summary>
    /// Where the day's window ends on the user's wall clock. <c>to</c> at or before <c>from</c> is a
    /// window over midnight and ends on the following day — which is also what makes <c>to == from</c>
    /// the full 24 hours rather than an empty window.
    /// </summary>
    private static DateTime EndWallClock(DateOnly day, TimeOnly fromTime, TimeOnly toTime) =>
        toTime <= fromTime ? day.AddDays(1).ToDateTime(toTime) : day.ToDateTime(toTime);
}
