using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskImportance.query;

public class GetAllTaskImportanceEndpoint(
    AppDbContext dbContext)
    : BaseGetAllEndpoint<TaskImportance, TaskImportanceResponse>(dbContext)
{
}