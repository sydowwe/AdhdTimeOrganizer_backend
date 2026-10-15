using AdhdTimeOrganizer.TodoLists.application.notification;
using System.Text.Json;
using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.serviceContract;
using FastEndpoints;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.action;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.TodoLists.application.service.reminder;

/// <summary>
/// The <b>Done</b> button on a to-do's due push (<see cref="TodoListsNotificationTypes.TodoItemDue"/>, P2-03): one tick of the
/// item's checkbox, the same write <c>ToggleIsDoneTodoListItemEndpoint</c> makes with <c>forceValue: true</c> —
/// <see cref="BaseTodoListItem.ApplyIsDoneToggle"/>, then the due reminder sync and the done-changed event, after the
/// save. So the planner block it is planned into and its other followers see it exactly as if it were ticked in the app.
/// <para>
/// <b>Idempotent</b> because it only ever ticks: an item already done is <see cref="NotificationActionOutcome.Applied"/>
/// with nothing written, never toggled back open. A counted item ("3×") gets one repetition per tap, which is what one
/// tap of its checkbox does; the push closes after the tap, so there is no second one. An item that is gone is
/// <see cref="NotificationActionOutcome.Gone"/>. The push's other button, Snooze, is the Reminders module's.
/// </para>
/// </summary>
public class TodoItemDueNotificationActionHandler(DbContext dbContext, ITodoListItemDueReminderSync dueReminders)
    : INotificationActionHandler, IScopedService
{
    public bool Handles(NotificationType type, NotificationActionKind kind) =>
        type == TodoListsNotificationTypes.TodoItemDue && kind == NotificationActionKind.Done;

    public async Task<NotificationActionOutcome> ExecuteAsync(NotificationActionContext context, CancellationToken ct)
    {
        var itemId = Payload(context.PayloadJson)?.TodoListItemId ?? 0;
        if (itemId <= 0)
            return NotificationActionOutcome.Gone;

        var item = await dbContext.Set<TodoListItem>()
            .Include(i => i.Steps)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == context.UserId, ct);
        if (item is null)
            return NotificationActionOutcome.Gone;
        if (item.IsDone)
            return NotificationActionOutcome.Applied;

        item.ApplyIsDoneToggle(forceValue: true);
        await dbContext.SaveChangesAsync(ct);

        await dueReminders.SyncAsync([item.Id], ct);
        await new TodoListItemIsDoneChangedEvent(item.Id, item.UserId, item.IsDone).PublishAsync(Mode.WaitForAll, ct);

        return NotificationActionOutcome.Applied;
    }

    /// <summary>The payload record, or null for an empty, malformed or shape-drifted document.</summary>
    private static TodoItemDuePayload? Payload(string payloadJson)
    {
        try
        {
            return string.IsNullOrWhiteSpace(payloadJson) ? null : JsonHelper.Deserialize<TodoItemDuePayload>(payloadJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
