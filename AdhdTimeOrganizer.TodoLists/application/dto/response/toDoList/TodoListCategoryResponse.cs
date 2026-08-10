using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.application.dto.response.generic;

namespace AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;

public record TodoListCategoryResponse : NameTextColorIconResponse,
    IProjectionResponse<TodoListCategoryResponse, TodoListCategory>
{
    public static IQueryable<TodoListCategoryResponse> Projection(IQueryable<TodoListCategory> q)
    {
        return q.Select(e => new TodoListCategoryResponse
        {
            Id = e.Id,
            Name = e.Name,
            Text = e.Text,
            Color = e.Color,
            Icon = e.Icon
        });
    }

    public static IQueryable<SelectOptionResponse> SelectOptionProjection(IQueryable<TodoListCategory> q)
    {
        return q.Select(e => new SelectOptionResponse { Id = e.Id, Text = e.Name });
    }
}