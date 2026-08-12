using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.repeatingPlannerTask.query;

public class GetAllRepeatingPlannerTaskEndpoint(DbContext dbContext)
    : BaseGetAllEndpoint<RepeatingPlannerTask, RepeatingPlannerTaskResponse>(dbContext)
{
}