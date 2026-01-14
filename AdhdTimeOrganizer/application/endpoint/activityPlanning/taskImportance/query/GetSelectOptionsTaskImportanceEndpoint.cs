using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskImportance.query;

public class GetSelectOptionsTaskImportanceEndpoint(
    AppCommandDbContext appDbContext,
    TaskImportanceMapper mapper)
    : BaseGetSelectOptionsEndpoint<TaskImportance, TaskImportanceMapper>(appDbContext, mapper)
{
}
