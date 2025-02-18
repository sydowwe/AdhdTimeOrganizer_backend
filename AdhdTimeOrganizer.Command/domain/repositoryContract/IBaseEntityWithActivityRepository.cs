using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;

namespace AdhdTimeOrganizer.Command.domain.repositoryContract;

public interface IBaseEntityWithActivityRepository<T> : IBaseEntityWithUserRepository<T>
    where T : BaseEntityWithActivity
{
    Task<List<T>> GetByActivityIdAsync(long userId, long activityId);
    IQueryable<T> GetByActivityIdAsQueryable(long userId,long activityId);
    IQueryable<Activity> GetDistinctActivities(long userId);
    Task<List<ActivityFormSelectOptionsResponse>> GetAllActivityFormOptionsCombinations(long userId);
}