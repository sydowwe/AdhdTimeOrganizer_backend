using AdhdTimeOrganizer.Common.application.dto.response.@base;
using AdhdTimeOrganizer.Common.application.dto.response.generic;
using AdhdTimeOrganizer.Common.application.@interface;
using AdhdTimeOrganizer.Common.domain.model.entity;
using AdhdTimeOrganizer.Common.domain.model.valueObject;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdhdTimeOrganizer.Web.controller.@base;

public abstract class BaseReadController<TEntity,  TResponse, TService>(TService service) : BaseController<TEntity, TService>(service)
    where TEntity : BaseEntity
    where TResponse : class, IMyResponse
    where TService : IBaseReadService<TEntity, TResponse>
{
    private TService _service = service;

    [HttpGet("{id:long}")]
    public virtual async Task<ActionResult<TResponse>> Get(long id)
    {
        var result = await _service.GetByIdAsync(id);
        return result.Failed
            ? HandleFailedServiceResult(result)
            : Ok(result);
    }
    [HttpPost]
    public virtual async Task<ActionResult<IEnumerable<TResponse>>> GetAll()
    {
        var response = await _service.GetAllAsync();
        return Ok(response);
    }

    [HttpPost]
    public virtual async Task<ActionResult<IEnumerable<SelectOptionResponse>>> GetAllOptions()
    {
        var response = await _service.GetAllAsOptionsAsync();
        return Ok(response);
    }
}