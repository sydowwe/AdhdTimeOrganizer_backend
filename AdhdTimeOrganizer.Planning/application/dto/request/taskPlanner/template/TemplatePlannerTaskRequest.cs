using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner.template;

public record TemplatePlannerTaskRequest : BasePlannerTaskRequest, IMyRequest<TemplatePlannerTask>
{
    public required long TemplateId { get; init; }


    public TemplatePlannerTask ToEntity => new()
    {
        UserId = 0,
        TemplateId = TemplateId,
        ActivityId = ActivityId,
        ImportanceId = ImportanceId,
        StartTime = StartTime.ToTimeOnly(),
        EndTime = EndTime.ToTimeOnly(),
        IsBackground = IsBackground,
        Location = Location,
        Notes = Notes
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