using AdhdTimeOrganizer.application.dto.filter.history;
using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.query;

public class GetFilteredActivityHistoryEndpoint(AppDbContext dbContext, ActivityHistoryMapper mapper) : BaseFilterEndpoint<ActivityHistory, ActivityHistoryResponse, ActivityHistoryDetailFilter, ActivityHistoryMapper>(dbContext, mapper)
{
    public override SortByRequest[]? DefaultSortBy => [new SortByRequest("StartTimestamp", false), new SortByRequest("EndTimestamp", false)];

    protected override IQueryable<ActivityHistory> ApplyCustomFiltering(IQueryable<ActivityHistory> query, ActivityHistoryDetailFilter filter)
    {
        var (from, to) = filter.ToDateTimeRange();

        query = query.Where(ah => ah.StartTimestamp >= from && ah.EndTimestamp <= to);

        return query;
    }

    protected override IQueryable<ActivityHistory> WithIncludes(IQueryable<ActivityHistory> query)
    {
        return query
            .Include(ah => ah.Activity)
            .ThenInclude(a => a.Role)
            .Include(ah => ah.Activity)
            .ThenInclude(a => a.Category);
    }
}