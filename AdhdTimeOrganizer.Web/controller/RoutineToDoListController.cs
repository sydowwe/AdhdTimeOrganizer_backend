using AdhdTimeOrganizer.Command.application.dto.request.toDoList;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.activityPlanning;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Common.application.dto.request.generic;
using AdhdTimeOrganizer.Common.application.dto.response.generic;
using AdhdTimeOrganizer.Web.controller.@base;
using Microsoft.AspNetCore.Mvc;

namespace AdhdTimeOrganizer.Web.controller;

public class RoutineToDoListController(IRoutineToDoListService service)
    : BaseWithActivityController<RoutineToDoList, RoutineToDoListRequest, RoutineToDoListResponse, IRoutineToDoListService>(service)
{
    private readonly IRoutineToDoListService _service = service;
    [HttpPost]
    public async Task<ActionResult<SuccessResponse>> ChangeIsDone(List<IdRequest> requestList)
    {
        await _service.SetIsDoneAsync(requestList);
        return Ok(new SuccessResponse("changed"));
    }
    [NonAction]
    public override Task<ActionResult<IEnumerable<RoutineToDoListResponse>>> GetAll()
    {
        return null;
    }
    [HttpPost]
    public async Task<ActionResult<IEnumerable<RoutineToDoListGroupedResponse>>> GetAllGrouped()
    {
        return Ok(await _service.GetAllGroupedByTimePeriod());
    }
}