using System.Text.Json.Nodes;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.Contracts.user;

namespace AdhdTimeOrganizer.TodoLists.application.service.reminder;

/// <summary>
/// The safety net under <see cref="TodoListItemDueReminderSync"/>: at dispatch, a to-do reminder is delivered only
/// if its item still exists, is still open, and is still due at the instant the reminder was registered for.
/// Anything else — ticked off by a path that forgot to sync, deleted by a database cascade (a list, an activity),
/// moved without a re-registration — retires the occurrence, and the scan writes a <c>Skipped</c> row instead of
/// pushing a reminder about something already dealt with.
/// <para>
/// Runs in the background scan with no user: the item is found by the id in the module's key, never by an ambient
/// principal. Compares the instants rather than the raw date and time, so a change of the user's time zone also
/// reads as "moved" — the preferences endpoint re-syncs on that change, so the re-registered reminder is the live one.
/// </para>
/// </summary>
public class TodoListItemDueReminderGuard(DbContext dbContext, IUserTimeZoneReader timeZones)
    : IReminderDispatchGuard, IScopedService
{
    public string TemplateKey => TodoListItemDueReminder.TemplateKey;

    public async Task<bool> IsStillLiveAsync(ReminderResolutionContext context, CancellationToken ct)
    {
        if (!long.TryParse(context.Key.SubjectId, out var itemId))
            return false;

        var item = await TodoListItemDueReminder.Snapshots(dbContext, [itemId]).FirstOrDefaultAsync(ct);
        if (item is null)
            return false;

        var zone = (await timeZones.GetTimeZonesAsync([item.UserId], ct)).GetValueOrDefault(item.UserId, TimeZoneInfo.Utc);
        if (TodoListItemDueReminder.DueInstant(item, zone) is not { } dueAt)
            return false;

        // A payload with no readable instant cannot be shown stale, and the item is open with a due time: deliver.
        return RegisteredDueAt(context.Payload) is not { } registered || registered == dueAt;
    }

    private static DateTimeOffset? RegisteredDueAt(JsonNode? payload) =>
        payload is JsonObject obj && obj["dueAt"] is JsonValue value && value.TryGetValue<DateTimeOffset>(out var dueAt)
            ? dueAt
            : null;
}
