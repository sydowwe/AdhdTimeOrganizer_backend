using System.Text.Json;
using System.Text.Json.Nodes;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.reminders;

namespace AdhdTimeOrganizer.TodoLists.application.service.reminder;

/// <summary>
/// Names a to-do reminder by the item's own title on the Reminders module's read surface, in place of
/// "TodoListItem #812". The title is the <c>TodoItemDuePayload.Title</c> the sync registers — the user's own text
/// about their own item, and its one recipient is that same user, so it is safe for every reader the contract names.
/// </summary>
public class TodoListItemDueReminderLabelResolver : IReminderLabelResolver, ISingletonService
{
    public string TemplateKey => TodoListItemDueReminder.TemplateKey;

    public string? ResolveLabel(ReminderLabelContext context) =>
        context.Payload is JsonObject payload && payload["title"] is JsonValue title && title.GetValueKind() == JsonValueKind.String
            ? title.GetValue<string>()
            : null;
}
