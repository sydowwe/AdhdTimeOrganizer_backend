using AdhdTimeOrganizer.application.dto.request.todoList;
using AdhdTimeOrganizer.application.dto.response.todoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Humanizer;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.command;

public class ChangePriorityTodoListItemEndpoint(AppDbContext dbContext)
    : BasePatchEndpoint<TodoListItem, ChangePriorityTodoListItemRequest, TodoListItemResponse>(dbContext)
{
    public override void Configure()
    {
        const string entityName = nameof(TodoListItem);
        Patch($"/{entityName.Kebaberize()}/{{id}}/priority");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Change priority of {entityName}";
            s.Description = $"Changes the priority of an existing {entityName}";
            s.Response(204, "Success");
            s.Response(404, "Not found");
            s.Response(400, "Bad request");
        });
    }

    protected override void Mapping(TodoListItem entity, ChangePriorityTodoListItemRequest req)
    {
        entity.TaskPriorityId = req.PriorityId;
    }
}
