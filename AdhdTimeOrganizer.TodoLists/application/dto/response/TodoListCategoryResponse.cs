using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.application.dto.response.generic;

namespace AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;

public record TodoListCategoryResponse : NameTextColorIconResponse,
    IProjectionResponse<TodoListCategoryResponse, TodoListCategory>
{
    /// <summary>
    /// Id of the synthetic "uncategorized" bucket. It is not a row: no <see cref="TodoListCategory"/> has this
    /// id, and no <c>TodoList.CategoryId</c> holds it — a list without a category has <c>CategoryId == null</c>.
    /// The two halves of the sentinel are <see cref="Uncategorized"/>, which
    /// <c>GetFilterSortTodoListCategoryEndpoint</c> appends to its list, and
    /// <c>TodoListFilterRequest.CategoryId</c>, which <c>FilterSortTodoListEndpoint</c> reads back as
    /// "<c>CategoryId is null</c>". Both must use this constant.
    /// </summary>
    public const long UncategorizedId = -1;

    public const string UncategorizedName = "Other";

    /// <summary>
    /// How many of the caller's lists point at this category: the category panel's badge and the
    /// delete-confirm's "this category holds N lists". Set by <see cref="Projection"/>, and on the
    /// "Other" row by <c>GetFilterSortTodoListCategoryEndpoint</c>. It stays null on the copy embedded
    /// in <c>TodoListResponse.Category</c>, which builds its own projection and has no use for it.
    /// </summary>
    public int? ListCount { get; init; }

    /// <summary>The synthetic row itself — see <see cref="UncategorizedId"/>.</summary>
    public static TodoListCategoryResponse Uncategorized => new()
    {
        Id = UncategorizedId,
        Name = UncategorizedName,
        Color = "default",
        Icon = "fas fa-ellipsis",
        Text = null
    };

    public static IQueryable<TodoListCategoryResponse> Projection(IQueryable<TodoListCategory> q)
    {
        return q.Select(e => new TodoListCategoryResponse
        {
            Id = e.Id,
            Name = e.Name,
            Text = e.Text,
            Color = e.Color,
            Icon = e.Icon,
            // The UserId clause is the same one HideEmpty's EXISTS carries: it is the leading column of
            // ix_todo_list_user_id_category_id, and it keeps another user's list that points at this
            // category out of the count without relying on a query filter being in force.
            ListCount = e.TodoListColl.Count(tl => tl.UserId == e.UserId)
        });
    }

    public static IQueryable<SelectOptionResponse> SelectOptionProjection(IQueryable<TodoListCategory> q)
    {
        return q.Select(e => new SelectOptionResponse { Id = e.Id, Text = e.Name });
    }
}