using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.taskPlannerDayTemplate.command;

public class DeleteTaskPlannerDayTemplateEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<TaskPlannerDayTemplate>(dbContext)
{
}