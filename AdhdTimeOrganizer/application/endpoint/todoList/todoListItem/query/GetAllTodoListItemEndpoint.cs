using AdhdTimeOrganizer.application.dto.response.todoList;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.query;

public class GetAllTodoListItemRequest
{
    public long? TodoListId { get; set; }
}

public class GetAllTodoListItemEndpoint(AppDbContext dbContext)
    : Endpoint<GetAllTodoListItemRequest, List<TodoListItemResponse>>
{
    public override void Configure()
    {
        Get("/todo-list-item");

        Summary(s =>
        {
            s.Summary = "Get all todo list items";
            s.Description = "Retrieves all todo list items, optionally filtered by todo list";
            s.Response<List<TodoListItemResponse>>(200, "Success");
        });
    }

    public override async Task HandleAsync(GetAllTodoListItemRequest req, CancellationToken ct)
    {
        var loggedUserId = User.GetId();

        var query = dbContext.Set<TodoListItem>()
            .FilteredByUser(loggedUserId)
            .Where(tdl => req.TodoListId == null || tdl.TodoListId == req.TodoListId)
            .OrderBy(td => td.DisplayOrder);

        var items = await TodoListItemResponse.Projection(query).ToListAsync(ct);
        await Send.OkAsync(items, ct);
    }
}
