using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.repeatingPlannerTask.command;

public class UpdateRepeatingPlannerTaskEndpoint(AppDbContext dbContext)
    : BaseUpdateEndpoint<RepeatingPlannerTask, RepeatingPlannerTaskRequest>(dbContext);
