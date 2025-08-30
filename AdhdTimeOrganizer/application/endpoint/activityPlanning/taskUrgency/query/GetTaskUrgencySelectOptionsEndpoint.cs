using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning;

public class GetTaskUrgencySelectOptionsEndpoint(
    AppCommandDbContext appDbContext,
    TaskUrgencyMapper mapper)
    : BaseGetSelectOptionsEndpoint<TaskUrgency, TaskUrgencyMapper>(appDbContext, mapper)
{
}
