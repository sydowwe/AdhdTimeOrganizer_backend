using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlanner.query;

public class GetSelectOptionsPlannerTaskEndpoint(
    AppCommandDbContext appDbContext,
    PlannerTaskMapper mapper)
    : BaseGetSelectOptionsEndpoint<PlannerTask, PlannerTaskMapper>(appDbContext, mapper)
{
}
