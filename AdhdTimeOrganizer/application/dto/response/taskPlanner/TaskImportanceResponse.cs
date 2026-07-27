using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.dto.response;

namespace AdhdTimeOrganizer.application.dto.response.taskPlanner;

public record TaskImportanceResponse : TextColorIconResponse, IProjectionResponse<TaskImportanceResponse, TaskImportance>
{
    public required int Importance { get; init; }

    public static IQueryable<TaskImportanceResponse> Projection(IQueryable<TaskImportance> query)
    {
        return query.Select(entity => new TaskImportanceResponse
        {
            Id = entity.Id,
            Text = entity.Text,
            Color = entity.Color,
            Icon = entity.Icon,
            Importance = entity.Importance
        });
    }
}