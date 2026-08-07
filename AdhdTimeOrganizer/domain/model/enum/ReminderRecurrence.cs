namespace AdhdTimeOrganizer.domain.model.@enum;

/// <summary>
/// How often a portal <see cref="entity.reminder.Reminder"/> repeats. A <c>null</c> recurrence on the entity
/// means one-shot; this enum only names the repeating cadences.
/// <para>
/// Deliberately a portal enum rather than a reuse of <c>Sydowwe.Framework.Contracts.reminders.ReminderIntervalPreset</c>: the
/// <c>Sydowwe.Framework.Contracts</c> enum is the scheduling contract and the portal must be free to expose a different set to users.
/// The mapping between the two lives in exactly one place — <c>ReminderRegistrationService.ToPreset</c>.
/// </para>
/// </summary>
public enum ReminderRecurrence
{
    Daily,
    Weekly,
    Monthly,
    Quarterly,
    Yearly
}