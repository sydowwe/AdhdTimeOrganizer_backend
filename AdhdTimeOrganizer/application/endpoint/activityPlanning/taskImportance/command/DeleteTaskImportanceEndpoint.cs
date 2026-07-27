using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskImportance.command;

public class DeleteTaskImportanceEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<TaskImportance>(dbContext);