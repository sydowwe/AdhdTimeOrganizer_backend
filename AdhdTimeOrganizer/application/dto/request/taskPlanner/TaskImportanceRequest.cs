using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@base;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner;

public record TaskImportanceRequest : TextColorRequest, IMyRequest<TaskImportance>
{
    [Required]
    public short Importance { get; init; }

    public TaskImportance ToEntity => new()
    {
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
