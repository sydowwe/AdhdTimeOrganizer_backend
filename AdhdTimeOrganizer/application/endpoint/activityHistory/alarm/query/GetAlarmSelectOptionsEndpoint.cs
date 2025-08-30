using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory;

public class GetAlarmSelectOptionsEndpoint(
    AppCommandDbContext appDbContext,
    AlarmMapper mapper)
    : BaseGetSelectOptionsEndpoint<Alarm, AlarmMapper>(appDbContext, mapper)
{
}
