using AdhdTimeOrganizer.Command.application.dto.request;
using AdhdTimeOrganizer.Command.application.dto.response.activityHistory;
using AdhdTimeOrganizer.Command.application.@interface.@base;
using AdhdTimeOrganizer.Command.application.service.@base;
using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Common.application.dto.request.generic;

namespace AdhdTimeOrganizer.Command.application.@interface.activityHistory;

public interface IAlarmService : IEntityWithActivityService<Alarm, AlarmRequest, AlarmResponse>
{
    Task SetIsActive(IEnumerable<IdRequest> requestList);
}