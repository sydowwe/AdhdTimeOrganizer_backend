using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.todoList;

[Mapper]
public partial class RoutineTodoListMapper : IBaseCrudMapper<RoutineTodoList, CreateRoutineTodoListRequest, UpdateRoutineTodoListRequest, RoutineTodoListResponse>
{
    public partial RoutineTodoListResponse ToResponse(RoutineTodoList entity);
    public partial SelectOptionResponse ToSelectOptionResponse(RoutineTodoList entity);

    [MapperIgnoreTarget(nameof(RoutineTodoList.Steps))]
    public partial RoutineTodoList ToEntity(CreateRoutineTodoListRequest request, long userId);

    [MapperIgnoreTarget(nameof(RoutineTodoList.Steps))]
    public partial void UpdateEntity(UpdateRoutineTodoListRequest request, RoutineTodoList entity);

    public partial IQueryable<RoutineTodoListResponse> ProjectToResponse(IQueryable<RoutineTodoList> source);

}
