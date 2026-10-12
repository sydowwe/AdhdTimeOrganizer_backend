namespace AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;

public record RescheduleOverdueTodoListItemResponse
{
    /// <summary>The due date every moved item now has.</summary>
    public required DateOnly DueDate { get; init; }

    /// <summary>
    /// The items that moved, so the client can undo exactly these. The set is the server's, not whatever the
    /// client last fetched: an item another tab already rescheduled is not in it.
    /// </summary>
    public required IReadOnlyList<long> MovedIds { get; init; }

    /// <summary>
    /// Takes this renegotiation back, slip record included, through <c>POST /todo-list-item/reschedule-overdue/undo</c>
    /// for 15 minutes. Null when nothing moved.
    /// </summary>
    public string? UndoToken { get; init; }
}

/// <summary><c>POST /todo-list-item/reschedule-overdue/undo</c>: the items put back on their old dates.</summary>
public record UndoRescheduleOverdueTodoListItemResponse
{
    /// <summary>The items restored. One edited or ticked off since the renegotiation is left as it is, and not listed.</summary>
    public required IReadOnlyList<long> RestoredIds { get; init; }
}
