using AdhdTimeOrganizer.Command.application.dto.request.activity;
using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Command.application.dto.response.extendable;
using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.application.@interface.@base;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Command.domain.repositoryContract;
using AdhdTimeOrganizer.Common.application.@interface;
using AdhdTimeOrganizer.Common.application.service;
using AutoMapper;

namespace AdhdTimeOrganizer.Command.application.service.@base;

public abstract class EntityWithActivityService<TEntity, TRequest, TResponse, TRepository>(
    TRepository repository,
    IActivityService activityService,
    ILoggedUserService loggedUserService,
    IMapper mapper
) : BaseCrudServiceWithUser<TEntity, TRequest, TResponse, TRepository>(repository, loggedUserService, mapper),
    IEntityWithActivityService<TEntity, TRequest, TResponse>
    where TEntity : BaseEntityWithActivity
    where TRequest : class, IActivityIdRequest
    where TResponse : class, IEntityWithActivityResponse
    where TRepository : IBaseEntityWithActivityRepository<TEntity>
{
    protected readonly IActivityService activityService = activityService;

    public async Task<List<ActivityFormSelectOptionsResponse>> GetAllActivityFormSelectOptions()
    {
        return await ProjectFromQueryToListAsync<ActivityFormSelectOptionsResponse>(
            _repository.GetDistinctActivities(LoggedUserId));
    }

}