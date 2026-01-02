using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.alarm.query;

public class GetAlarmsFetchTableEndpoint(
    AppCommandDbContext dbContext,
    AlarmMapper mapper) 
    : BaseFetchTableEndpoint<Alarm, AlarmResponse, AlarmFilterRequest, AlarmMapper>(dbContext, mapper)
{
    private readonly AlarmMapper _mapper = mapper;

    protected override IQueryable<Alarm> WithIncludes(IQueryable<Alarm> query)
    {
        return query
            .Include(a => a.Activity)
                .ThenInclude(act => act.Role)
            .Include(a => a.Activity)
                .ThenInclude(act => act.Category);
    }

    protected override IQueryable<Alarm> ApplyCustomFiltering(IQueryable<Alarm> query, AlarmFilterRequest filter)
    {
        if (filter.ActivityId.HasValue)
        {
            query = query.Where(a => a.ActivityId == filter.ActivityId.Value);
        }

        if (filter.StartTimestampAfter.HasValue)
        {
            query = query.Where(a => a.StartTimestamp >= filter.StartTimestampAfter.Value);
        }

        if (filter.StartTimestampBefore.HasValue)
        {
            query = query.Where(a => a.StartTimestamp <= filter.StartTimestampBefore.Value);
        }

        if (filter.IsActive.HasValue)
        {
            query = query.Where(a => a.IsActive == filter.IsActive.Value);
        }

        if (filter.UserId.HasValue)
        {
            query = query.Where(a => a.UserId == filter.UserId.Value);
        }

        return query;
    }
}
