using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.taskPriority.query;

public class GetAllTaskPriorityEndpoint(
    AppDbContext dbContext)
    : BaseGetAllEndpoint<TaskPriority, TaskPriorityResponse>(dbContext)
{
}
