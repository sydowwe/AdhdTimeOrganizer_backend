namespace AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;

public record RescheduleOverdueTodoListItemRequest
{
    /// <summary>The list whose overdue items move, or null for every list (and items in none).</summary>
    public long? ListId { get; init; }

    /// <summary>
    /// Days from the user's today the items land on: 0 is today, 7 a week out. Measured from today rather than
    /// from each item's own date, so the pile clears instead of shifting a month-old task to three weeks old.
    /// </summary>
    public int Days { get; init; }
}

/// <summary><c>POST /todo-list-item/reschedule-overdue/undo</c>: the token the renegotiation returned.</summary>
public record UndoRescheduleOverdueTodoListItemRequest
{
    public required string UndoToken { get; init; }
}
