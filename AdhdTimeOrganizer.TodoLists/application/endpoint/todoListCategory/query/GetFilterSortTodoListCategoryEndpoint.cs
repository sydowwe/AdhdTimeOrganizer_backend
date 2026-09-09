using AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.TodoLists.application.dto.filter;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FastEndpoints;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListCategory.query;

/// <summary>
/// The categories the current user's todo lists actually use, plus the synthetic "Other" row
/// (<see cref="TodoListCategoryResponse.UncategorizedId"/>) when they have lists with no category.
/// </summary>
public class GetFilterSortTodoListCategoryEndpoint(DbContext dbContext)
    : BaseFilterSortEndpoint<TodoListCategory, TodoListCategoryResponse, TodoListCategoryFilterRequest>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    /// <summary>
    /// The name filter of the request being handled, or null when the caller sent none. Set by
    /// <see cref="ApplyCustomFiltering"/> — the only place the filter is in scope — and read by
    /// <see cref="PostProcessItemsAsync"/>, which has to apply the same predicate to the synthetic row in C#.
    /// Safe as instance state: FastEndpoints resolves one endpoint instance per request.
    /// </summary>
    private string? _nameFilter;

    public override void Configure()
    {
        base.Configure();

        Summary(s =>
        {
            s.Summary = "Get categories that have todo lists, with optional name filter";
            s.Description = $"Returns only categories used by the current user's todo lists. Appends '{TodoListCategoryResponse.UncategorizedName}' (id={TodoListCategoryResponse.UncategorizedId}) if uncategorized lists exist and the name filter does not exclude it.";
            s.Response<List<TodoListCategoryResponse>>(200, "Success");
        });
    }

    protected override Task<IQueryable<TodoListCategory>> ApplyUserScoping(IQueryable<TodoListCategory> query, long userId, CancellationToken ct = default) =>
        Task.FromResult(query.Where(c => c.UserId == userId));

    protected override IQueryable<TodoListCategory> ApplyCustomFiltering(IQueryable<TodoListCategory> query, TodoListCategoryFilterRequest filter)
    {
        var userId = User.GetId();

        // UserId is repeated inside the EXISTS on purpose: it is the leading column of
        // ix_todo_list_user_id_category_id, which the correlated subquery cannot use without it.
        if (filter.HideEmpty)
            query = query.Where(c => _dbContext.Set<TodoList>().Any(tl => tl.UserId == userId && tl.CategoryId == c.Id));

        _nameFilter = filter.Name;

        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            var pattern = filter.Name.ToContainsPattern();
            query = query.Where(c => EF.Functions.ILike(c.Name, pattern, FreeTextFilterExtensions.EscapeCharacter));
        }

        return query;
    }

    /// <summary>
    /// Appends the "Other" row. The probe runs only when the row could survive the request's own filter —
    /// a name filter that "Other" does not match makes both the probe and the row pointless, so the second
    /// round trip is skipped outright rather than issued and then discarded.
    /// </summary>
    protected override async Task<List<TodoListCategoryResponse>> PostProcessItemsAsync(List<TodoListCategoryResponse> items, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_nameFilter) &&
            !TodoListCategoryResponse.UncategorizedName.Contains(_nameFilter, StringComparison.OrdinalIgnoreCase))
            return items;

        var userId = User.GetId();

        var hasUncategorized = await _dbContext.Set<TodoList>()
            .AnyAsync(tl => tl.UserId == userId && tl.CategoryId == null, ct);

        if (hasUncategorized)
            items.Add(TodoListCategoryResponse.Uncategorized);

        return items;
    }
}
