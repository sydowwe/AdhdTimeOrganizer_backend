using AdhdTimeOrganizer.application.dto.request.@base;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record TodoListRequest : NameTextIconRequest, IMyRequest<TodoList>
{
    public long? CategoryId { get; init; }

    public TodoList ToEntity => new() { UserId = 0, Name = Name, Text = Text, Icon = Icon, CategoryId = CategoryId };

    public void UpdateEntity(TodoList e)
    {
        e.Name = Name;
        e.Text = Text;
        e.Icon = Icon;
        e.CategoryId = CategoryId;
    }
}
