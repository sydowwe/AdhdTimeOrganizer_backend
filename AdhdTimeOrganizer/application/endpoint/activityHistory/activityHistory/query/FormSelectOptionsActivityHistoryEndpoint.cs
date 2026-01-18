using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.query;

public class FormSelectOptionsActivityHistoryEndpoint(AppCommandDbContext appDbContext) 
    : BaseActivityFormSelectOptionsEndpoint<ActivityHistory>(appDbContext)
{
    public override string EntityRoute => "activity-history";

    protected override IQueryable<Activity> GetBaseQuery(long userId)
    {
        return _appDbContext.Set<ActivityHistory>()
            .AsNoTracking()
            .FilteredByUser(userId)
            .Select(ah => ah.Activity)
            .Distinct();
    }
}
