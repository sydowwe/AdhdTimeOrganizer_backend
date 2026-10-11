using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.Contracts.user;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.TodoLists.application.service.reminder;

/// <summary>
/// Translates a <see cref="TodoListItem"/>'s due date and time into <see cref="IReminderRegistry"/> calls — the only
/// class in the slice that knows the schedule shape and payload. See <see cref="ITodoListItemDueReminderSync"/> for
/// the contract every caller relies on.
/// <para>
/// <b>Explicit recipient, guarded dispatch.</b> The item's owner is registered as an explicit recipient rather than
/// through a recipient resolver, because the module keeps resolver-backed reminders off every self-service read —
/// the user's upcoming list, snooze and dismiss. What a resolver would have added, retiring a reminder whose item was
/// finished or deleted behind this service's back, is <see cref="TodoListItemDueReminderGuard"/>'s job instead.
/// </para>
/// </summary>
public class TodoListItemDueReminderSync(
    DbContext dbContext,
    IReminderRegistry registry,
    IUserTimeZoneReader timeZones,
    TimeProvider time,
    ILogger<TodoListItemDueReminderSync> logger) : ITodoListItemDueReminderSync, IScopedService
{
    public async Task SyncAsync(IReadOnlyCollection<long> itemIds, CancellationToken ct = default)
    {
        if (itemIds.Count == 0)
            return;

        var ids = itemIds.Distinct().ToList();
        var items = await TodoListItemDueReminder.Snapshots(dbContext, ids).ToDictionaryAsync(i => i.Id, ct);

        // One lookup each for the whole batch — the bulk reschedule moves every overdue item in a single call.
        var userIds = items.Values.Select(i => i.UserId).Distinct().ToList();
        var zones = await timeZones.GetTimeZonesAsync(userIds, ct);
        var leads = await dbContext.Set<UserTodoListSettings>()
            .AsNoTracking()
            .IgnoreNamedQueryFilters([QueryFilters.UserScope])
            .Where(s => userIds.Contains(s.UserId))
            .ToDictionaryAsync(s => s.UserId, s => s.DueReminderMinutesBefore, ct);

        var now = time.GetUtcNow();
        foreach (var id in ids)
        {
            var registration = items.TryGetValue(id, out var item)
                ? BuildRegistration(item,
                    zones.GetValueOrDefault(item.UserId, TimeZoneInfo.Utc),
                    leads.GetValueOrDefault(item.UserId, UserTodoListSettings.DefaultDueReminderMinutesBefore),
                    now)
                : null;

            if (registration is null)
                await CancelOneAsync(id, ct);
            else
                await RegisterOneAsync(id, registration, ct);
        }
    }

    public async Task CancelAsync(IReadOnlyCollection<long> itemIds, CancellationToken ct = default)
    {
        foreach (var id in itemIds.Distinct())
            await CancelOneAsync(id, ct);
    }

    public async Task SyncAllForUserAsync(long userId, CancellationToken ct = default)
    {
        // A day of slack on the date bound: the due instant is composed in the user's zone, and a UTC "today" can
        // be a day either side of theirs. An item whose instant has passed is cancelled by SyncAsync, not skipped.
        var earliest = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(-1);

        var ids = await dbContext.Set<TodoListItem>()
            .IgnoreNamedQueryFilters([QueryFilters.UserScope])
            .Where(i => i.UserId == userId && !i.IsDone && i.DueTime != null && i.DueDate >= earliest)
            .Select(i => i.Id)
            .ToListAsync(ct);

        await SyncAsync(ids, ct);
    }

    /// <summary>
    /// The registration an item should have now, or null when it should have none: done, date-only, or due
    /// already. The lead is the user's setting; when the item is created or moved so close to its due time that
    /// the lead has already passed, the reminder fires at the due time itself rather than not at all.
    /// </summary>
    private static ReminderRegistration? BuildRegistration(
        TodoListItemDueReminder.Snapshot item, TimeZoneInfo zone, int leadMinutes, DateTimeOffset now)
    {
        if (TodoListItemDueReminder.DueInstant(item, zone) is not { } dueAt || dueAt <= now)
            return null;

        var minutesBefore = leadMinutes > 0 && dueAt.AddMinutes(-leadMinutes) > now ? leadMinutes : 0;

        return new ReminderRegistration
        {
            Key = TodoListItemDueReminder.KeyFor(item.Id),
            Schedule = ReminderSchedule.OneShot(dueAt, [-minutesBefore]),
            TemplateKey = TodoListItemDueReminder.TemplateKey,
            NotificationType = NotificationType.TodoItemDue,
            Payload = new TodoItemDuePayload(item.Id, item.Title, item.TodoListId, dueAt, minutesBefore),
            RecipientMode = RecipientMode.ExplicitUsers,
            ExplicitRecipientUserIds = [item.UserId]
        };
    }

    private async Task RegisterOneAsync(long itemId, ReminderRegistration registration, CancellationToken ct)
    {
        try
        {
            await registry.RegisterAsync(registration, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ids only — the payload carries the item's title, which is the user's own text.
            logger.LogError(ex,
                "Failed to register the due reminder for to-do item {TodoListItemId}; it keeps whatever schedule it had, and the dispatch guard retires it if that is stale",
                itemId);
        }
    }

    private async Task CancelOneAsync(long itemId, CancellationToken ct)
    {
        try
        {
            await registry.CancelAsync(TodoListItemDueReminder.KeyFor(itemId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Failed to cancel the due reminder for to-do item {TodoListItemId}; it stays on the user's upcoming list until the dispatch guard retires it",
                itemId);
        }
    }
}
