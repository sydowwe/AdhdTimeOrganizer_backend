using AdhdTimeOrganizer.Command.application.dto.request.activity;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;
using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Command.application.dto.response.extendable;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Common.application.@interface;

namespace AdhdTimeOrganizer.Command.application.@interface.@base;

public interface
    IEntityWithActivityService<TEntity, in TRequest, TResponse> : IBaseWithUserService<TEntity, TRequest, TResponse>
    where TEntity : BaseEntityWithActivity
    where TRequest : IActivityIdRequest
    where TResponse : IEntityWithActivityResponse
{
    Task<List<ActivityFormSelectOptionsResponse>> GetAllActivityFormSelectOptions();
}