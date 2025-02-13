using AdhdTimeOrganizer.Command.application.dto.request.extendable;
using AdhdTimeOrganizer.Command.application.dto.response.extendable;
using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.application.@interface.@base;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.domain.model.entity.@base;
using AdhdTimeOrganizer.Command.domain.repositoryContract;
using AdhdTimeOrganizer.Common.application.dto.request.generic;
using AutoMapper;

namespace AdhdTimeOrganizer.Command.application.service.@base;

public abstract class EntityWithIsDoneService<TEntity, TRequest, TResponse, TRepository>(
    TRepository repository,
    IActivityService activityService,
    ILoggedUserService loggedUserService,
    IMapper mapper
) : EntityWithActivityService<TEntity, TRequest, TResponse, TRepository>(repository, activityService,loggedUserService,
        mapper),
    IEntityWithIsDoneService<TEntity, TRequest, TResponse>
    where TEntity : BaseEntityWithIsDone
    where TRequest : WithIsDoneRequest
    where TResponse : WithIsDoneResponse
    where TRepository : IBaseEntityWithIsDoneRepository<TEntity>
{
    public async Task SetIsDoneAsync(IEnumerable<IdRequest> requestList)
    {
        var ids = requestList.Select(req => req.Id);
        var affectedRows = await _repository.UpdateIsDoneByIdsAsync(ids);
        if (affectedRows <= 0)
        {
            //throw new UpdateFailedException();
        }
    }
}