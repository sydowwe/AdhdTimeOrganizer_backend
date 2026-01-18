using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.application.mapper.@interface;

public interface IBaseCreateMapper<TEntity, in TCreateRequest> : IMapperService
    where TEntity : class, IEntityWithUser
    where TCreateRequest : class, ICreateRequest
{
    TEntity ToEntity(TCreateRequest request, long userId);
}