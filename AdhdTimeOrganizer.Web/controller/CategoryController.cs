using AdhdTimeOrganizer.Command.application.dto.request.extendable;
using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Common.application.dto.response.@base;
using AdhdTimeOrganizer.Web.controller.@base;
using Microsoft.AspNetCore.Mvc;

namespace AdhdTimeOrganizer.Web.controller;

public class CategoryController(ICategoryService service) : BaseWithUserController<Category,NameTextColorIconRequest,NameTextColorIconResponse,ICategoryService>(service)
{
}