using AdhdTimeOrganizer.application.dto.request.todoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.result;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.infrastructure.settings;
using Microsoft.Extensions.Options;
using RoutineTodoListMapper = AdhdTimeOrganizer.application.mapper.todoList.RoutineTodoListMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.command;

public class CreateRoutineTodoListEndpoint(AppDbContext dbContext, RoutineTodoListMapper mapper, IOptions<TodoListSettings> settings)
    : BaseCreateEndpoint<RoutineTodoList, CreateRoutineTodoListRequest, RoutineTodoListMapper>(dbContext, mapper)
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly TodoListSettings _settings = settings.Value;

    public override void Configure()
    {
        base.Configure();
        Validator<CreateRoutineTodoListValidator>();
    }

    protected override async Task<Result> AfterMapping(RoutineTodoList entity, CreateRoutineTodoListRequest req, CancellationToken ct = default)
    {
        entity.DisplayOrder = await _dbContext.RoutineTodoLists.GetNextDisplayOrder(_settings, User.GetId(), entity.TimePeriodId, ct);

        if (req.Steps is { Count: > 0 })
        {
            entity.Steps = req.Steps.Select(s => new TodoListStep { Name = s.Name, Order = s.Order, Note = s.Note }).ToList();
            entity.DoneCount = 0;
        }
        else if (entity.TotalCount.HasValue)
        {
            entity.DoneCount = 0;
        }

        return Result.Successful();
    }
}