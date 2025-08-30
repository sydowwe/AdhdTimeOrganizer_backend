using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory;

public class GetAlarmByIdEndpoint(
    AppCommandDbContext dbContext,
    AlarmMapper mapper)
    : BaseGetByIdEndpoint<Alarm, AlarmResponse, AlarmMapper>(dbContext, mapper)
{
    protected override IQueryable<Alarm> WithIncludes(IQueryable<Alarm> query)
    {
        return query
            .Include(a => a.Activity)
                .ThenInclude(act => act.Role)
            .Include(a => a.Activity)
                .ThenInclude(act => act.Category);
    }
}
