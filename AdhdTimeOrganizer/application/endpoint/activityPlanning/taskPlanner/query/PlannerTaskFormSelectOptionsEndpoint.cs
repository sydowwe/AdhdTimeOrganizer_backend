using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.extension;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activity.read;

public class PlannerTaskFormSelectOptionsEndpoint(AppCommandDbContext appDbContext) 
    : BaseActivityFormSelectOptionsEndpoint<PlannerTask>(appDbContext)
{
    public override string EntityRoute => "task-planner";

    protected override IQueryable<Activity> GetBaseQuery(long userId)
    {
        return _appDbContext.Set<PlannerTask>()
            .AsNoTracking()
            .FilteredByUser(userId)
            .Select(pt => pt.Activity)
            .Distinct();
    }
}
