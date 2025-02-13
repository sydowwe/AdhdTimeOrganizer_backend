using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.domain.model.entity.user;
using AdhdTimeOrganizer.Common.application.dto.request.@base;
using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Web.controller.@base;

public abstract class BaseWithUserController<TEntity, TRequest, TResponse, TService>(TService service)
    : BaseCrudController<TEntity, TRequest, TResponse, TService>(service)
    where TEntity : BaseEntityWithUser
    where TRequest : class, IMyRequest
    where TResponse : class, IIdResponse
    where TService : IBaseWithUserService<TEntity, TRequest, TResponse>
{
    private TService _service = service;

}