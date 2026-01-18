using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskImportance.query;

public class GetAllTaskImportanceEndpoint(
    AppCommandDbContext dbContext,
    TaskImportanceMapper mapper)
    : BaseGetAllEndpoint<TaskImportance, TaskImportanceResponse, TaskImportanceMapper>(dbContext, mapper)
{
}
