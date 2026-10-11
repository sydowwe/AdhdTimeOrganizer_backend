using System.Text.Json;
using System.Text.Json.Nodes;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.reminders;

namespace AdhdTimeOrganizer.Planning.application.service.reminder;

/// <summary>
/// Names an automatic block reminder by its block's activity on the Reminders module's read surface, in place of
/// "Reminder #23001" — the <c>PlannedBlockStartingPayload.Title</c> that <see cref="ReminderRegistrationService"/>
/// registers. The user's own activity name, and its one recipient is that same user, so it is safe for every reader
/// the resolver contract names. Same shape as <see cref="PersonalReminderLabelResolver"/>.
/// </summary>
public class PlannedBlockStartingLabelResolver : IReminderLabelResolver, ISingletonService
{
    public string TemplateKey => ReminderRegistrationService.AutoTemplateKey;

    public string? ResolveLabel(ReminderLabelContext context) =>
        context.Payload is JsonObject payload && payload["title"] is JsonValue title && title.GetValueKind() == JsonValueKind.String
            ? title.GetValue<string>()
            : null;
}
