using AdhdTimeOrganizer.TodoLists.application.dto.@enum;

namespace AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;

/// <summary>
/// <c>?groupBy=list|category|priority</c>, bound from the query string (case-insensitive).
/// </summary>
/// <remarks>
/// Nullable so an omitted value reaches the validator as a 400, rather than binding to <c>List</c> — the enum's
/// default — and answering a question nobody asked.
/// </remarks>
public record TodoListItemEstimateFitRequest
{
    public EstimateFitGroupBy? GroupBy { get; init; }
}
