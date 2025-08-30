using AdhdTimeOrganizer.application.dto.request.plannerTask;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlanner.command;

public class PlannerTaskCreateEndpoint(AppCommandDbContext dbContext, PlannerTaskMapper mapper)
    : BaseCreateEndpoint<PlannerTask, PlannerTaskRequest, PlannerTaskResponse, PlannerTaskMapper>(dbContext, mapper);
