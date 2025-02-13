using AdhdTimeOrganizer.Command.application.dto.request;
using AdhdTimeOrganizer.Command.application.dto.response.activityHistory;
using AdhdTimeOrganizer.Command.application.@interface.activityHistory;
using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Common.application.dto.request.generic;
using AdhdTimeOrganizer.Common.application.dto.response.@base;
using AdhdTimeOrganizer.Common.application.dto.response.generic;
using AdhdTimeOrganizer.Web.controller.@base;
using Microsoft.AspNetCore.Mvc;

namespace AdhdTimeOrganizer.Web.controller;

public class WebExtensionDataController(IWebExtensionDataService service) : BaseWithActivityController<WebExtensionData,WebExtensionDataRequest,WebExtensionDataResponse,IWebExtensionDataService>(service)
{
    [NonAction]
    public override Task<ActionResult<WebExtensionDataResponse>> Get(long id)
    {
        return null;
    }
    [NonAction]
    public override Task<ActionResult<WebExtensionDataResponse>> Update(long id, WebExtensionDataRequest request)
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