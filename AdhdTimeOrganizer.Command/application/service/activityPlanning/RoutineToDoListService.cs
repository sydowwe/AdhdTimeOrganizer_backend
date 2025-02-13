using AdhdTimeOrganizer.Command.application.dto.request.toDoList;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.application.@interface.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.application.service.@base;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activityPlanning;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Command.application.service.activityPlanning;



public class RoutineToDoListService(
    IRoutineTimePeriodService timePeriodService,
    IRoutineToDoListRepository repository,
    IActivityService activityService,
    ILoggedUserService loggedUserService,
    IMapper mapper)
    : EntityWithIsDoneService<RoutineToDoList, RoutineToDoListRequest, RoutineToDoListResponse, IRoutineToDoListRepository>(repository,
        activityService, loggedUserService, mapper), IRoutineToDoListService
{
    public async Task<IEnumerable<RoutineToDoListGroupedResponse>> GetAllGroupedByTimePeriod()
    {
        var allTimePeriods = await timePeriodService.GetAllAsync();
        var allItems = await repository.GetAllByUserIdAsQueryable(LoggedUserId)
            .ProjectTo<RoutineToDoListResponse>(mapper.ConfigurationProvider)
            .ToListAsync();
        var groupedItems = allItems.GroupBy(item => item.timePeriod);
        var result = allTimePeriods.Select(tp => new RoutineToDoListGroupedResponse(
            tp, groupedItems.FirstOrDefault(g => g.Key.Equals(tp)) ?? Enumerable.Empty<RoutineToDoListResponse>()
        ));
        return result;
    }
};