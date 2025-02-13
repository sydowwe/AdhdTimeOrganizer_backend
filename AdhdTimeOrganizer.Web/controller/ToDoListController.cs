using AdhdTimeOrganizer.Command.application.dto.request.toDoList;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.activityPlanning;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Common.application.dto.request.generic;
using AdhdTimeOrganizer.Common.application.dto.response.generic;
using AdhdTimeOrganizer.Web.controller.@base;
using Microsoft.AspNetCore.Mvc;

namespace AdhdTimeOrganizer.Web.controller;

public class ToDoListController(IToDoListService service) : BaseWithActivityController<ToDoList,ToDoListRequest,ToDoListResponse,IToDoListService>(service)
{
    [HttpPost]
    public async Task<ActionResult<SuccessResponse>> ChangeIsDone(List<IdRequest> requestList)
    {
        await service.SetIsDoneAsync(requestList);
        return Ok(new SuccessResponse("changed"));
    }
}