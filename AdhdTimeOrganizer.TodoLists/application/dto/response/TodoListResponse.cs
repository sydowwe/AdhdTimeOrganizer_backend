using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.application.dto.response.generic;

namespace AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;

public record TodoListResponse : NameTextIconResponse,
    IProjectionResponse<TodoListResponse, TodoList>
{
    public TodoListCategoryResponse? Category { get; init; }
    public int ItemCount { get; init; }
    public int CompletedCount { get; init; }

    /// <summary>
    /// <c>ItemCount</c> and <c>CompletedCount</c> come from <b>one</b> grouped scan of
    /// <c>todo_list_item</c> for the whole page, LEFT JOINed on <c>todo_list_id</c> — not from two
    /// correlated subqueries per row, which is what this projection used to emit (<c>PERF-3</c>) and what
    /// the <c>(user_id, todo_list_id, display_order)</c> index was added to serve.
    /// <para>
    /// The <c>DefaultIfEmpty</c> arm is load-bearing: a list with no items has no group, so without it the
    /// join would drop the row entirely and an empty list would vanish from every list read rather than
    /// answer 0/0. <c>FilterSortTodoListTests.ItemAndCompletedCounts_AreComputedPerList</c> asserts both
    /// halves for that reason.
    /// </para>
    /// </summary>
    public static IQueryable<TodoListResponse> Projection(IQueryable<TodoList> q)
    {
        // The two aggregates are projected as int? on purpose: on the outer join's empty side every column of
        // the group row reads NULL, and a non-nullable int there throws "Nullable object must have a value"
        // while materialising — a runtime failure on exactly the empty-list case, not a compile error.
        var counts = q.SelectMany(l => l.TodoListItemColl)
            .GroupBy(i => i.TodoListId)
            .Select(g => new { TodoListId = g.Key, Total = (int?)g.Count(), Done = (int?)g.Count(i => i.IsDone) });

        return from e in q
            join c in counts on (long?)e.Id equals c.TodoListId into matched
            from c in matched.DefaultIfEmpty()
            select new TodoListResponse
            {
                Id = e.Id,
                Name = e.Name,
                Text = e.Text,
                Icon = e.Icon,
                ItemCount = c.Total ?? 0,
                CompletedCount = c.Done ?? 0,
                Category = e.Category == null
                    ? null
                    : new TodoListCategoryResponse
                    {
                        Id = e.Category.Id,
                        Name = e.Category.Name,
                        Text = e.Category.Text,
                        Color = e.Category.Color,
                        Icon = e.Category.Icon
                    }
            };
    }

    public static IQueryable<SelectOptionResponse> SelectOptionProjection(IQueryable<TodoList> q)
    {
        return q.Select(e => new SelectOptionResponse { Id = e.Id, Text = e.Name });
    }
}