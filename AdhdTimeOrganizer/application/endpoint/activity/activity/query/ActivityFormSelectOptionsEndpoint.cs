using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.query;

public class ActivityFormSelectOptionsEndpoint(AppDbContext appDbContext)
    : BaseActivityFormSelectOptionsEndpoint<Activity>(appDbContext)
{
    public override string EntityRoute => "activity";

    protected override IQueryable<Activity> GetBaseQuery(long userId) =>
        AppDbContext.Set<Activity>()
            .AsNoTracking()
            .FilteredByUser(userId);
}