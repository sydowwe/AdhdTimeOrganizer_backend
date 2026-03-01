using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.activityPlanning;

[Mapper]
public partial class TodoListCategoryMapper : IBaseSimpleCrudMapper<TodoListCategory, TodoListCategoryRequest, TodoListCategoryResponse>
{
    public partial TodoListCategoryResponse ToResponse(TodoListCategory entity);
    public partial TodoListCategory ToEntity(TodoListCategoryRequest request, long userId);
    public partial void UpdateEntity(TodoListCategoryRequest request, TodoListCategory entity);
    public partial IQueryable<TodoListCategoryResponse> ProjectToResponse(IQueryable<TodoListCategory> source);
    public SelectOptionResponse ToSelectOptionResponse(TodoListCategory entity) => new(entity.Id, entity.Name);
}
