using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.request.@base.table;
using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TodoListCategoryMapper = AdhdTimeOrganizer.application.mapper.todoList.TodoListCategoryMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListCategory.query;

public class GetFilterSortTodoListCategoryEndpoint(AppDbContext dbContext, TodoListCategoryMapper mapper)
    : Endpoint<BaseFilterSortRequest<TodoListCategoryFilterRequest>, List<TodoListCategoryResponse>>
{
    public override void Configure()
    {
        Post("/todo-list-category/filter-sort");
        Roles(EndpointHelper.GetUserOrHigherRoles());

        Summary(s =>
        {
            s.Summary = "Get categories that have todo lists, with optional name filter";
            s.Description = "Returns only categories used by the current user's todo lists. Appends 'Other' (id=-1) if uncategorized items exist.";
            s.Response<List<TodoListCategoryResponse>>(200, "Success");
        });
    }

    public override async Task HandleAsync(BaseFilterSortRequest<TodoListCategoryFilterRequest> req, CancellationToken ct)
    {
        var userId = User.GetId();
        var hideEmpty = req is { UseFilter: true, Filter.HideEmpty: true };

        var query = dbContext.TodoListCategories
            .Where(c => c.UserId == userId);

        if (hideEmpty)
            query = query.Where(c => dbContext.TodoLists.Any(tl => tl.UserId == userId && tl.CategoryId == c.Id));

        if (req is { UseFilter: true, Filter: not null } && !string.IsNullOrWhiteSpace(req.Filter.Name))
            query = query.Where(c => c.Name.Contains(req.Filter.Name));

        var result = await mapper.ProjectToResponse(query.SortByMany(req.SortBy)).ToListAsync(ct);

        var hasUncategorized = await dbContext.TodoLists
            .AnyAsync(tl => tl.UserId == userId && tl.CategoryId == null, ct);

        if (hasUncategorized)
        {
            result.Add(new TodoListCategoryResponse
            {
                Id = -1,
                Name = "Other",
                Color = "default",
                Icon = "fas fa-ellipsis",
                Text = null
            });
        }

        await Send.OkAsync(result, ct);
    }
}
