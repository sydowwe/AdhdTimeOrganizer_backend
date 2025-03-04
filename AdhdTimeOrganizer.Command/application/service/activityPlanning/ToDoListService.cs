using AdhdTimeOrganizer.Command.application.dto.request.toDoList;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.application.@interface.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.application.service.@base;
using AdhdTimeOrganizer.Command.domain.@event;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activityPlanning;
using AdhdTimeOrganizer.Common.domain.result;
using AutoMapper;
using MediatR;

namespace AdhdTimeOrganizer.Command.application.service.activityPlanning;

public class ToDoListService(
    IMediator mediator,
    IToDoListRepository repository,
    IActivityService activityService,
    ILoggedUserService loggedUserService,
    IMapper mapper)
    : EntityWithIsDoneService<ToDoList, ToDoListRequest, ToDoListResponse, IToDoListRepository>(repository, activityService, loggedUserService, mapper), IToDoListService
{
    public new async Task<ServiceResult<ToDoListResponse>> InsertAsync(ToDoListRequest request)
    {
        var entity = mapper.Map<ToDoList>(request);
        await mediator.Publish(new ActivityAddedToToDoListEvent(request.ActivityId));
        return await InsertAsync(entity);
    }
}