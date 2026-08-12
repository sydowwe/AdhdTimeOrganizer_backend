using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.application.validator;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.command;

public class UpdateTodoListItemEndpoint(DbContext dbContext)
    : BaseUpdateEndpoint<TodoListItem, UpdateTodoListItemRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<UpdateTodoListItemValidator>();
    }

    protected override Task<bool> AfterMapping(TodoListItem entity, UpdateTodoListItemRequest req, CancellationToken ct = default)
    {
        if (req.Steps is not null)
            entity.Steps = req.Steps.Select(s => new TodoListStep
            {
                Name = s.Name,
                Order = s.Order,
                Note = s.Note,
                IsDone = s.Id.HasValue && entity.Steps.FirstOrDefault(e => e.Id == s.Id.Value)?.IsDone == true
            }).ToList();

        if (req is { TotalCount: not null, DoneCount: null })
            entity.DoneCount = 0;

        return Task.FromResult(true);
    }
}