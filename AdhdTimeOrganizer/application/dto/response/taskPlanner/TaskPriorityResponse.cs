using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.application.dto.response.taskPlanner;

public record TaskPriorityResponse : TextColorResponse, IProjectionResponse<TaskPriorityResponse, TaskPriority>
{
    public required int Priority { get; init; }

    public static IQueryable<TaskPriorityResponse> Projection(IQueryable<TaskPriority> query) =>
        query.Select(e => new TaskPriorityResponse
        {
            Id = e.Id,
            Text = e.Text,
            Color = e.Color,
            Priority = e.Priority
        });

    public static TaskPriorityResponse FromEntity(TaskPriority entity)
    {
        return new TaskPriorityResponse
        {
            Id = entity.Id,
            Text = entity.Text,
            Color = entity.Color,
            Priority = entity.Priority
        };
    }
}