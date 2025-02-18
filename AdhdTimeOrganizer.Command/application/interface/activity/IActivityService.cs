using AdhdTimeOrganizer.Command.application.dto.request.activity;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;
using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Common.application.dto.request.@base;
using AdhdTimeOrganizer.Common.domain.result;

namespace AdhdTimeOrganizer.Command.application.@interface.activity;

public interface IActivityService : IBaseWithUserService<Activity, ActivityRequest, ActivityResponse>
{
    Task<List<ActivityFormSelectOptionsResponse>> GetAllFormSelectOptions();
    Task<ServiceResult<ActivityResponse>> QuickUpdateAsync(long id, NameTextRequest request);
}