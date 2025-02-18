using AdhdTimeOrganizer.Command.application.dto.request.history;
using AdhdTimeOrganizer.Command.application.dto.response.activityHistory;
using AdhdTimeOrganizer.Command.application.@interface.activityHistory;
using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Web.controller.@base;
using Microsoft.AspNetCore.Mvc;

namespace AdhdTimeOrganizer.Web.controller;

public class ActivityHistoryController(IActivityHistoryService service) : BaseWithActivityController<ActivityHistory, ActivityHistoryRequest, ActivityHistoryResponse, IActivityHistoryService>(service)
{
    private readonly IActivityHistoryService _service = service;

    [HttpPost]
    public async Task<IActionResult> Filter([FromBody] ActivityHistoryFilterRequest filterRequest)
    {
        var response = await _service.FilterAsync(filterRequest);
        return Ok(response);
    }

}