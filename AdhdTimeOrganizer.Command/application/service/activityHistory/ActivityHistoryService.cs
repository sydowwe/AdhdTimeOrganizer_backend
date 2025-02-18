using AdhdTimeOrganizer.Command.application.dto.request.history;
using AdhdTimeOrganizer.Command.application.dto.response.activityHistory;
using AdhdTimeOrganizer.Command.application.dto.response.extendable;
using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.application.@interface.activityHistory;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.application.service.@base;
using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activityHistory;
using AdhdTimeOrganizer.Common.application.dto.response.@base;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Command.application.service.activityHistory;



public class ActivityHistoryService(IActivityHistoryRepository repository, IActivityService activityService, ILoggedUserService loggedUserService, IMapper mapper)
    : EntityWithActivityService<ActivityHistory, ActivityHistoryRequest, ActivityHistoryResponse, IActivityHistoryRepository>(repository, activityService, loggedUserService, mapper), IActivityHistoryService
{
       public async Task<List<ActivityHistoryListGroupedByDateResponse>> FilterAsync(ActivityHistoryFilterRequest filterRequest)
    {
        var query = _repository.ApplyFilters(_loggedUserService.GetUserId, filterRequest);

        var historyResponses = await query.OrderBy(h=>h.StartTimestamp)
            .ProjectTo<ActivityHistoryResponse>(mapper.ConfigurationProvider).ToListAsync();

        return historyResponses
            .GroupBy(hr => hr.StartTimestamp.ToUniversalTime().Date)
            .Select(group => new ActivityHistoryListGroupedByDateResponse
            {
                Date = group.Key,
                HistoryResponseList = group.OrderBy(h=>h.StartTimestamp).ToList()
            })
            .OrderBy(response => response.Date)
            .ToList();
    }

    // public async Task<ActivityFormSelectsResponse> UpdateFilterSelectsAsync(ActivitySelectForm request)
    // {
    //     var loggedUserId = (await _userService.GetLoggedUserAsync()).Id;
    //     var query = context.Histories.AsQueryable();
    //
    //     // Apply filters
    //     query = applyFilters(query, loggedUserId, request);
    //
    //     var activityList = await query
    //         .Select(h => h.Activity)
    //         .Distinct()
    //         .ToListAsync();
    //
    //     return await activityService.GetActivityFormSelectsFromActivityListAsync(activityList);
    // }
}