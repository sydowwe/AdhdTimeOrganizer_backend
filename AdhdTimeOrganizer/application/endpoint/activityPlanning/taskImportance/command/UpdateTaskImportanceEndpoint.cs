using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskImportance.command;

public class UpdateTaskImportanceEndpoint(AppDbContext dbContext)
    : BaseUpdateEndpoint<TaskImportance, TaskImportanceRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TaskImportanceValidator>();
    }
}