using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.todoList.command;

public class TodoListUpdateEndpoint(AppCommandDbContext dbContext, TodoListMapper mapper)
    : BaseUpdateEndpoint<TodoList, TodoListRequest, TodoListResponse, TodoListMapper>(dbContext, mapper)
{
    protected override void AfterMapping(TodoList entity, TodoListRequest req)
    {
        if (req is { TotalCount: not null, DoneCount: null })
        {
            entity.DoneCount = 0;
        }
    }
}
