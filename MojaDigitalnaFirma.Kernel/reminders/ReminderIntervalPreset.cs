namespace MojaDigitalnaFirma.Kernel.reminders;

/// <summary>
/// The recurrence cadence of a <see cref="ReminderScheduleType.RecurringInterval"/> reminder. Coarser than
/// <c>Kernel.scheduling.ReminderIntervalPreset</c> (no sub-day units): a scheduled reminder fires on a
/// human-meaningful calendar cadence, not "every N minutes".
/// </summary>
public enum ReminderIntervalPreset
{
    Daily,
    Weekly,
    Monthly,
    Quarterly,
    Yearly
}