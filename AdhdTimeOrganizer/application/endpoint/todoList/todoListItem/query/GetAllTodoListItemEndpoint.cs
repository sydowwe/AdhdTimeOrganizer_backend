using AdhdTimeOrganizer.application.dto.response.todoList;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TodoListItemMapper = AdhdTimeOrganizer.application.mapper.todoList.TodoListItemMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.query;

public class GetAllTodoListItemEndpoint(AppDbContext dbContext, TodoListItemMapper mapper)
    : EndpointWithoutRequest<List<TodoListItemResponse>>
{
    public override void Configure()
    {
        Get("/todo-list-item/{todoListId}");
        
        Summary(s =>
        {
            s.Summary = "Get all todo list items";
            s.Description = "Retrieves all todo list items for a specific todo list";
            s.Response<List<TodoListItemResponse>>(200, "Success");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var todoListId = Route<long>("todoListId");
        var loggedUserId = User.GetId();

        var query = dbContext.Set<TodoListItem>()
            .FilteredByUser(loggedUserId)
            .Where(tdl => tdl.TodoListId == todoListId)
            .Include(tdl => tdl.Activity)
                .ThenInclude(a => a.Role)
            .Include(tdl => tdl.Activity)
                .ThenInclude(a => a.Category)
            .Include(tdl => tdl.TaskPriority)
            .OrderBy(td => td.DisplayOrder);

        var items = await mapper.ProjectToResponse(query).ToListAsync(ct);
        await Send.OkAsync(items, ct);
    }
}
