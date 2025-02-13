using AdhdTimeOrganizer.Command.application.@interface;
using AdhdTimeOrganizer.Common.application.dto.request.@base;
using AdhdTimeOrganizer.Common.application.dto.request.generic;
using AdhdTimeOrganizer.Common.application.dto.response.@base;
using AdhdTimeOrganizer.Common.application.dto.response.generic;
using AdhdTimeOrganizer.Common.application.@interface;
using AdhdTimeOrganizer.Common.domain.model.entity;
using Microsoft.AspNetCore.Mvc;

namespace AdhdTimeOrganizer.Web.controller.@base;

public abstract class BaseCrudController<TEntity, TRequest, TResponse, TService>(TService service) : BaseReadController<TEntity, TResponse, TService>(service)
    where TEntity : BaseEntity
    where TRequest : class, IMyRequest
    where TResponse : class, IIdResponse
    where TService : IBaseCrudService<TEntity, TRequest, TResponse>
{
    private TService _service = service;


    [HttpPost]
    public virtual async Task<ActionResult<TResponse>> Create(TRequest request)
    {
        var result = await _service.InsertAsync(request);

        return result.Failed
            ? HandleFailedServiceResult(result)
            : CreatedAtAction(nameof(Get), new { id = result.Data?.Id }, result.Data);
    }

    [HttpPut("{id:long}")]
    public virtual async Task<ActionResult<TResponse>> Update(long id, TRequest request)
    {
        var result = await _service.UpdateAsync(id, request);
        return result.Failed
            ? HandleFailedServiceResult(result)
            : Ok(result);
    }

    [HttpDelete("{id:long}")]
    public virtual async Task<ActionResult<IdResponse>> Delete(long id)
    {
        var result = await _service.DeleteAsync(id);
        return result.Failed
            ? HandleFailedServiceResult(result)
            : Ok(new IdResponse(id));
    }

    [HttpPost]
    public virtual async Task<ActionResult<SuccessResponse>> BatchDelete(List<IdRequest> request)
    {
        var result = await _service.BatchDeleteAsync(request.Select(x => x.Id));
        return result.Failed
            ? HandleFailedServiceResult(result)
            : Ok(new SuccessResponse("deleted"));
    }
}