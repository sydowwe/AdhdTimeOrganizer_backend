using AdhdTimeOrganizer.Command.application.dto.request;
using AdhdTimeOrganizer.Command.application.dto.response.activityHistory;
using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.application.@interface.activityHistory;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.application.service.@base;
using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activityHistory;
using AutoMapper;

namespace AdhdTimeOrganizer.Command.application.service.activityHistory;

public class WebExtensionDataService(IWebExtensionDataRepository repository, IActivityService activityService, ILoggedUserService loggedUserService, IMapper mapper)
    : EntityWithActivityService<WebExtensionData, WebExtensionDataRequest, WebExtensionDataResponse, IWebExtensionDataRepository>(repository, activityService, loggedUserService, mapper), IWebExtensionDataService;