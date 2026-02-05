using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.command;

public class DeleteTaskPlannerDayTemplateEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<TaskPlannerDayTemplate>(dbContext)
{
}
