namespace AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;

/// <summary>
/// The day to recap, as <c>YYYY-MM-DD</c>. Bound from the query string.
/// </summary>
/// <remarks>
/// A bare <see cref="DateOnly"/> rather than a timestamp: the caller is asking about a calendar day,
/// and the day is bounded as a half-open UTC range, the same way every History dashboard bounds one.
/// </remarks>
public record TodoListItemDailyRecapRequest
{
    public required DateOnly Date { get; init; }
}
