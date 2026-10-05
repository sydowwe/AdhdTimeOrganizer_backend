using System.Text.Json;
using System.Text.Json.Nodes;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.reminders;

namespace AdhdTimeOrganizer.Planning.application.service.reminder;

/// <summary>
/// Names a portal reminder by its own title ("Call the dentist") on the Reminders module's read surface, in
/// place of "Reminder #23001". The title is the <c>PersonalReminderPayload.Title</c> that
/// <see cref="ReminderRegistrationService"/> registers — the user's own text about their own reminder, and its
/// one recipient is that same user, so it is safe for every reader the resolver contract names.
/// </summary>
public class PersonalReminderLabelResolver : IReminderLabelResolver, ISingletonService
{
    public string TemplateKey => ReminderRegistrationService.TemplateKey;

    public string? ResolveLabel(ReminderLabelContext context) =>
        context.Payload is JsonObject payload && payload["title"] is JsonValue title && title.GetValueKind() == JsonValueKind.String
            ? title.GetValue<string>()
            : null;
}
