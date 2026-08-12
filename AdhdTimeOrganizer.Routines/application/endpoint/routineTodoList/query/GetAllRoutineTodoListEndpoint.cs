using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTodoList.query;

public class GetAllRoutineTodoListEndpoint(
    DbContext dbContext)
    : BaseGetAllEndpoint<RoutineTodoList, RoutineTodoListResponse>(dbContext)
{
}