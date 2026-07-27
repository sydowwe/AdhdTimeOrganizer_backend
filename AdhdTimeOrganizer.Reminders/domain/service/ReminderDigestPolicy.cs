using AdhdTimeOrganizer.Reminders.infrastructure.scheduling;

namespace AdhdTimeOrganizer.Reminders.domain.service;

/// <summary>
/// Pure digest-flush math (phase 04b), separated from the scanner so it can be unit-tested without a DB or host.
/// Given a key's <see cref="ReminderDigestWindow"/> and the earliest due occurrence in a digest group, decides
/// whether the group should flush <i>now</i> (one notification + the per-occurrence rows) or wait for the next
/// scan to reconsider it.
/// <para>
/// Three shapes: a <c>null</c> window (no config for the key) and a non-positive
/// <see cref="ReminderDigestWindow.WindowMinutes"/> both mean <b>batch-per-run</b> (always flush); a positive
/// <c>WindowMinutes</c> is a rolling wait since the earliest occurrence; a
/// <see cref="ReminderDigestWindow.DailyAnchorMinute"/> (which wins if both are set) is a fixed daily local
/// anchor time. The whole group shares one window because the window is a property of the <c>DigestKey</c>.
/// </para>
/// </summary>
public static class ReminderDigestPolicy
{
    public const int MinutesPerDay = 24 * 60;

    /// <summary>
    /// True when a digest group whose earliest due occurrence is <paramref name="earliestOccurrence"/> should
    /// flush at the scan instant <paramref name="now"/>. Daily-anchor windows are evaluated in
    /// <paramref name="timeZone"/>; rolling/batch-per-run windows are time-zone-independent.
    /// </summary>
    public static bool ShouldFlush(
        ReminderDigestWindow? window, DateTimeOffset earliestOccurrence, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        if (window is null)
            return true; // no config for this key ⇒ batch-per-run

        if (window.DailyAnchorMinute is { } anchorMinute)
        {
            // Flush once per day: on the first scan at/after the day's anchor, for occurrences that came due
            // at/before that anchor instant. Occurrences arriving after today's anchor wait for tomorrow's.
            var anchor = MostRecentAnchorInstant(anchorMinute, now, timeZone);
            return earliestOccurrence <= anchor;
        }

        if (window.WindowMinutes <= 0)
            return true; // batch-per-run

        return now - earliestOccurrence >= TimeSpan.FromMinutes(window.WindowMinutes);
    }

    /// <summary>
    /// The most recent daily-anchor instant at or before <paramref name="now"/> — today's anchor if
    /// <paramref name="now"/> is already past it, otherwise yesterday's. <paramref name="anchorMinute"/> is
    /// minutes from local midnight in <paramref name="timeZone"/>.
    /// </summary>
    private static DateTimeOffset MostRecentAnchorInstant(int anchorMinute, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var anchorLocal = DateTime.SpecifyKind(localNow.Date.AddMinutes(anchorMinute), DateTimeKind.Unspecified);
        var anchorInstant = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(anchorLocal, timeZone), TimeSpan.Zero);

        if (now < anchorInstant)
        {
            anchorLocal = anchorLocal.AddDays(-1);
            anchorInstant = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(anchorLocal, timeZone), TimeSpan.Zero);
        }

        return anchorInstant;
    }
}