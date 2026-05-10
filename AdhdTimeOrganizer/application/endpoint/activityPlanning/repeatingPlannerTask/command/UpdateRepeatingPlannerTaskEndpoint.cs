using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.repeatingPlannerTask.command;

public class UpdateRepeatingPlannerTaskEndpoint(AppDbContext dbContext, RepeatingPlannerTaskMapper mapper)
    : BaseUpdateEndpoint<RepeatingPlannerTask, RepeatingPlannerTaskRequest, RepeatingPlannerTaskMapper>(dbContext, mapper);
