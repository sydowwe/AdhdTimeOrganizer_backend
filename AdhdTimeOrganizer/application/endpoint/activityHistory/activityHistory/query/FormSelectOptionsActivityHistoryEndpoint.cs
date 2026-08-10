using AdhdTimeOrganizer.Core.application.endpoint.@base.read;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.query;

public class FormSelectOptionsActivityHistoryEndpoint(AppDbContext appDbContext)
    : BaseActivityFormSelectOptionsEndpoint<ActivityHistory>(appDbContext)
{
    public override string EntityRoute => "activity-history";

    protected override IQueryable<Activity> GetBaseQuery(long userId)
    {
        return DbContext.Set<ActivityHistory>()
            .AsNoTracking()
            .FilteredByUser(userId)
            .Select(ah => ah.Activity)
            .Distinct();
    }
}