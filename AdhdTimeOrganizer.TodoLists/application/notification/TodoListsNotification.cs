using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.action;
using Sydowwe.Framework.Contracts.notification.kind;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.notification.text;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.TodoLists.application.notification;

/// <summary>
/// The notification kinds the TodoLists slice raises. The key is stored and on the wire, and is what the client's
/// <c>notificationTypeMeta</c> is keyed on: <b>only add, never rename</b>.
/// </summary>
public static class TodoListsNotificationTypes
{
    public const string TodoItemDueKey = "TodoItemDue";

    public static readonly NotificationType TodoItemDue = new(TodoItemDueKey);
}

/// <summary>
/// A to-do item with a due date <b>and</b> a due time is coming due. Registered through the Reminders module as a
/// one-shot at the due instant with the user's lead offset, so it is frozen at registration: the owner re-registers on
/// every change and guards the dispatch (<c>IReminderDispatchGuard</c>) against the changes it missed.
/// <para>
/// <c>Title</c> is the user's own text about their own item, and its one recipient is that same user — read
/// <see cref="INotificationPayload"/> first. <c>DueAt</c> is the due instant the registration was built from (UTC); the
/// guard compares it with the item's current one, so a stale registration retires itself. <c>MinutesBefore</c> is the
/// lead the occurrence fires at, 0 for "due now", so the title can say which. Property names are the persisted JSON
/// keys.
/// </para>
/// </summary>
[NotificationPayload(TodoListsNotificationTypes.TodoItemDueKey)]
public sealed record TodoItemDuePayload(
    long TodoListItemId,
    string? Title = null,
    long? TodoListId = null,
    DateTimeOffset? DueAt = null,
    int? MinutesBefore = null) : INotificationPayload, IReminderPayload;

/// <summary>
/// A to-do item with a due time is coming due — the user's own lead time before it, or at the instant itself.
/// Registered per item (<c>TodoListItemDueReminderSync</c>), so the user sees it on their upcoming list and can snooze
/// or dismiss it. A date-only item never raises it: there is no instant to fire at. The item's own title is the body —
/// it is the whole point — and the title says how soon; each half degrades on its own.
/// <para>
/// Not held while away: it is about the user's own commitment, not a nudge to come back. Not mailed by default — a
/// self-set due time the user already sees in-app. Deliberately not the Reminders module's <c>DeadlineApproaching</c>,
/// which is mailed by default and carries no id to link to.
/// </para>
/// </summary>
public sealed class TodoItemDueNotificationKind : NotificationKind<TodoItemDuePayload>, ISingletonService
{
    public override NotificationType Type => TodoListsNotificationTypes.TodoItemDue;

    /// <summary>P2-03: tick it off (<c>TodoItemDueNotificationActionHandler</c>), or snooze it an hour (Reminders').</summary>
    public override IReadOnlyList<NotificationAction> Actions { get; } = [NotificationAction.Done, NotificationAction.Snooze(60)];

    protected override (string Title, string Body) Render(TodoItemDuePayload? payload, NotificationRenderContext context)
    {
        var en = context.Kit.Language == AvailableLocales.En;
        // "Termín úlohy o 30 min", "… o 1 h 30 min"; "Termín úlohy je teraz"; no lead: "Termín úlohy sa blíži".
        var title = payload?.MinutesBefore switch
        {
            null => en ? "Due soon" : "Termín úlohy sa blíži",
            <= 0 => en ? "Due now" : "Termín úlohy je teraz",
            var m => en ? $"Due in {NotificationTextKit.Duration(m.Value)}" : $"Termín úlohy o {NotificationTextKit.Duration(m.Value)}"
        };
        return (title, NonEmpty(payload?.Title) ?? (en ? "One of your to-dos is coming due." : "Blíži sa termín jednej z vašich úloh."));
    }

    protected override NotificationSubject? Subject(TodoItemDuePayload? payload) =>
        payload?.TodoListItemId is long id and > 0 ? new NotificationSubject(NotificationSubjectKinds.TodoListItem, id) : null;
}
