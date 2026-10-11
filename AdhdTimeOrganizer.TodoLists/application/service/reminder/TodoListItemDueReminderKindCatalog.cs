using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.reminders;

namespace AdhdTimeOrganizer.TodoLists.application.service.reminder;

/// <summary>
/// Declares <c>("TodoLists", "TodoItemDue")</c>, so the user can mute to-do reminders from <c>/reminder-preference</c>
/// before one has ever been registered for them. Read from <see cref="TodoListItemDueReminder"/>'s constants, the
/// same pair the sync registers, so the switch and the reminders cannot drift apart.
/// </summary>
public class TodoListItemDueReminderKindCatalog : IReminderKindCatalog, ISingletonService
{
    public IReadOnlyList<ReminderKindDescriptor> Kinds { get; } =
        [new ReminderKindDescriptor(TodoListItemDueReminder.OwnerModule, TodoListItemDueReminder.Kind)];
}
