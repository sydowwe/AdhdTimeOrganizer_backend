using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;

public record TaskPriorityResponse : TextColorResponse, IProjectionResponse<TaskPriorityResponse, TaskPriority>
{
    public required int Priority { get; init; }

    public static IQueryable<TaskPriorityResponse> Projection(IQueryable<TaskPriority> query)
    {
        return query.Select(e => new TaskPriorityResponse
        {
            Id = e.Id,
            Text = e.Text,
            Color = e.Color,
            Priority = e.Priority
        });
    }
}