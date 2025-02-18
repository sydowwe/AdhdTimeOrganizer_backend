using AdhdTimeOrganizer.Command.application.dto.request.extendable;
using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.application.service.@base;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activity;
using AdhdTimeOrganizer.Common.application.dto.request.@base;
using AdhdTimeOrganizer.Common.application.dto.response.@base;
using AdhdTimeOrganizer.Common.application.service;
using AutoMapper;

namespace AdhdTimeOrganizer.Command.application.service.activity;

public class CategoryService(ICategoryRepository repository, ILoggedUserService loggedUserService, IMapper mapper)
    : BaseWithUserService<Category, NameTextColorIconRequest, NameTextColorIconResponse, ICategoryRepository>(repository, loggedUserService, mapper), ICategoryService;