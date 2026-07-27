using AdhdTimeOrganizer.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.request.@base;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record TodoListCategoryRequest : NameTextColorIconRequest, IMyRequest<TodoListCategory>
{
    TodoListCategory ICreateRequest<TodoListCategory>.ToEntity => new() { Name = Name, Text = Text, Color = Color, Icon = Icon };

    void IUpdateRequest<TodoListCategory>.UpdateEntity(TodoListCategory e)
    {
        e.Name = Name;
        e.Text = Text;
        e.Color = Color;
        e.Icon = Icon;
    }
}