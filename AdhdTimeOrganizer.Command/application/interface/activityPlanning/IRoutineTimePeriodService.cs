using AdhdTimeOrganizer.Command.application.dto.request.toDoList;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.Command.application.@interface.activityPlanning;

public interface IRoutineTimePeriodService : IBaseWithUserService<RoutineTimePeriod, TimePeriodRequest, TimePeriodResponse>
{
    Task CreateDefaultItems(long newUserId);
    Task ChangeIsHiddenInViewAsync(long id);
}