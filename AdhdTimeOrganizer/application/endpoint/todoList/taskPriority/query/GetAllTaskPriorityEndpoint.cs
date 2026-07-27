using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.todoList.taskPriority.query;

public class GetAllTaskPriorityEndpoint(
    AppDbContext dbContext)
    : BaseGetAllEndpoint<TaskPriority, TaskPriorityResponse>(dbContext)
{
}