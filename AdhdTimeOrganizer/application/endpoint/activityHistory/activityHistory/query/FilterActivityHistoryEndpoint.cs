using AdhdTimeOrganizer.application.dto.filter.history;
using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.query;

public class FilterActivityHistoryEndpoint(AppDbContext dbContext) : BaseFilterEndpoint<ActivityHistory, ActivityHistoryResponse, ActivityHistoryDetailFilter>(dbContext)
{
    public override SortByRequest[] AlwaysSortBy => [new("StartTimestamp", false), new("EndTimestamp", false)];

    protected override IQueryable<ActivityHistory> ApplyCustomFiltering(IQueryable<ActivityHistory> query, ActivityHistoryDetailFilter filter)
    {
        var (from, to) = filter.ToDateTimeRange();

        query = query.Where(ah => ah.StartTimestamp >= from && ah.EndTimestamp <= to);

        return query;
    }
}