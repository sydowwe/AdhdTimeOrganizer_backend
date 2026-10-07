using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.TodoLists.application.service;

/// <summary>
/// FO3 phase 2: the one rule that decides whether moving a to-do item's due date was a <b>postponement</b>, and
/// records it on the item. Both paths that push a due date go through it — the item PUT and
/// <c>RescheduleOverdueTodoListItemEndpoint</c> (the renegotiation banner) — so the two cannot count differently.
/// <para>
/// <b>A move is a postponement</b> when the item was open, had a due date and gets a later one, and its
/// <b>first</b> due date is behind the user's today — <see cref="TodoListItem.FirstDueDate"/> once it has slipped,
/// the date it is moving from before that. So setting a date, clearing one ("let it go"), or pushing a deadline
/// that has not arrived yet is planning, not slipping; and once an item has slipped, every further push counts.
/// </para>
/// <para>
/// The planner's equivalent is <c>PlannerTaskPostponeRule</c>. They are two rules because a deadline is a day and
/// a block is a slot, but they answer the same question the same way.
/// </para>
/// </summary>
public static class TodoListItemPostponeRule
{
    /// <param name="item">The item. Only its first-due-date and counter are read and written.</param>
    /// <param name="wasDone">Whether the item was done before this write, or is made done by it.</param>
    /// <param name="fromDueDate">The due date before the write.</param>
    /// <param name="toDueDate">The due date after it.</param>
    /// <param name="userToday">The user's today, in their own zone.</param>
    /// <returns>Whether the move was counted.</returns>
    public static bool Apply(TodoListItem item, bool wasDone, DateOnly? fromDueDate, DateOnly? toDueDate, DateOnly userToday)
    {
        if (wasDone || fromDueDate is not { } from || toDueDate is not { } to || to <= from)
            return false;

        if ((item.FirstDueDate ?? from) >= userToday)
            return false;

        item.PostponeCount++;
        item.FirstDueDate ??= from;
        return true;
    }
}
