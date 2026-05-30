using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskImportance.command;

public class CreateTaskImportanceEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<TaskImportance, TaskImportanceRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TaskImportanceValidator>();
    }
}
