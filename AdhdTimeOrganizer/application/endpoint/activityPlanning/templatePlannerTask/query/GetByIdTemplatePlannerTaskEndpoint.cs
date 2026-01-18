using AdhdTimeOrganizer.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using TemplatePlannerTaskMapper = AdhdTimeOrganizer.application.mapper.activityPlanning.TemplatePlannerTaskMapper;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.templatePlannerTask.query;

public class GetByIdTemplatePlannerTaskEndpoint(AppCommandDbContext dbContext, TemplatePlannerTaskMapper mapper)
    : BaseGetByIdEndpoint<TemplatePlannerTask, TemplatePlannerTaskResponse, TemplatePlannerTaskMapper>(dbContext, mapper)
{
    protected override IQueryable<TemplatePlannerTask> WithIncludes(IQueryable<TemplatePlannerTask> query)
    {
        return query
            .Include(t => t.Activity)
            .ThenInclude(a => a.Role)
            .Include(t => t.Activity)
            .ThenInclude(a => a.Category)
            .Include(t => t.Importance);
    }
}
