using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPriority.query;

public class GetByIdTaskPriorityEndpoint(
    AppCommandDbContext dbContext,
    TaskPriorityMapper mapper)
    : BaseGetByIdEndpoint<TaskPriority, TaskPriorityResponse, TaskPriorityMapper>(dbContext, mapper)
{
}
