using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.query;

public class ActivityFormSelectOptionsEndpoint(AppCommandDbContext appDbContext) 
    : BaseActivityFormSelectOptionsEndpoint<Activity>(appDbContext)
{
    public override string EntityRoute => "activity";

    protected override IQueryable<Activity> GetBaseQuery(long userId)
    {
        return _appDbContext.Set<Activity>()
            .AsNoTracking()
            .FilteredByUser(userId);
    }
}
