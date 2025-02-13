using AdhdTimeOrganizer.Command.application.dto.request.extendable;
using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Common.application.dto.request.generic;
using AdhdTimeOrganizer.Common.application.dto.response.@base;
using AdhdTimeOrganizer.Common.application.dto.response.generic;
using AdhdTimeOrganizer.Web.controller.@base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdhdTimeOrganizer.Web.controller;

[Authorize]
public class RoleController(IRoleService service) : BaseWithUserController<Role,NameTextColorIconRequest,NameTextColorIconResponse,IRoleService>(service)
{
    private readonly IRoleService _service = service;

    [HttpPost("get-by-name/{name}")]
    public async Task<ActionResult<NameTextColorIconResponse>> GetByName([FromRoute] string name)
    {
        return Ok(await _service.GetByNameAsync(name));
    }

    [NonAction]
    public override Task<ActionResult<NameTextColorIconResponse>> Update(long id, NameTextColorIconRequest request)
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