using AdhdTimeOrganizer.application.dto.request.filter;
using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory;

public class GetAlarmsFilteredPaginatedEndpoint(
    AppCommandDbContext dbContext,
    AlarmMapper mapper) 
    : BaseFilteredPaginatedEndpoint<Alarm, AlarmResponse, AlarmFilterRequest>(dbContext)
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

    protected override AlarmResponse MapToResponse(Alarm entity)
    {
        return _mapper.ToResponse(entity);
    }
}
