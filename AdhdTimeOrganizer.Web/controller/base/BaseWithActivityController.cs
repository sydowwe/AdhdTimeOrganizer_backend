using AdhdTimeOrganizer.Command.application.dto.request.activity;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;
using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Command.application.dto.response.extendable;
using AdhdTimeOrganizer.Command.application.@interface.@base;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using Microsoft.AspNetCore.Mvc;

namespace AdhdTimeOrganizer.Web.controller.@base;

public class BaseWithActivityController<TEntity, TRequest, TResponse, TService>(TService service) : BaseWithUserController<TEntity, TRequest, TResponse, TService>(service) where TEntity : BaseEntityWithActivity
    where TRequest : class, IActivityIdRequest
    where TResponse : class, IEntityWithActivityResponse
    where TService : IEntityWithActivityService<TEntity, TRequest, TResponse>
{
    private TService _service = service;

    [HttpPost]
    public async Task<ActionResult<IEnumerable<ActivityFormSelectOptionsResponse>>> GetAllActivityFormSelectOptions()
    {
        return Ok(await _service.GetAllActivityFormSelectOptions());
    }
}