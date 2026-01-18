using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.query;

public class GetSelectOptionsRoutineTodoListEndpoint(
    AppCommandDbContext appDbContext,
    RoutineTodoListMapper mapper)
    : BaseGetSelectOptionsEndpoint<RoutineTodoList, RoutineTodoListMapper>(appDbContext, mapper)
{
}
