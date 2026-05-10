using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.repeatingPlannerTask.command;

public class CreateRepeatingPlannerTaskEndpoint(AppDbContext dbContext, RepeatingPlannerTaskMapper mapper)
    : BaseCreateEndpoint<RepeatingPlannerTask, RepeatingPlannerTaskRequest, RepeatingPlannerTaskMapper>(dbContext, mapper);
