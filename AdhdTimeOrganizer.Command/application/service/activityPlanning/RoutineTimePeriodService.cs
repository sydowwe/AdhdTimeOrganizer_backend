using AdhdTimeOrganizer.Command.application.dto.request.toDoList;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.application.service.@base;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activityPlanning;
using AutoMapper;

namespace AdhdTimeOrganizer.Command.application.service.activityPlanning;

public class RoutineTimePeriodService(IRoutineTimePeriodRepository repository, ILoggedUserService loggedUserService, IMapper mapper)
    : BaseCrudServiceWithUser<RoutineTimePeriod, TimePeriodRequest, TimePeriodResponse,IRoutineTimePeriodRepository>(repository, loggedUserService, mapper), IRoutineTimePeriodService
{
    public async Task CreateDefaultItems(long newUserId)
    {
        await _repository.AddRangeAsync(
            [
                new RoutineTimePeriod{UserId = newUserId,Text = "Daily", Color = "#92F58C",LengthInDays = 1},         // Green
                new RoutineTimePeriod{UserId = newUserId,Text = "Weekly", Color = "#936AF1",LengthInDays = 7},      // purple
                new RoutineTimePeriod{UserId = newUserId,Text = "Monthly", Color = "#2C7EF4",LengthInDays = 30},     // blue
                new RoutineTimePeriod{UserId = newUserId,Text = "Yearly", Color = "#A5CCF3",LengthInDays = 365}
            ]
        );
    }

    public async Task ChangeIsHiddenInViewAsync(long id)
    {
        await _repository.ChangeIsHiddenInViewAsync(id);
    }
}