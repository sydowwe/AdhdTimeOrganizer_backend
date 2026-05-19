using AdhdTimeOrganizer.application.dto.request.todoList;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.todoList;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.todoList;

[Mapper]
public partial class TodoListMapper : IBaseSimpleCrudMapper<TodoList, TodoListRequest, TodoListResponse>, IBaseSelectOptionMapper<TodoList>
{
    public partial TodoListResponse ToResponse(TodoList entity);
    public SelectOptionResponse ToSelectOptionResponse(TodoList entity) => new(entity.Id, entity.Name);
    public partial TodoList ToEntity(TodoListRequest request, long userId);
    public partial void UpdateEntity(TodoListRequest request, TodoList entity);
    public partial IQueryable<TodoListResponse> ProjectToResponse(IQueryable<TodoList> source);
}