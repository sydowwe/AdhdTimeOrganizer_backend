using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.plannerTask.command;

public class DeletePlannerTaskEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<PlannerTask>(dbContext);
