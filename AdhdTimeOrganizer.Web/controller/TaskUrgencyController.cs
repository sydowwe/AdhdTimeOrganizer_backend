using AdhdTimeOrganizer.Command.application.dto.request.toDoList;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.activityPlanning;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Common.application.dto.request.generic;
using AdhdTimeOrganizer.Common.application.dto.response.@base;
using AdhdTimeOrganizer.Common.application.dto.response.generic;
using AdhdTimeOrganizer.Web.controller.@base;
using Microsoft.AspNetCore.Mvc;

namespace AdhdTimeOrganizer.Web.controller;

public class TaskUrgencyController(ITaskUrgencyService service) : BaseWithUserController<TaskUrgency,TaskUrgencyRequest,TaskUrgencyResponse,ITaskUrgencyService>(service)
{
    [NonAction]
    public override Task<ActionResult<TaskUrgencyResponse>> Create(TaskUrgencyRequest request)
    {
        return null;
    }
    [NonAction]
    public override Task<ActionResult<TaskUrgencyResponse>> Update(long id, TaskUrgencyRequest request)
    {
        return null;
    }
    [NonAction]
    public override  Task<ActionResult<IdResponse>> Delete(long id)
    {
        return null;
    }
    [NonAction]
    public override Task<ActionResult<SuccessResponse>> BatchDelete(List<IdRequest> request)
    {
        return null;
    }
}