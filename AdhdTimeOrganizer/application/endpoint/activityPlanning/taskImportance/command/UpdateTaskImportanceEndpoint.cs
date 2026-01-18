using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskImportance.command;

public class UpdateTaskImportanceEndpoint(AppCommandDbContext dbContext, TaskImportanceMapper mapper)
    : BaseUpdateEndpoint<TaskImportance, TaskImportanceRequest, TaskImportanceMapper>(dbContext, mapper);
