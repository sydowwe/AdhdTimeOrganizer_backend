using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.plannerTask.query;

public class FormSelectOptionsPlannerTaskEndpoint(AppCommandDbContext appDbContext) 
    : BaseActivityFormSelectOptionsEndpoint<PlannerTask>(appDbContext)
{
    public override string EntityRoute => "planner-task";

    protected override IQueryable<Activity> GetBaseQuery(long userId)
    {
        return _appDbContext.Set<PlannerTask>()
            .AsNoTracking()
            .FilteredByUser(userId)
            .Select(pt => pt.Activity)
            .Distinct();
    }
}
