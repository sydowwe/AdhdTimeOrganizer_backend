using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.taskImportance.query;

public class GetAllTaskImportanceEndpoint(
    DbContext dbContext)
    : BaseGetAllEndpoint<TaskImportance, TaskImportanceResponse>(dbContext)
{
}