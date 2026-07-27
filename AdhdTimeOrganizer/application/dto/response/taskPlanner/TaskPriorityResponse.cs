using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.response;

namespace AdhdTimeOrganizer.application.dto.response.taskPlanner;

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