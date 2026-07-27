namespace AdhdTimeOrganizer.Reminders.infrastructure.scheduling;

/// <summary>
/// The digest-window configuration (phase 04b), bound from the "ReminderDigest" config section. The digest
/// window is a property of the <c>DigestKey</c>, <b>not</b> the reminder row: a digest aggregates across all
/// definitions sharing the key for one recipient, so the window can't live on a single definition. Each entry
/// in <see cref="Windows"/> maps a <c>DigestKey</c> to its batching window.
/// <para>
/// A <c>DigestKey</c> with <b>no</b> entry here defaults to <i>batch-per-run</i> — every scan flushes whatever
/// is currently due into one notification. Absent any digest keys at all the module behaves exactly as phase
/// 03 / 04a (one notification per occurrence); digest is strictly additive and opt-in.
/// </para>
/// </summary>
public sealed class ReminderDigestOptions
{
    public const string SectionName = "ReminderDigest";

    /// <summary>Per-<c>DigestKey</c> batching windows. Case-insensitive on the key.</summary>
    public Dictionary<string, ReminderDigestWindow> Windows { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The window configured for <paramref name="digestKey"/>, or <c>null</c> ⇒ batch-per-run.</summary>
    public ReminderDigestWindow? Resolve(string digestKey) => Windows.TryGetValue(digestKey, out var window) ? window : null;
}

/// <summary>
/// One digest key's batching window. Two mutually-exclusive shapes (a daily anchor wins if both are set):
/// a rolling <see cref="WindowMinutes"/> interval, or a <see cref="DailyAnchorMinute"/> fixed local time.
/// </summary>
public sealed class ReminderDigestWindow
{
    /// <summary>
    /// Rolling batching interval in minutes: a group flushes once its earliest due occurrence has waited at
    /// least this long. <c>0</c> (the default) means <i>batch-per-run</i> — flush every scan.
    /// </summary>
    public int WindowMinutes { get; set; }

    /// <summary>
    /// Optional fixed daily anchor as minutes from local midnight (<c>0..1439</c>, deployment time zone). When
    /// set it overrides <see cref="WindowMinutes"/>: a group flushes once per day, on the first scan at/after
    /// the day's anchor, for occurrences that came due at/before it (a "daily 09:00 digest").
    /// </summary>
    public int? DailyAnchorMinute { get; set; }
}