using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.repeatingPlannerTask.command;

public class DeleteRepeatingPlannerTaskEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<RepeatingPlannerTask>(dbContext);