using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.domain.extension;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.application.dto.response.taskPlanner.template;

public record TemplatePlannerTaskResponse : BasePlannerTaskResponse, IProjectionResponse<TemplatePlannerTaskResponse, TemplatePlannerTask>
{
    public required long TemplateId { get; init; }
    public required string Color { get; init; }

    public static IQueryable<TemplatePlannerTaskResponse> Projection(IQueryable<TemplatePlannerTask> query) =>
        query.Select(e => FromEntity(e));

    public static TemplatePlannerTaskResponse FromEntity(TemplatePlannerTask entity) => new TemplatePlannerTaskResponse
    {
        Id = entity.Id,
        StartTime = entity.StartTime.ToDto,
        EndTime = entity.EndTime.ToDto,
        IsBackground = entity.IsBackground,
        Location = entity.Location,
        Notes = entity.Notes,
        Activity = ActivityResponse.FromEntity(entity.Activity),
        Importance = entity.Importance == null ? null : new TaskImportanceResponse
        {
            Id = entity.Importance.Id,
            Text = entity.Importance.Text,
            Color = entity.Importance.Color,
            Icon = entity.Importance.Icon,
            Importance = entity.Importance.Importance,
        },
        TemplateId = entity.TemplateId,
        Color = entity.Color,
    };
}