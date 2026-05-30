using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.repeatingPlannerTask.query;

public class GetByIdRepeatingPlannerTaskEndpoint(AppDbContext dbContext)
    : BaseGetByIdEndpoint<RepeatingPlannerTask, RepeatingPlannerTaskResponse>(dbContext)
{
}
