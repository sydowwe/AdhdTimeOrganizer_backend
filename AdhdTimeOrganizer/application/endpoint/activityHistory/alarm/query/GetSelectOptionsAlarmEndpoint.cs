using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.alarm.query;

public class GetSelectOptionsAlarmEndpoint(
    AppCommandDbContext appDbContext,
    AlarmMapper mapper)
    : BaseGetSelectOptionsEndpoint<Alarm, AlarmMapper>(appDbContext, mapper)
{
}
