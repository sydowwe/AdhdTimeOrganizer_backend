using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.taskImportance.command;

public class UpdateTaskImportanceEndpoint(DbContext dbContext)
    : BaseUpdateEndpoint<TaskImportance, TaskImportanceRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TaskImportanceValidator>();
    }
}