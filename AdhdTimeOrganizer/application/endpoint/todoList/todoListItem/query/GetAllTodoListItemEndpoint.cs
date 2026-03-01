using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.query;

public class GetAllTodoListItemEndpoint(AppDbContext dbContext, TodoListItemMapper mapper)
    : Endpoint<GetTodoListItemsByListRequest, List<TodoListItemResponse>>
{
    public override void Configure()
    {
        Get("/todo-list-item");
        Roles(EndpointHelper.GetUserOrHigherRoles());
        Summary(s =>
        {
            s.Summary = "Get all todo list items";
            s.Description = "Retrieves all todo list items filtered by todo list";
            s.Response<List<TodoListItemResponse>>(200, "Success");
        });
    }

    public override async Task HandleAsync(GetTodoListItemsByListRequest req, CancellationToken ct)
    {
        var loggedUserId = User.GetId();

        var query = dbContext.Set<TodoListItem>()
            .FilteredByUser(loggedUserId)
            .Where(tdl => tdl.TodoListId == req.TodoListId)
            .Include(tdl => tdl.Activity)
                .ThenInclude(a => a.Role)
            .Include(tdl => tdl.Activity)
                .ThenInclude(a => a.Category)
            .Include(tdl => tdl.TaskPriority)
            .OrderBy(td => td.DisplayOrder);

        var items = await mapper.ProjectToResponse(query).ToListAsync(ct);
        await SendOkAsync(items, ct);
    }
}
