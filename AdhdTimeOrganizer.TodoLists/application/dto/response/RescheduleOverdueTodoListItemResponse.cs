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
}
