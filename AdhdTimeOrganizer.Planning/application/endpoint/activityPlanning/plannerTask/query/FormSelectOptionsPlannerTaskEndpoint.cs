using AdhdTimeOrganizer.Core.application.endpoint.@base.read;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.query;

public class FormSelectOptionsPlannerTaskEndpoint(DbContext appDbContext)
    : BaseActivityFormSelectOptionsEndpoint<PlannerTask>(appDbContext)
{
    public override string EntityRoute => "planner-task";

    protected override IQueryable<Activity> GetBaseQuery(long userId)
    {
        return DbContext.Set<PlannerTask>()
            .AsNoTracking()
            .FilteredByUser(userId)
            .Select(pt => pt.Activity)
            .Distinct();
    }
}