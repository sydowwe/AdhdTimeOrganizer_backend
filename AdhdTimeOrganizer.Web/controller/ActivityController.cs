using AdhdTimeOrganizer.Command.application.dto.request.activity;
using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Common.application.dto.response.generic;
using AdhdTimeOrganizer.Common.domain.model.valueObject;
using AdhdTimeOrganizer.Web.controller.@base;
using Microsoft.AspNetCore.Mvc;

namespace AdhdTimeOrganizer.Web.controller;

public class ActivityController(IActivityService service)
    : BaseWithUserController<Activity, ActivityRequest, ActivityResponse, IActivityService>(service)
{
    private readonly IActivityService _service = service;

    [HttpPost]
    public async Task<ActionResult<IEnumerable<ActivityFormSelectOptionsResponse>>> GetAllFormSelectsOptions()
    {
        return Ok(await _service.GetAllFormSelectOptions());
    }
}