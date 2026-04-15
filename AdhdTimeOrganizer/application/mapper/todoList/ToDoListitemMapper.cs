using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.todoList;

[Mapper]
public partial class TodoListItemMapper : IBaseCrudMapper<TodoListItem, CreateTodoListItemRequest, UpdateTodoListItemRequest, TodoListItemResponse>
{
    public partial TodoListItemResponse ToResponse(TodoListItem entity);
    public partial SelectOptionResponse ToSelectOptionResponse(TodoListItem entity);

    [MapperIgnoreTarget(nameof(TodoListItem.Steps))]
    public partial TodoListItem ToEntity(CreateTodoListItemRequest request, long userId);

    [MapperIgnoreTarget(nameof(TodoListItem.Steps))]
    public partial void UpdateEntity(UpdateTodoListItemRequest request, TodoListItem entity);

    public partial IQueryable<TodoListItemResponse> ProjectToResponse(IQueryable<TodoListItem> source);
}
