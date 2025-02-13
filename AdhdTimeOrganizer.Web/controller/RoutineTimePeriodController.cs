using AdhdTimeOrganizer.Command.application.dto.request.toDoList;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.activityPlanning;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Common.application.dto.response.generic;
using AdhdTimeOrganizer.Web.controller.@base;
using Microsoft.AspNetCore.Mvc;

namespace AdhdTimeOrganizer.Web.controller;

public class RoutineTimePeriodController(IRoutineTimePeriodService service) : BaseWithUserController<RoutineTimePeriod,TimePeriodRequest,TimePeriodResponse,IRoutineTimePeriodService>(service)
{
    private readonly IRoutineTimePeriodService _service = service;

    [HttpPost]
    public async Task<ActionResult<IEnumerable<TimePeriodResponse>>> CreateDefaults()
    {
        await _service.CreateDefaultItems(0);
        return Ok("ok");
    }
    [HttpPost("change-is-hidden/{id:long}")]
    public async Task<ActionResult<SuccessResponse>> ChangeIsHidden(long id)
    {
        await _service.ChangeIsHiddenInViewAsync(id);
        return Ok(new SuccessResponse("hidden changed"));
    }
}