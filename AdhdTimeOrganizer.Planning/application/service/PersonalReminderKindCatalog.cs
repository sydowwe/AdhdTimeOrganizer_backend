using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.reminders;

namespace AdhdTimeOrganizer.Planning.application.service.reminder;

/// <summary>
/// Declares the two kinds this slice raises, so the user can mute either from <c>/reminder-preference</c> without a
/// row existing first: <c>("Portal", "PersonalReminder")</c> for the reminders they wrote, and
/// <c>("Portal", "PlannedBlockStarting")</c> for the ones the planner created for a new block by itself. They are
/// the same pairs <see cref="ReminderRegistrationService"/> registers, read from its constants so the two cannot drift.
/// </summary>
public class PersonalReminderKindCatalog : IReminderKindCatalog, ISingletonService
{
    public IReadOnlyList<ReminderKindDescriptor> Kinds { get; } =
    [
        new ReminderKindDescriptor(ReminderRegistrationService.OwnerModule, ReminderRegistrationService.Kind),
        new ReminderKindDescriptor(ReminderRegistrationService.OwnerModule, ReminderRegistrationService.AutoKind)
    ];
}
