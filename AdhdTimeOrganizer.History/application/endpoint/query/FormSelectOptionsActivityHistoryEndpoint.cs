using AdhdTimeOrganizer.Core.application.endpoint.@base.read;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.query;

public class FormSelectOptionsActivityHistoryEndpoint(DbContext dbContext)
    : BaseActivityFormSelectOptionsEndpoint<ActivityHistory>(dbContext)
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