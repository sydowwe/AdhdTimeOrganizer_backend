using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Command.domain.repositoryContract;
using AdhdTimeOrganizer.Command.infrastructure.persistence;
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
}