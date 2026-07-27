using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.plannerTask.command;

public class DeletePlannerTaskEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<PlannerTask>(dbContext);