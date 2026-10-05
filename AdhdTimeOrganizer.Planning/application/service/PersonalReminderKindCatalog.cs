using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.reminders;

namespace AdhdTimeOrganizer.Planning.application.service.reminder;

/// <summary>
/// Declares the one kind this host raises, <c>("Portal", "PersonalReminder")</c>, so the user can mute personal
/// reminders from <c>/reminder-preference</c> without a row existing first. It is the same pair
/// <see cref="ReminderRegistrationService"/> registers, read from its constants so the two cannot drift.
/// </summary>
public class PersonalReminderKindCatalog : IReminderKindCatalog, ISingletonService
{
    public IReadOnlyList<ReminderKindDescriptor> Kinds { get; } =
        [new ReminderKindDescriptor(ReminderRegistrationService.OwnerModule, ReminderRegistrationService.Kind)];
}
