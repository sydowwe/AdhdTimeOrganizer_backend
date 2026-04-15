using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using RoutineTodoListMapper = AdhdTimeOrganizer.application.mapper.todoList.RoutineTodoListMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.command;

public class UpdateRoutineTodoListEndpoint(AppDbContext dbContext, RoutineTodoListMapper mapper)
    : BaseUpdateEndpoint<RoutineTodoList, UpdateRoutineTodoListRequest, RoutineTodoListMapper>(dbContext, mapper)
{
    protected override Task AfterMapping(RoutineTodoList entity, UpdateRoutineTodoListRequest req, CancellationToken ct = default)
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
        return Task.CompletedTask;
    }
}
