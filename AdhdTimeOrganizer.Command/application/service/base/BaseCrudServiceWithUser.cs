using AdhdTimeOrganizer.Command.application.dto.request.activity;
using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.domain.model.entity.user;
using AdhdTimeOrganizer.Command.domain.repositoryContract;
using AdhdTimeOrganizer.Common.application.dto.request.@base;
using AdhdTimeOrganizer.Common.application.dto.response.@base;
using AdhdTimeOrganizer.Common.domain.model.valueObject;
using AdhdTimeOrganizer.Common.domain.result;
using AutoMapper;

namespace AdhdTimeOrganizer.Command.application.service.@base;

public abstract class BaseCrudServiceWithUser<TEntity, TRequest, TResponse, TRepository>(
    TRepository repository,
    ILoggedUserService loggedUserService,
    IMapper mapper
) : BaseCrudService<TEntity, TRequest, TResponse, TRepository>(repository, mapper),
    IBaseWithUserService<TEntity, TRequest, TResponse>
    where TEntity : BaseEntityWithUser
    where TRequest : class, IMyRequest
    where TResponse : class, IMyResponse
    where TRepository : IBaseEntityWithUserRepository<TEntity>
{
    protected readonly ILoggedUserService _loggedUserService = loggedUserService;


    public async Task<ServiceResult<TEntity>> GetSingleForCurrentUser()
    {
        return await GetSingleByUserId(LoggedUserId);
    }
    public async Task<ServiceResult<TEntity>> GetSingleByUserId(long userId)
    {
        return ProcessRepositoryResultWithEntity(await _repository.GetSingleByUserId(userId));
    }
    public async Task<List<TResponse>> GetAllForCurrentUserAsync()
    {
        return await GetAllByUserIdAsync(LoggedUserId);
    }
    public async Task<List<TResponse>> GetAllByUserIdAsync(long userId)
    {
        return await ProjectFromQueryToListAsync<TResponse>(
            _repository.GetAllByUserIdAsQueryable(userId)
        );
    }
    public async Task<List<TitleValueObject>> GetAllForCurrentUserAsOptionsAsync()
    {
        return await GetAllByUserIdAsOptionsAsync(LoggedUserId);
    }
    public async Task<List<TitleValueObject>> GetAllByUserIdAsOptionsAsync(long userId)
    {
        return await ProjectFromQueryToListAsync<TitleValueObject>(
            _repository.GetAllByUserIdAsQueryable(userId)
        );
    }
    public async Task<ServiceResult> DeleteForCurrentUserAsync()
    {
        return await DeleteByUserIdAsync(LoggedUserId);
    }
    public async Task<ServiceResult> DeleteByUserIdAsync(long userId)
    {
        return await DeleteByAsync(e => e.UserId == userId);
    }

    protected long LoggedUserId => _loggedUserService.GetUserId;
}