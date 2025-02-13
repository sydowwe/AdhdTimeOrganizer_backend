using AdhdTimeOrganizer.Command.domain.model.entity.user;
using AdhdTimeOrganizer.Common.application.dto.request.@base;
using AdhdTimeOrganizer.Common.application.dto.response.@base;
using AdhdTimeOrganizer.Common.application.@interface;
using AdhdTimeOrganizer.Common.domain.model.valueObject;
using AdhdTimeOrganizer.Common.domain.result;

namespace AdhdTimeOrganizer.Command.application.@interface.users;

public interface IBaseWithUserService<TEntity, in TRequest, TResponse> : IBaseCrudService<TEntity, TRequest, TResponse>
    where TEntity : BaseEntityWithUser
    where TRequest : IMyRequest
    where TResponse : IMyResponse
{
    Task<ServiceResult<TEntity>> GetSingleForCurrentUser();
    Task<ServiceResult<TEntity>> GetSingleByUserId(long userId);
    Task<List<TResponse>> GetAllForCurrentUserAsync();
    Task<List<TResponse>> GetAllByUserIdAsync(long userId);
    Task<List<TitleValueObject>> GetAllForCurrentUserAsOptionsAsync();
    Task<List<TitleValueObject>> GetAllByUserIdAsOptionsAsync(long userId);
    Task<ServiceResult> DeleteForCurrentUserAsync();
    Task<ServiceResult> DeleteByUserIdAsync(long userId);

}