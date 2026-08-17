using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.dto.response;

namespace AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner.template;

public record TemplatePlannerTaskResponse : BasePlannerTaskResponse, IProjectionResponse<TemplatePlannerTaskResponse, TemplatePlannerTask>
{
    public required long TemplateId { get; init; }
    public required string Color { get; init; }

    public static IQueryable<TemplatePlannerTaskResponse> Projection(IQueryable<TemplatePlannerTask> query)
    {
        return query.Select(entity => new TemplatePlannerTaskResponse
        {
            Id = entity.Id,
            // Inlined rather than TimeOnly.ToDto: extension properties can't appear in an expression tree (CS9296).
            StartTime = new TimeDto(entity.StartTime.Hour, entity.StartTime.Minute),
            EndTime = new TimeDto(entity.EndTime.Hour, entity.EndTime.Minute),
            IsBackground = entity.IsBackground,
            Location = entity.Location,
            Notes = entity.Notes,
            Activity = new ActivityResponse
            {
                Id = entity.Activity.Id,
                Name = entity.Activity.Name,
                Text = entity.Activity.Text,
                IsUnavoidable = entity.Activity.IsUnavoidable,
                Role = new ActivityRoleResponse
                {
                    Id = entity.Activity.Role.Id,
                    Name = entity.Activity.Role.Name,
                    Text = entity.Activity.Role.Text,
                    Color = entity.Activity.Role.Color,
                    Icon = entity.Activity.Role.Icon
                },
                Category = entity.Activity.Category == null
                    ? null
                    : new ActivityCategoryResponse
                    {
                        Id = entity.Activity.Category.Id,
                        Name = entity.Activity.Category.Name,
                        Text = entity.Activity.Category.Text,
                        Color = entity.Activity.Category.Color,
                        Icon = entity.Activity.Category.Icon
                    }
            },
            Importance = entity.Importance == null
                ? null
                : new TaskImportanceResponse
                {
                    Id = entity.Importance.Id,
                    Text = entity.Importance.Text,
                    Color = entity.Importance.Color,
                    Icon = entity.Importance.Icon,
                    Importance = entity.Importance.Importance
                },
            TemplateId = entity.TemplateId,
            // Inlined rather than entity.Color: EF Core can't translate a call to that computed property inside
            // a .Select() that becomes SQL (no implicit client evaluation since EF Core 3).
            Color = entity.Activity.Role.Color
        });
    }
}