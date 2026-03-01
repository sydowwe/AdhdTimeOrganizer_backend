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

        var usedCategoryIds = await dbContext.TodoLists
            .Where(tl => tl.UserId == userId && tl.CategoryId != null)
            .Select(tl => tl.CategoryId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var hasUncategorized = await dbContext.TodoLists
            .AnyAsync(tl => tl.UserId == userId && tl.CategoryId == null, ct);

        IQueryable<TodoListCategory> query = dbContext.TodoListCategories
            .Where(c => c.UserId == userId && usedCategoryIds.Contains(c.Id));

        if (req is { UseFilter: true, Filter: not null } && !string.IsNullOrWhiteSpace(req.Filter.Name))
        {
            query = query.Where(c => c.Name.Contains(req.Filter.Name));
        }

        var result = await mapper.ProjectToResponse(query.SortByMany(req.SortBy)).ToListAsync(ct);

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

        await SendOkAsync(result, ct);
    }
}
