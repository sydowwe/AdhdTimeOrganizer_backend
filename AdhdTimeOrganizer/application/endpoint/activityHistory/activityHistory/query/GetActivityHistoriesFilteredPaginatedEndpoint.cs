using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.query;

public class GetActivityHistoriesFilteredPaginatedEndpoint(
    AppCommandDbContext dbContext,
    ActivityHistoryMapper mapper)
    : BaseFilteredPaginatedEndpoint<ActivityHistory, ActivityHistoryResponse, ActivityHistoryFilterRequest, ActivityHistoryMapper>(dbContext, mapper)
{
    private readonly ActivityHistoryMapper _mapper = mapper;

    protected override IQueryable<ActivityHistory> WithIncludes(IQueryable<ActivityHistory> query)
    {
        return query
            .Include(ah => ah.Activity)
            .ThenInclude(a => a.Role)
            .Include(ah => ah.Activity)
            .ThenInclude(a => a.Category);
    }

    protected override IQueryable<ActivityHistory> ApplyCustomFiltering(IQueryable<ActivityHistory> query, ActivityHistoryFilterRequest filter)
    {
        if (filter.ActivityId.HasValue)
        {
            query = query.Where(ah => ah.ActivityId == filter.ActivityId.Value);
        }

        if (filter.RoleId.HasValue)
            query = query.Where(h => h.Activity.CategoryId == filter.RoleId);

        if (filter.CategoryId.HasValue)
            query = query.Where(h => h.Activity.RoleId == filter.CategoryId);


        if (filter.IsFromTodoList.HasValue)
        {
            query = query.Include(ah => ah.Activity).ThenInclude(a => a.TodoList);

            query = query.Where(ah=> ah.Activity.TodoList != null == filter.IsFromTodoList.Value );
            if (filter.TaskUrgencyId.HasValue && filter.IsFromTodoList.Value)
            {
                query = query.Where(ah => ah.Activity.TodoList!.TaskUrgencyId == filter.TaskUrgencyId.Value);
            }
        }

        if (filter.IsFromRoutineTodoList.HasValue)
        {
            query = query.Include(ah => ah.Activity).ThenInclude(a => a.RoutineTodoLists);

            query = query.Where(h => h.Activity.RoutineTodoLists.Any() == filter.IsFromRoutineTodoList.Value);
            if (filter.RoutineTimePeriodId.HasValue && filter.IsFromRoutineTodoList.Value)
            {
                query = query.Where(h => h.Activity.RoutineTodoLists.Any(rtd => rtd.TimePeriodId == filter.RoutineTimePeriodId.Value));
            }
        }

        if (filter.IsUnavoidable.HasValue)
        {
            query = query.Where(h => h.Activity.IsUnavoidable == filter.IsUnavoidable);
        }

        if (filter.DateFrom.HasValue)
        {
            query = query.Where(h => h.StartTimestamp >= filter.DateFrom);
        }

        if (filter.DateTo.HasValue)
        {
            query = query.Where(h => h.StartTimestamp <= filter.DateTo);
        }

        if (filter.HoursBack.HasValue)
        {
            var cutoffTime = DateTime.UtcNow.AddHours(-filter.HoursBack.Value);
            query = query.Where(h => h.StartTimestamp >= cutoffTime);
        }

        if (filter.MinLength != null)
        {
            query = query.Where(ah => ah.Length >= filter.MinLength);
        }

        if (filter.MaxLength != null)
        {
            query = query.Where(ah => ah.Length <= filter.MaxLength);
        }

        return query;
    }
}