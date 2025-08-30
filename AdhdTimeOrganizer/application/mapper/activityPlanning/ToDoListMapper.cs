using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class TodoListMapper : IBaseCrudMapper<TodoList, TodoListRequest, TodoListResponse>
{
    public partial TodoListResponse ToResponse(TodoList entity);
    public partial SelectOptionResponse ToSelectOptionResponse(TodoList entity);
    public partial TodoList ToEntity(TodoListRequest request, long userId);

    public partial void UpdateEntity(TodoListRequest request, TodoList entity);
}
