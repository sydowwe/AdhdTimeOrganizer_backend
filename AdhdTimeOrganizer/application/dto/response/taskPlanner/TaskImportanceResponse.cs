using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.application.dto.response.taskPlanner;

public record TaskImportanceResponse : TextColorIconResponse, IProjectionResponse<TaskImportanceResponse, TaskImportance>
{
    public required int Importance { get; init; }

    public static IQueryable<TaskImportanceResponse> Projection(IQueryable<TaskImportance> query) =>
        query.Select(e => FromEntity(e));

    public static TaskImportanceResponse FromEntity(TaskImportance entity) => new()
    {
        Id = entity.Id,
        Text = entity.Text,
        Color = entity.Color,
        Icon = entity.Icon,
        Importance = entity.Importance
    };
}