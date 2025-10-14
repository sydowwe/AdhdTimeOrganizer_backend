using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.todoList.query;

public class GetSelectOptionsTodoListEndpoint(
    AppCommandDbContext appDbContext,
    TodoListMapper mapper)
    : BaseGetSelectOptionsEndpoint<TodoList, TodoListMapper>(appDbContext, mapper)
{
}
