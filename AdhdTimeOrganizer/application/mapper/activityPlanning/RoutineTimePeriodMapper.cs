using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class RoutineTimePeriodMapper : IBaseSimpleCrudMapper<RoutineTimePeriod, RoutineTimePeriodRequest, RoutineTimePeriodResponse>
{
    public partial RoutineTimePeriodResponse ToResponse(RoutineTimePeriod entity);
    public partial SelectOptionResponse ToSelectOptionResponse(RoutineTimePeriod entity);
    public partial RoutineTimePeriod ToEntity(RoutineTimePeriodRequest request, long userId);

    public partial void UpdateEntity(RoutineTimePeriodRequest request, RoutineTimePeriod entity);

    public partial IQueryable<RoutineTimePeriodResponse> ProjectToResponse(IQueryable<RoutineTimePeriod> source);

}
