using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.dto.request.@base;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

public record TaskImportanceRequest : TextColorRequest, IMyRequest<TaskImportance>
{
    public short Importance { get; init; }

    public TaskImportance ToEntity => new()
    {
        UserId = 0,
        Text = Text,
        Color = Color,
        Importance = Importance
    };

    public void UpdateEntity(TaskImportance entity)
    {
        entity.Text = Text;
        entity.Color = Color;
        entity.Importance = Importance;
    }
}