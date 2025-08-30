using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.query;

public class GetAllActivityHistoriesEndpoint(
    AppCommandDbContext dbContext,
    ActivityHistoryMapper mapper)
    : BaseGetAllEndpoint<ActivityHistory, ActivityHistoryResponse, ActivityHistoryMapper>(dbContext, mapper)
{
    protected override IQueryable<ActivityHistory> WithIncludes(IQueryable<ActivityHistory> query)
    {
        return query
            .Include(ah => ah.Activity)
                .ThenInclude(a => a.Role)
            .Include(ah => ah.Activity)
                .ThenInclude(a => a.Category);
    }
}
