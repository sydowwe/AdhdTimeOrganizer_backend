using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class RoutineTimePeriodMapper : IBaseCrudMapper<RoutineTimePeriod, RoutineTimePeriodRequest, RoutineTimePeriodResponse>
{
    public partial RoutineTimePeriodResponse ToResponse(RoutineTimePeriod entity);
    public partial SelectOptionResponse ToSelectOptionResponse(RoutineTimePeriod entity);
    public partial RoutineTimePeriod ToEntity(RoutineTimePeriodRequest request, long userId);

    public partial void UpdateEntity(RoutineTimePeriodRequest request, RoutineTimePeriod entity);
}
