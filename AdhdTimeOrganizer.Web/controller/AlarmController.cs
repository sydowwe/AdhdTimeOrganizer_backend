using AdhdTimeOrganizer.Command.application.dto.request;
using AdhdTimeOrganizer.Command.application.dto.response.activityHistory;
using AdhdTimeOrganizer.Command.application.@interface.activityHistory;
using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Common.application.dto.request.generic;
using AdhdTimeOrganizer.Common.application.dto.response.generic;
using AdhdTimeOrganizer.Web.controller.@base;
using Microsoft.AspNetCore.Mvc;

namespace AdhdTimeOrganizer.Web.controller;

public class AlarmController(IAlarmService service) : BaseWithActivityController<Alarm, AlarmRequest, AlarmResponse, IAlarmService>(service)
{
    [HttpPost]
    public async Task<ActionResult<SuccessResponse>> ChangeActive([FromBody] IEnumerable<IdRequest> requestList)
    {
        await service.SetIsActive(requestList);
        return Ok(new SuccessResponse("changed"));
    }
}