namespace MojaDigitalnaFirma.Kernel.notification;

/// <summary>
/// Pure quiet-hours window math, kept free of DB and DI so it can be unit-tested directly. Lives in the
/// Kernel because two modules reason about the same window: the Notifications dispatcher (defer background
/// channels, see <see cref="ResumeAt"/>) and the Reminders scan (defer a due occurrence, see
/// <see cref="IsWithin"/>). It was lifted here from <c>ReminderQuietHoursPolicy</c> when the window moved to
/// Notifications — do not re-fork it per module.
/// <para>
/// A window is a half-open minute-of-day span <c>[start, end)</c> in the deployment time zone;
/// <c>start &gt; end</c> denotes an overnight window that wraps past midnight (e.g. 22:00→06:00).
/// </para>
/// </summary>
public static class QuietHoursPolicy
{
    public const int MinutesPerDay = 24 * 60;

    /// <inheritdoc cref="IsWithin(int,int,DateTimeOffset,TimeZoneInfo)"/>
    public static bool IsWithin(QuietHoursWindow window, DateTimeOffset instant, TimeZoneInfo timeZone) => IsWithin(window.StartMinute, window.EndMinute, instant, timeZone);

    /// <summary>
    /// True when <paramref name="instant"/>, converted to <paramref name="timeZone"/>, falls inside the
    /// half-open quiet window <c>[startMinute, endMinute)</c>. A degenerate window (<c>start == end</c>) is
    /// treated as <b>no quiet hours</b> (always false) — the write endpoint rejects that input, but callers
    /// stay safe if it ever reaches the DB.
    /// </summary>
    public static bool IsWithin(int startMinute, int endMinute, DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        if (startMinute == endMinute)
            return false;

        var local = TimeZoneInfo.ConvertTime(instant, timeZone);
        var minuteOfDay = local.Hour * 60 + local.Minute;

        return startMinute < endMinute
            ? minuteOfDay >= startMinute && minuteOfDay < endMinute // same-day window
            : minuteOfDay >= startMinute || minuteOfDay < endMinute; // overnight (wraps midnight)
    }

    /// <inheritdoc cref="ResumeAt(int,int,DateTimeOffset,TimeZoneInfo)"/>
    public static DateTimeOffset? ResumeAt(QuietHoursWindow window, DateTimeOffset instant, TimeZoneInfo timeZone) => ResumeAt(window.StartMinute, window.EndMinute, instant, timeZone);

    /// <summary>
    /// The instant the window covering <paramref name="instant"/> ends — i.e. the earliest moment a deferred
    /// delivery may go out. Returns <c>null</c> when <paramref name="instant"/> is <b>not</b> inside the
    /// window (nothing to defer), so a caller can branch on the result alone rather than calling
    /// <see cref="IsWithin"/> first.
    /// </summary>
    public static DateTimeOffset? ResumeAt(int startMinute, int endMinute, DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        if (!IsWithin(startMinute, endMinute, instant, timeZone))
            return null;

        var local = TimeZoneInfo.ConvertTime(instant, timeZone);
        var minuteOfDay = local.Hour * 60 + local.Minute;

        // Same-day window ⇒ the end is later today (IsWithin guarantees minuteOfDay < endMinute). Overnight
        // window ⇒ the evening leg (minuteOfDay >= startMinute) ends tomorrow morning; the after-midnight leg
        // (minuteOfDay < endMinute) ends later the same day.
        var endsTomorrow = startMinute > endMinute && minuteOfDay >= startMinute;
        var endDate = local.Date.AddDays(endsTomorrow ? 1 : 0);

        return ToInstant(endDate.AddMinutes(endMinute), timeZone);
    }

    /// <summary>
    /// Resolves a local wall-clock reading to an absolute instant, surviving both DST transitions.
    /// </summary>
    private static DateTimeOffset ToInstant(DateTime localWallClock, TimeZoneInfo timeZone)
    {
        var wall = DateTime.SpecifyKind(localWallClock, DateTimeKind.Unspecified);

        // Spring forward: this wall-clock reading never happens (the clock jumps over it). Walk to the first
        // reading that does exist — the window ends the moment the clock reaches it, i.e. at the transition.
        while (timeZone.IsInvalidTime(wall))
            wall = wall.AddMinutes(1);

        // Fall back: the reading happens twice. Take the LATER of the two instants (the smaller UTC offset)
        // so the repeated hour never cuts a quiet window short — erring toward not waking the user.
        var offset = timeZone.IsAmbiguousTime(wall)
            ? timeZone.GetAmbiguousTimeOffsets(wall).Min()
            : timeZone.GetUtcOffset(wall);

        return new DateTimeOffset(wall, offset);
    }
}