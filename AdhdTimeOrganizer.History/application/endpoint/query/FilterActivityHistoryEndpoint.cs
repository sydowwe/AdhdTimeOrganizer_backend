using AdhdTimeOrganizer.History.application.dto.filter.history;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.query;

public class FilterActivityHistoryEndpoint(DbContext dbContext) : BaseFilterEndpoint<ActivityHistory, ActivityHistoryResponse, ActivityHistoryDetailFilter>(dbContext)
{
    public override SortByRequest[] AlwaysSortBy => [new("StartTimestamp", false), new("EndTimestamp", false)];

    protected override IQueryable<ActivityHistory> ApplyCustomFiltering(IQueryable<ActivityHistory> query, ActivityHistoryDetailFilter filter)
    {
        var (from, to) = filter.ToDateTimeRange();

        query = query.Where(ah => ah.StartTimestamp >= from && ah.EndTimestamp <= to);

        return query;
    }
}