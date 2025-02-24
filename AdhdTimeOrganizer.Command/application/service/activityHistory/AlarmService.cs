using AdhdTimeOrganizer.Command.application.dto.request;
using AdhdTimeOrganizer.Command.application.dto.response.activityHistory;
using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.application.@interface.activityHistory;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.application.service.@base;
using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activityHistory;
using AdhdTimeOrganizer.Common.application.dto.request.generic;
using AutoMapper;

namespace AdhdTimeOrganizer.Command.application.service.activityHistory;

public class AlarmService(IAlarmRepository repository, IActivityService activityService, ILoggedUserService loggedUserService, IMapper mapper)
    : EntityWithActivityService<Alarm, AlarmRequest, AlarmResponse, IAlarmRepository>(repository, activityService, loggedUserService, mapper), IAlarmService
{
    public async Task SetIsActive(IEnumerable<IdRequest> requestList)
    {
        var ids = requestList.Select(req => req.Id);
        var affectedRows = await _repository.UpdateIsActiveByIds(ids);
        if (affectedRows <= 0)
        {
            //throw new UpdateFailedException();
        }
    }
}