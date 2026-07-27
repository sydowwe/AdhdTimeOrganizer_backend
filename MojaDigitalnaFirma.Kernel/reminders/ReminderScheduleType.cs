namespace MojaDigitalnaFirma.Kernel.reminders;

/// <summary>
/// Discriminates how a reminder occurs (see <see cref="ReminderSchedule"/>). Distinct from
/// <c>Kernel.scheduling.ReminderScheduleType</c>: that one drives a Quartz recurring trigger, this one
/// describes per-reminder occurrence timing stored in the Reminders DB (<c>NextOccurrenceAt</c>),
/// and adds a one-shot deadline variant the Scheduler contract has no notion of.
/// </summary>
public enum ReminderScheduleType
{
    /// <summary>A fixed deadline (<c>DueAt</c>) with one occurrence per lead-time offset.</summary>
    OneShot,

    /// <summary>A repeating reminder on a fixed interval preset anchored to a date.</summary>
    RecurringInterval,

    /// <summary>A repeating reminder driven by a cron expression (UTC).</summary>
    RecurringCron
}