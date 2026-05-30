using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner.template;

public record TemplatePlannerTaskRequest : BasePlannerTaskRequest, IMyRequest<TemplatePlannerTask>
{
    [Required]
    public required long TemplateId { get; init; }


    public TemplatePlannerTask ToEntity => new()
    {
        TemplateId = TemplateId,
        ActivityId = ActivityId,
        ImportanceId = ImportanceId,
        StartTime = StartTime.ToTimeOnly(),
        EndTime = EndTime.ToTimeOnly(),
        IsBackground = IsBackground,
        Location = Location,
        Notes = Notes,
    };

    public void UpdateEntity(TemplatePlannerTask entity)
    {
        entity.TemplateId = TemplateId;
        entity.ActivityId = ActivityId;
        entity.ImportanceId = ImportanceId;
        entity.StartTime = StartTime.ToTimeOnly();
        entity.EndTime = EndTime.ToTimeOnly();
        entity.IsBackground = IsBackground;
        entity.Location = Location;
        entity.Notes = Notes;
    }
}