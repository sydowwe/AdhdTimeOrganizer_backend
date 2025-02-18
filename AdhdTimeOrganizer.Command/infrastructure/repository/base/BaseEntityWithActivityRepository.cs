using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Command.domain.repositoryContract;
using AdhdTimeOrganizer.Command.infrastructure.persistence;
using AdhdTimeOrganizer.Common.application.dto.response.generic;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Command.infrastructure.repository.@base;

public class BaseEntityWithActivityRepository<T>(AppCommandDbContext context)
    : BaseEntityWithUserRepository<T>(context), IBaseEntityWithActivityRepository<T>
    where T : BaseEntityWithActivity
{
    public async Task<List<T>> GetByActivityIdAsync(long userId, long activityId)
    {
        return await dbSet.Where(e => e.ActivityId == activityId && e.UserId == userId).ToListAsync();
    }

    public IQueryable<T> GetByActivityIdAsQueryable(long userId, long activityId)
    {
        return dbSet.Where(e => e.ActivityId == activityId && e.UserId == userId);
    }
    public IQueryable<Activity> GetDistinctActivities(long userId)
    {
        return dbSet.Where(e => e.UserId == userId).Select(e=>e.Activity).Distinct();
    }

    public Task<List<ActivityFormSelectOptionsResponse>> GetAllActivityFormOptionsCombinations(long userId)
    {
        return dbSet.Where(e => e.UserId == userId).Select(e => e.Activity).Distinct()
            .Select(activity => new ActivityFormSelectOptionsResponse
            {
                Id = activity.Id,
                Text = activity.Name,
                RoleOption = new SelectOptionResponse { Id = activity.Role.Id, Text = activity.Role.Name },
                CategoryOption = activity.Category != null ? new SelectOptionResponse { Id = activity.Category.Id, Text = activity.Category.Name } : null,
                TaskUrgencyOption = activity.ToDoList != null ? new SelectOptionResponse { Id = activity.ToDoList.TaskUrgencyId, Text = activity.ToDoList.TaskUrgency.Text } : null,
                RoutineTimePeriodOption = activity.RoutineToDoList != null ? new SelectOptionResponse { Id = activity.RoutineToDoList.TimePeriodId, Text = activity.RoutineToDoList.TimePeriod.Text } : null
            }).ToListAsync();
    }
}