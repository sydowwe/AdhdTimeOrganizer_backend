using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.repeatingPlannerTask.query;

public class GetAllRepeatingPlannerTaskEndpoint(AppDbContext dbContext)
    : BaseGetAllEndpoint<RepeatingPlannerTask, RepeatingPlannerTaskResponse>(dbContext)
{
}