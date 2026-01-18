using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.plannerTask.command;

public class UpdatePlannerTaskEndpoint(AppCommandDbContext dbContext, PlannerTaskMapper mapper)
    : BaseUpdateEndpoint<PlannerTask, PlannerTaskRequest, PlannerTaskMapper>(dbContext, mapper);
