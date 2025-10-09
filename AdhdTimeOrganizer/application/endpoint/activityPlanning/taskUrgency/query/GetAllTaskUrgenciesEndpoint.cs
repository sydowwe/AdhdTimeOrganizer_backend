using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.TaskPriority.query;

public class GetAllTaskUrgenciesEndpoint(
    AppCommandDbContext dbContext,
    TaskPriorityMapper mapper)
    : BaseGetAllEndpoint<TaskPriority, TaskPriorityResponse, TaskPriorityMapper>(dbContext, mapper)
{
}
