using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.TodoLists.application.service.reminder;

/// <summary>
/// What the registration (<see cref="TodoListItemDueReminderSync"/>) and the dispatch guard
/// (<see cref="TodoListItemDueReminderGuard"/>) must agree on: the key, the read of an item, and how its due
/// instant is composed. Both sides go through here, so "the instant the reminder was built for" and "the instant
/// the item has now" cannot drift apart by construction — the guard's whole test is that they are equal.
/// </summary>
public static class TodoListItemDueReminder
{
    /// <summary>This slice, as the Reminders module sees it — half of the idempotency key and of the mute switch.</summary>
    public const string OwnerModule = "TodoLists";

    public const string SubjectType = nameof(TodoListItem);

    /// <summary>One kind for every to-do due reminder, so muting them is one <c>ReminderKindPreference</c> row.</summary>
    public const string Kind = "TodoItemDue";

    /// <summary>Content key. Also keys the label resolver and the dispatch guard.</summary>
    public const string TemplateKey = "todo-lists.todo-item-due";

    public static ReminderKey KeyFor(long itemId) => new(OwnerModule, SubjectType, itemId.ToString(), Kind);

    /// <summary>The columns a reminder is built from, and nothing else.</summary>
    public sealed record Snapshot(
        long Id,
        long UserId,
        long? TodoListId,
        bool IsDone,
        DateOnly? DueDate,
        TimeOnly? DueTime,
        string Title);

    /// <summary>
    /// Items by id, read for the reminder. The per-user filter is lifted: the ids come from a caller that already
    /// scoped them (an endpoint's own load, a save it just made) or from the module's key in a background scan
    /// where the filter is inert anyway, and a filter that hid an item here would read as "deleted" and cancel a
    /// live reminder. Every other filter stays on — an item hidden for another reason (its activity archived,
    /// which drops it from every to-do read) is treated as gone, the same as everywhere else in the app.
    /// </summary>
    public static IQueryable<Snapshot> Snapshots(DbContext dbContext, IReadOnlyCollection<long> itemIds) =>
        dbContext.Set<TodoListItem>()
            .AsNoTracking()
            .IgnoreNamedQueryFilters([QueryFilters.UserScope])
            .Where(i => itemIds.Contains(i.Id))
            .Select(i => new Snapshot(i.Id, i.UserId, i.TodoListId, i.IsDone, i.DueDate, i.DueTime, i.Activity.Name));

    /// <summary>
    /// The instant an open item is due, or null when it should have no reminder: done, date-only (there is no
    /// instant to fire at, and inventing 09:00 is wrong), or no due date at all. Composed in the user's own zone;
    /// <see cref="WallClockZone"/> handles both DST discontinuities.
    /// </summary>
    public static DateTimeOffset? DueInstant(Snapshot item, TimeZoneInfo timeZone) =>
        item is { IsDone: false, DueDate: { } date, DueTime: { } time }
            ? new DateTimeOffset(WallClockZone.ToUtc(date, time, timeZone))
            : null;
}
