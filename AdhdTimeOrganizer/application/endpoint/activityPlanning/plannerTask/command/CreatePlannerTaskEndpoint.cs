using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.plannerTask.command;

public class CreatePlannerTaskEndpoint(AppCommandDbContext dbContext, PlannerTaskMapper mapper)
    : BaseCreateEndpoint<PlannerTask, PlannerTaskRequest, PlannerTaskMapper>(dbContext, mapper);
