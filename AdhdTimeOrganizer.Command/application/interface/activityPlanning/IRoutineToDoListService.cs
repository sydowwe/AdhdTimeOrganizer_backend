using AdhdTimeOrganizer.Command.application.dto.request.toDoList;
using AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.@base;
using AdhdTimeOrganizer.Command.application.service.@base;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.Command.application.@interface.activityPlanning;

public interface IRoutineToDoListService : IEntityWithIsDoneService<RoutineToDoList, RoutineToDoListRequest, RoutineToDoListResponse>
{
    Task<IEnumerable<RoutineToDoListGroupedResponse>> GetAllGroupedByTimePeriod();
}