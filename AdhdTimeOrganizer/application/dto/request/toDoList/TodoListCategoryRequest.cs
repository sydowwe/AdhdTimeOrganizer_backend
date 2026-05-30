using AdhdTimeOrganizer.application.dto.request.@base;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record TodoListCategoryRequest : NameTextColorIconRequest, IMyRequest<TodoListCategory>
{
    TodoListCategory ICreateRequest<TodoListCategory>.ToEntity => new() { UserId = 0, Name = Name, Text = Text, Color = Color, Icon = Icon };
    void IUpdateRequest<TodoListCategory>.UpdateEntity(TodoListCategory e) { e.Name = Name; e.Text = Text; e.Color = Color; e.Icon = Icon; }
}
