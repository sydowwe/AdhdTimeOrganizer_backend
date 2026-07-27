using AdhdTimeOrganizer.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.request.@base;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record TodoListRequest : NameTextIconRequest, IMyRequest<TodoList>
{
    public long? CategoryId { get; init; }

    public TodoList ToEntity => new() { Name = Name, Text = Text, Icon = Icon, CategoryId = CategoryId };

    public void UpdateEntity(TodoList e)
    {
        e.Name = Name;
        e.Text = Text;
        e.Icon = Icon;
        e.CategoryId = CategoryId;
    }
}