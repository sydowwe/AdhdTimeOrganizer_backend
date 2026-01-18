
using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class RoutineTodoListMapper : IBaseSimpleCrudMapper<RoutineTodoList, UpdateRoutineTodoListRequest, RoutineTodoListResponse>
{
    public partial RoutineTodoListResponse ToResponse(RoutineTodoList entity);
    public partial SelectOptionResponse ToSelectOptionResponse(RoutineTodoList entity);
    public partial RoutineTodoList ToEntity(UpdateRoutineTodoListRequest request, long userId);

    public partial void UpdateEntity(UpdateRoutineTodoListRequest request, RoutineTodoList entity);

    public partial IQueryable<RoutineTodoListResponse> ProjectToResponse(IQueryable<RoutineTodoList> source);

}
