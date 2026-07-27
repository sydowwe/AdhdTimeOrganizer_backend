using AdhdTimeOrganizer.application.dto.response.todoList;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.query;

public class GetAllRoutineTodoListEndpoint(
    AppDbContext dbContext)
    : BaseGetAllEndpoint<RoutineTodoList, RoutineTodoListResponse>(dbContext)
{
}