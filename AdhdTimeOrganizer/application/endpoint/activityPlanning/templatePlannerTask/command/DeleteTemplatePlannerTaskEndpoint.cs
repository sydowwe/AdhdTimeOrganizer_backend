using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.templateTask.command;

public class DeleteTemplatePlannerTaskEndpoint(AppCommandDbContext dbContext)
    : BaseDeleteEndpoint<TemplatePlannerTask>(dbContext)
{
}
