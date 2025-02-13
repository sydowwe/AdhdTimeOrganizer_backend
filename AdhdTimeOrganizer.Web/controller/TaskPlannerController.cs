using AdhdTimeOrganizer.Command.application.dto.request.plannerTask;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.activityPlanning;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Common.application.dto.request.generic;
using AdhdTimeOrganizer.Common.application.dto.response.generic;
using AdhdTimeOrganizer.Web.controller.@base;
using Microsoft.AspNetCore.Mvc;

namespace AdhdTimeOrganizer.Web.controller;

public class TaskPlannerController(IPlannerTaskService service)
    : BaseWithActivityController<PlannerTask, PlannerTaskRequest, PlannerTaskResponse, IPlannerTaskService>(service)
{
    private readonly IPlannerTaskService _service = service;

    [HttpPost]
    public async Task<ActionResult<List<PlannerTaskResponse>>> ApplyFilter(PlannerFilterRequest request)
    {
        var result =  await _service.GetAllByDateAndHourSpan(request);
        return Ok(result);
    }
    [HttpPost]
    public async Task<ActionResult<SuccessResponse>> ChangeIsDone(List<IdRequest> requestList)
    {
        await _service.SetIsDoneAsync(requestList);
        return Ok(new SuccessResponse("changed"));
    }
}