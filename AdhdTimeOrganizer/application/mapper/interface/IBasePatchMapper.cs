using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.application.mapper.@interface;

public interface IBasePatchMapper<in TEntity, in TPatchRequest> : IMapperService
    where TEntity : class, IEntityWithId
    where TPatchRequest : class, IPatchRequest
{
    void PatchEntity(TPatchRequest request, TEntity entity);
}
