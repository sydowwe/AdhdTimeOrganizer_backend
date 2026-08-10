using AdhdTimeOrganizer.Routines.application.dto.request.todoList;
using AdhdTimeOrganizer.Routines.application.validator;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.settings;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTodoList.command;

public class CreateRoutineTodoListEndpoint(DbContext dbContext, IOptions<TodoListSettings> settings)
    : BaseCreateEndpoint<RoutineTodoList, CreateRoutineTodoListRequest>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;
    private readonly TodoListSettings _settings = settings.Value;

    public override void Configure()
    {
        base.Configure();
        Validator<CreateRoutineTodoListValidator>();
    }

    protected override async Task<bool> AfterMapping(RoutineTodoList entity, CreateRoutineTodoListRequest req, CancellationToken ct = default)
    {
        entity.DisplayOrder = await _dbContext.Set<RoutineTodoList>().GetNextDisplayOrder(_settings, User.GetId(), entity.TimePeriodId, ct);

        if (req.Steps is { Count: > 0 })
        {
            entity.Steps = req.Steps.Select(s => new TodoListStep { Name = s.Name, Order = s.Order, Note = s.Note }).ToList();
            entity.DoneCount = 0;
        }
        else if (entity.TotalCount.HasValue)
        {
            entity.DoneCount = 0;
        }

        return true;
    }
}