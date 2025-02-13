using AdhdTimeOrganizer.Command.application.dto.request.toDoList;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.Command.application.@interface.activityPlanning;

public interface ITaskUrgencyService : IBaseWithUserService<TaskUrgency, TaskUrgencyRequest, TaskUrgencyResponse>
{
    Task CreateDefaultItems(long newUserId);
}