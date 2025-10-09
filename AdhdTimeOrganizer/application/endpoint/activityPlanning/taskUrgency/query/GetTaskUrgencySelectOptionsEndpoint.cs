using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.TaskPriority.query;

public class GetTaskPrioritySelectOptionsEndpoint(
    AppCommandDbContext appDbContext,
    TaskPriorityMapper mapper)
    : BaseGetSelectOptionsEndpoint<TaskPriority, TaskPriorityMapper>(appDbContext, mapper)
{
}
