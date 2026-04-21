using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class RoutineTimePeriodMapper : IBaseSimpleCrudMapper<RoutineTimePeriod, RoutineTimePeriodRequest, RoutineTimePeriodResponse>, IBaseSelectOptionMapper<RoutineTimePeriod>
{
    [MapperIgnoreTarget(nameof(RoutineTimePeriodResponse.NextResetAt))]
    [MapperIgnoreTarget(nameof(RoutineTimePeriodResponse.CompletionHistory))]
    public partial RoutineTimePeriodResponse ToResponse(RoutineTimePeriod entity);

    public SelectOptionResponse ToSelectOptionResponse(RoutineTimePeriod entity) => new(entity.Id, entity.Text);

    public partial RoutineTimePeriod ToEntity(RoutineTimePeriodRequest request, long userId);

    public partial void UpdateEntity(RoutineTimePeriodRequest request, RoutineTimePeriod entity);

    [MapperIgnoreTarget(nameof(RoutineTimePeriodResponse.NextResetAt))]
    [MapperIgnoreTarget(nameof(RoutineTimePeriodResponse.CompletionHistory))]
    public partial IQueryable<RoutineTimePeriodResponse> ProjectToResponse(IQueryable<RoutineTimePeriod> source);
}