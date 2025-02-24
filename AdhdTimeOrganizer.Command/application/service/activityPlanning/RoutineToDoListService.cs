using AdhdTimeOrganizer.Command.application.dto.request.toDoList;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.application.eventHandler;
using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.application.@interface.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.application.service.@base;
using AdhdTimeOrganizer.Command.domain.@event;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activityPlanning;
using AdhdTimeOrganizer.Common.domain.result;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Command.application.service.activityPlanning;

public class RoutineToDoListService(
    IMediator mediator,
    IRoutineTimePeriodService timePeriodService,
    IRoutineToDoListRepository repository,
    IActivityService activityService,
    ILoggedUserService loggedUserService,
    IMapper mapper)
    : EntityWithIsDoneService<RoutineToDoList, RoutineToDoListRequest, RoutineToDoListResponse, IRoutineToDoListRepository>(repository,
        activityService, loggedUserService, mapper), IRoutineToDoListService
{
    public new async Task<ServiceResult<RoutineToDoListResponse>> InsertAsync(RoutineToDoListRequest request)
    {
        var entity = mapper.Map<RoutineToDoList>(request);
        await mediator.Publish(new ActivityAddedToRoutineToDoListEvent(request.ActivityId));
        return await InsertAsync(entity);
    }

    public async Task<IEnumerable<RoutineToDoListGroupedResponse>> GetAllGroupedByTimePeriod()
    {
        var allTimePeriods = await timePeriodService.GetAllAsync();
        var allItems = await _repository.GetAllByUserIdAsQueryable(LoggedUserId)
            .ProjectTo<RoutineToDoListResponse>(mapper.ConfigurationProvider)
            .ToListAsync();
        var groupedItems = allItems.GroupBy(item => item.TimePeriod);
        var result = allTimePeriods.Select(tp => new RoutineToDoListGroupedResponse
        {
            TimePeriod = tp,
            Items = groupedItems.FirstOrDefault(g => g.Key.Equals(tp)) ?? Enumerable.Empty<RoutineToDoListResponse>()
        });
        return result;
    }
};