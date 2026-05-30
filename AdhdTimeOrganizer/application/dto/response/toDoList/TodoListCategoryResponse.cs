using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.domain.model.entity.todoList;


namespace AdhdTimeOrganizer.application.dto.response.todoList;

public record TodoListCategoryResponse : NameTextColorIconResponse,
    IProjectionResponse<TodoListCategoryResponse, TodoListCategory>
{
    public static IQueryable<TodoListCategoryResponse> Projection(IQueryable<TodoListCategory> q) =>
        q.Select(e => new TodoListCategoryResponse
        {
            Id = e.Id,
            Name = e.Name,
            Text = e.Text,
            Color = e.Color,
            Icon = e.Icon,
        });

    public static TodoListCategoryResponse FromEntity(TodoListCategory e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Text = e.Text,
        Color = e.Color,
        Icon = e.Icon,
    };

    public static IQueryable<SelectOptionResponse> SelectOptionProjection(IQueryable<TodoListCategory> q) =>
        q.Select(e => new SelectOptionResponse { Id = e.Id, Text = e.Name });
}
