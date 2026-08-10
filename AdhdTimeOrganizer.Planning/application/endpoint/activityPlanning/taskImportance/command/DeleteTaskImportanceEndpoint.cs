using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.taskImportance.command;

public class DeleteTaskImportanceEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<TaskImportance>(dbContext);