using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.taskPriority.query;

public class GetAllTaskPriorityEndpoint(
    AppCommandDbContext dbContext,
    TaskPriorityMapper mapper)
    : BaseGetAllEndpoint<TaskPriority, TaskPriorityResponse, TaskPriorityMapper>(dbContext, mapper)
{
}
