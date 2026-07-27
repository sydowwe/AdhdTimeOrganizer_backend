using AdhdTimeOrganizer.application.dto.request.todoList;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Humanizer;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.command;

public class ChangePriorityTodoListItemEndpoint(AppDbContext dbContext)
    : BasePatchEndpoint<TodoListItem, ChangePriorityTodoListItemRequest>(dbContext)
{
    public override void Configure()
    {
        const string entityName = nameof(TodoListItem);
        Patch($"/{entityName.Kebaberize()}/{{id:long:required}}/priority");
        Validator<ChangePriorityTodoListItemValidator>();

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