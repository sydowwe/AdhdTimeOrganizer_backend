using AdhdTimeOrganizer.Core.application.endpoint.@base.read;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.activity.query;

public class ActivityFormSelectOptionsEndpoint(DbContext appDbContext)
    : BaseActivityFormSelectOptionsEndpoint<Activity>(appDbContext)
{
    public override string EntityRoute => "activity";

    protected override IQueryable<Activity> GetBaseQuery(long userId) =>
        DbContext.Set<Activity>()
            .AsNoTracking()
            .FilteredByUser(userId);
}