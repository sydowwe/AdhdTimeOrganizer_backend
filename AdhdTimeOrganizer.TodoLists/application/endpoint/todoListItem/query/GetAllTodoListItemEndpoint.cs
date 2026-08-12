using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.query;

public class GetAllTodoListItemRequest
{
    public long? TodoListId { get; set; }
}

public class GetAllTodoListItemEndpoint(DbContext dbContext)
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