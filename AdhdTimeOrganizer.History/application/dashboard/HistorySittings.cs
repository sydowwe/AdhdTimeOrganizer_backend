using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;

namespace AdhdTimeOrganizer.History.application.dashboard;

/// <summary>
/// The one rule for "how many entries": count <b>sittings</b>, not rows. A timer session is one sitting however
/// many phase rows it wrote; a row outside a session is a sitting of its own.
/// </summary>
/// <remarks>
/// <para>
/// Every count a client divides by or reads as "how many times" goes through here — <c>aggregate-by-activity</c>'s
/// <c>entryCount</c>, the pie charts' <c>entries</c> / <c>totalEntries</c>, and the calendar's
/// <c>sessionCount</c>. Counting rows there would read one four-period Pomodoro as four short entries: the
/// typical-time average drops to a quarter, and a focused morning becomes the period's "most broken up" activity.
/// </para>
/// <para>
/// Applied to a <b>subset</b> of rows, a session counts once in that subset if any of its rows are in it. So a
/// Pomodoro whose focus and rest activities land in two pie groups counts once in each, and the groups' entries
/// can sum to more than the total. That is intended; the total counts it once.
/// </para>
/// <para>
/// Deliberately <b>not</b> applied to <c>summary/time-of-day</c>'s per-hour <c>entries</c>, which counts row
/// overlaps with each hour and is documented as not a record count.
/// </para>
/// </remarks>
public static class HistorySittings
{
    public static int CountSittings(this IEnumerable<ActivityHistory> rows) => rows.CountSittings(r => r.SessionId);

    /// <inheritdoc cref="HistorySittings"/>
    /// <param name="sessionId">The row's session id, for callers that projected rows into something narrower.</param>
    public static int CountSittings<T>(this IEnumerable<T> rows, Func<T, long?> sessionId)
    {
        var standalone = 0;
        var sessions = new HashSet<long>();

        foreach (var row in rows)
        {
            if (sessionId(row) is { } id)
                sessions.Add(id);
            else
                standalone++;
        }

        return standalone + sessions.Count;
    }
}
