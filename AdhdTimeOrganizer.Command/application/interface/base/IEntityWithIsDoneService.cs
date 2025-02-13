using AdhdTimeOrganizer.Command.application.dto.request.extendable;
using AdhdTimeOrganizer.Command.application.dto.response.extendable;
using AdhdTimeOrganizer.Command.application.service.@base;
using AdhdTimeOrganizer.Command.domain.model.entity.@base;
using AdhdTimeOrganizer.Common.application.dto.request.generic;

namespace AdhdTimeOrganizer.Command.application.@interface.@base;

public interface IEntityWithIsDoneService<TEntity, in TRequest, TResponse>
    : IEntityWithActivityService<TEntity, TRequest, TResponse>
    where TEntity : BaseEntityWithIsDone
    where TRequest : WithIsDoneRequest
    where TResponse : WithIsDoneResponse
{
    Task SetIsDoneAsync(IEnumerable<IdRequest> requestList);
}