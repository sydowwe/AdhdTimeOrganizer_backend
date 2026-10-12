namespace AdhdTimeOrganizer.Tracking.domain.helper;

/// <summary>
/// P2-04: when a tracker counts as gone quiet, in one place. A source is <b>silent</b> when it reported within
/// <see cref="ReportedWithin"/> but not for <see cref="SilentAfter"/>, and the silence is worth saying only while the
/// user is around — the web app saw them within <see cref="PresentWithin"/> — because "the person is here and the
/// tracker isn't" is the one case nothing else surfaces. A tracker gone longer than <see cref="ReportedWithin"/> was
/// most likely put away on purpose, and an away user has nobody to tell.
/// </summary>
public static class TrackerSilenceRule
{
    /// <summary>How often a source's last-seen stamp is written at most. Heartbeats come once a minute per tracker; the
    /// rule's thresholds are days, so an hour's precision costs nothing.</summary>
    public static readonly TimeSpan StampInterval = TimeSpan.FromHours(1);

    /// <summary>No report for this long and the source is silent. Two days, so a weekend away from the desk with the
    /// laptop shut does not read as a broken agent before Monday.</summary>
    public static readonly TimeSpan SilentAfter = TimeSpan.FromHours(48);

    /// <summary>A source last heard from longer ago than this is retired, not silent: nothing is said about it.</summary>
    public static readonly TimeSpan ReportedWithin = TimeSpan.FromDays(30);

    /// <summary>The user counts as around when <c>User.LastActiveAt</c> is at most this old.</summary>
    public static readonly TimeSpan PresentWithin = TimeSpan.FromHours(24);

    /// <summary>
    /// Whether <paramref name="localTime"/> falls in the user's waking day, <paramref name="wakeUp"/> up to
    /// <paramref name="bedTime"/>. A bed time at or before the wake-up wraps past midnight — and the default 00:00 is
    /// "end of the day", so the day then runs from wake-up to midnight.
    /// </summary>
    public static bool IsAwake(TimeOnly localTime, TimeOnly wakeUp, TimeOnly bedTime) =>
        bedTime > wakeUp
            ? localTime >= wakeUp && localTime < bedTime
            : localTime >= wakeUp || localTime < bedTime;
}
