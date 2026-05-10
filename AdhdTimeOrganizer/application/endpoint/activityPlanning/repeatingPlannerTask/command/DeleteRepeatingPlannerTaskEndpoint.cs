using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.repeatingPlannerTask.command;

public class DeleteRepeatingPlannerTaskEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<RepeatingPlannerTask>(dbContext);
