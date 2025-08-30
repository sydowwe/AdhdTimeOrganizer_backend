using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskUrgency.query;

public class GetTaskUrgencyByIdEndpoint(
    AppCommandDbContext dbContext,
    TaskUrgencyMapper mapper)
    : BaseGetByIdEndpoint<TaskUrgency, TaskUrgencyResponse, TaskUrgencyMapper>(dbContext, mapper)
{
}
