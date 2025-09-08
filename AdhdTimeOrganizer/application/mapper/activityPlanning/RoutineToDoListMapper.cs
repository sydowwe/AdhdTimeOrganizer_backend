
using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class RoutineTodoListMapper : IBaseCrudMapper<RoutineTodoList, RoutineTodoListRequest, RoutineTodoListResponse>
{
    public partial RoutineTodoListResponse ToResponse(RoutineTodoList entity);
    public partial SelectOptionResponse ToSelectOptionResponse(RoutineTodoList entity);
    public partial RoutineTodoList ToEntity(RoutineTodoListRequest request, long userId);

    public partial void UpdateEntity(RoutineTodoListRequest request, RoutineTodoList entity);

    public partial IQueryable<RoutineTodoListResponse> ProjectToResponse(IQueryable<RoutineTodoList> source);

}
