using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.repeatingPlannerTask.command;

public class DeleteRepeatingPlannerTaskEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<RepeatingPlannerTask>(dbContext);