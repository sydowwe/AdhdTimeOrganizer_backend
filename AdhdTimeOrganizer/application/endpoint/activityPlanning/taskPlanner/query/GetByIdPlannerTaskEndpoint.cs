using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlanner.query;

public class GetByIdPlannerTaskEndpoint(
    AppCommandDbContext dbContext,
    PlannerTaskMapper mapper)
    : BaseGetByIdEndpoint<PlannerTask, PlannerTaskResponse, PlannerTaskMapper>(dbContext, mapper)
{
    protected override IQueryable<PlannerTask> WithIncludes(IQueryable<PlannerTask> query)
    {
        return query
            .Include(pt => pt.Activity)
                .ThenInclude(a => a.Role)
            .Include(pt => pt.Activity)
                .ThenInclude(a => a.Category);
    }
}
