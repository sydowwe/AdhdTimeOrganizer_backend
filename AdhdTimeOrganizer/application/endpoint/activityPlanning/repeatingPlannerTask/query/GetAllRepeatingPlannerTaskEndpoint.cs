using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.repeatingPlannerTask.query;

public class GetAllRepeatingPlannerTaskEndpoint(AppDbContext dbContext, RepeatingPlannerTaskMapper mapper)
    : BaseGetAllEndpoint<RepeatingPlannerTask, RepeatingPlannerTaskResponse, RepeatingPlannerTaskMapper>(dbContext, mapper)
{
    protected override IQueryable<RepeatingPlannerTask> WithIncludes(IQueryable<RepeatingPlannerTask> query)
    {
        return query
            .Include(t => t.Importance)
            .Include(t => t.Activity).ThenInclude(a => a.Role)
            .Include(t => t.Activity).ThenInclude(a => a.Category);
    }
}
