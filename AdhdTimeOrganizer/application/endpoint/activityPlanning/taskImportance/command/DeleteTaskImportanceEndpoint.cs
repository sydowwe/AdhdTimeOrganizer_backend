using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskImportance.command;

public class DeleteTaskImportanceEndpoint(AppCommandDbContext dbContext)
    : BaseDeleteEndpoint<TaskImportance>(dbContext);
