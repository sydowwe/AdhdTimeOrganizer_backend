using AdhdTimeOrganizer.application.dto.request.todoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.command;

public class UpdateRoutineTodoListEndpoint(AppDbContext dbContext)
    : BaseUpdateEndpoint<RoutineTodoList, UpdateRoutineTodoListRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<UpdateRoutineTodoListValidator>();
    }

    protected override Task<bool> AfterMapping(RoutineTodoList entity, UpdateRoutineTodoListRequest req, CancellationToken ct = default)
    {
        if (req.Steps is not null)
        {
            entity.Steps = req.Steps.Select(s => new TodoListStep
            {
                Name = s.Name,
                Order = s.Order,
                Note = s.Note,
                IsDone = s.Id.HasValue && entity.Steps.FirstOrDefault(e => e.Id == s.Id.Value)?.IsDone == true
            }).ToList();
        }

        if (req is { TotalCount: not null, DoneCount: null })
        {
            entity.DoneCount = 0;
        }

        return Task.FromResult(true);
    }
}
