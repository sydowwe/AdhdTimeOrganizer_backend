using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.application.dto.response.todoList;

public record TodoListResponse : NameTextIconResponse,
    IProjectionResponse<TodoListResponse, TodoList>
{
    public TodoListCategoryResponse? Category { get; init; }
    public int ItemCount { get; init; }
    public int CompletedCount { get; init; }

    public static IQueryable<TodoListResponse> Projection(IQueryable<TodoList> q) =>
        q.Select(e => new TodoListResponse
        {
            Id = e.Id,
            Name = e.Name,
            Text = e.Text,
            Icon = e.Icon,
            ItemCount = e.TodoListItemColl.Count(),
            CompletedCount = e.TodoListItemColl.Count(i => i.IsDone),
            Category = e.Category == null ? null : new TodoListCategoryResponse
            {
                Id = e.Category.Id,
                Name = e.Category.Name,
                Text = e.Category.Text,
                Color = e.Category.Color,
                Icon = e.Category.Icon,
            },
        });

    public static TodoListResponse FromEntity(TodoList e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Text = e.Text,
        Icon = e.Icon,
        ItemCount = e.ItemCount,
        CompletedCount = e.CompletedCount,
        Category = e.Category == null ? null : TodoListCategoryResponse.FromEntity(e.Category),
    };

    public static IQueryable<SelectOptionResponse> SelectOptionProjection(IQueryable<TodoList> q) =>
        q.Select(e => new SelectOptionResponse { Id = e.Id, Text = e.Name });
}
