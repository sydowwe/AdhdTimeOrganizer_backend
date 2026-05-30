using AdhdTimeOrganizer.application.dto.response.todoList;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.query;

public class GetAllRoutineTodoListEndpoint(
    AppDbContext dbContext)
    : BaseGetAllEndpoint<RoutineTodoList, RoutineTodoListResponse>(dbContext)
{
}
