using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.repeatingPlannerTask.command;

public class UpdateRepeatingPlannerTaskEndpoint(DbContext dbContext)
    : BaseUpdateEndpoint<RepeatingPlannerTask, RepeatingPlannerTaskRequest>(dbContext);