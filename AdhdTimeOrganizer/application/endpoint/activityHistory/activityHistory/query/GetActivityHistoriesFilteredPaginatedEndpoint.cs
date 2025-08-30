using AdhdTimeOrganizer.application.dto.request.filter;
using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory;

public class GetActivityHistoriesFilteredPaginatedEndpoint(
    AppCommandDbContext dbContext,
    ActivityHistoryMapper mapper)
    : BaseFilteredPaginatedEndpoint<ActivityHistory, ActivityHistoryResponse, ActivityHistoryFilterRequest>(dbContext)
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


        if (filter.IsFromToDoList.HasValue)
        {
            query = query.Where(h => h.Activity.IsOnToDoList == filter.IsFromToDoList.Value);

            if (filter.IsFromToDoList.Value && filter.TaskUrgencyId.HasValue)
            {
                query = query.Where(h => h.Activity.ToDoList != null && h.Activity.ToDoList.TaskUrgencyId == filter.TaskUrgencyId.Value);
            }
        }

        if (filter.IsFromRoutineToDoList.HasValue)
        {
            query = query.Where(h => h.Activity.IsOnRoutineToDoList == filter.IsFromRoutineToDoList.Value);

            if (filter.IsFromRoutineToDoList.Value && filter.RoutineTimePeriodId.HasValue)
            {
                query = query.Where(h => h.Activity.RoutineToDoList != null && h.Activity.RoutineToDoList.TimePeriodId == filter.RoutineTimePeriodId.Value);
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

    protected override ActivityHistoryResponse MapToResponse(ActivityHistory entity)
    {
        return _mapper.ToResponse(entity);
    }
}