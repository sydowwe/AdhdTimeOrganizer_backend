using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.command;

public class DeleteTaskPlannerDayTemplateEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<TaskPlannerDayTemplate>(dbContext)
{
}