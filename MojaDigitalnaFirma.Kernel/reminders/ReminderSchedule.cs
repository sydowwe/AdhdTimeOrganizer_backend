namespace MojaDigitalnaFirma.Kernel.reminders;

/// <summary>
/// When a reminder occurs: a one-shot deadline with lead-time offsets, a recurring interval, or a
/// recurring cron. Per-type fields are nullable and only meaningful for the matching
/// <see cref="ReminderScheduleType"/>; they are validated in phase 02.
/// <para>
/// Modelled as one sealed class with per-type nullable fields + factory methods, mirroring how the repo
/// already models a discriminated schedule payload (<c>Kernel.scheduling.ScheduleSpec</c>) rather than a
/// type hierarchy. Construct via <see cref="OneShot"/> / <see cref="RecurringInterval"/> /
/// <see cref="RecurringCron"/> rather than the initializer.
/// </para>
/// </summary>
public sealed class ReminderSchedule
{
    public ReminderScheduleType Type { get; private init; }

    /// <summary>The deadline. Set only when <see cref="Type"/> is <see cref="ReminderScheduleType.OneShot"/>.</summary>
    public DateTimeOffset? DueAt { get; private init; }

    /// <summary>
    /// One occurrence per offset, in minutes relative to <see cref="DueAt"/>. Negative = minutes
    /// <b>before</b> the deadline (e.g. 30 days before = <c>-43200</c>); <c>0</c> = at the deadline.
    /// So <c>[-43200,-10080,-1440,0]</c> = four occurrences. Set only for <see cref="ReminderScheduleType.OneShot"/>.
    /// </summary>
    public IReadOnlyList<int>? LeadOffsetsMinutes { get; private init; }

    /// <summary>Recurrence cadence. Set only when <see cref="Type"/> is <see cref="ReminderScheduleType.RecurringInterval"/>.</summary>
    public ReminderIntervalPreset? IntervalPreset { get; private init; }

    /// <summary>The date the interval recurrence is anchored to. Set only for <see cref="ReminderScheduleType.RecurringInterval"/>.</summary>
    public DateTimeOffset? AnchorDate { get; private init; }

    /// <summary>Cron expression (UTC). Set only when <see cref="Type"/> is <see cref="ReminderScheduleType.RecurringCron"/>.</summary>
    public string? Cron { get; private init; }

    /// <summary>Optional end of a recurring schedule (after which no more occurrences are produced). Recurring types only.</summary>
    public DateTimeOffset? EndsAt { get; private init; }

    private ReminderSchedule()
    {
    }

    /// <summary>A one-shot deadline at <paramref name="dueAt"/>, one occurrence per lead-time offset.</summary>
    public static ReminderSchedule OneShot(DateTimeOffset dueAt, IReadOnlyList<int> leadOffsetsMinutes) =>
        new() { Type = ReminderScheduleType.OneShot, DueAt = dueAt, LeadOffsetsMinutes = leadOffsetsMinutes };

    /// <summary>A recurring reminder on the given interval preset, anchored to <paramref name="anchorDate"/>.</summary>
    public static ReminderSchedule RecurringInterval(ReminderIntervalPreset preset, DateTimeOffset anchorDate, DateTimeOffset? endsAt = null) =>
        new() { Type = ReminderScheduleType.RecurringInterval, IntervalPreset = preset, AnchorDate = anchorDate, EndsAt = endsAt };

    /// <summary>A recurring reminder driven by a cron expression (UTC).</summary>
    public static ReminderSchedule RecurringCron(string cron, DateTimeOffset? endsAt = null) => new() { Type = ReminderScheduleType.RecurringCron, Cron = cron, EndsAt = endsAt };
}