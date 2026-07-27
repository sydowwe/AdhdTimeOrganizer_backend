using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.domain.model.entity.timer;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.timer;

public record TimerPresetResponse : IdResponse, IProjectionResponse<TimerPresetResponse, TimerPreset>
{
    public required int Duration { get; init; }
    public ActivityResponse? Activity { get; init; }

    public static IQueryable<TimerPresetResponse> Projection(IQueryable<TimerPreset> q)
    {
        return q.Select(e => new TimerPresetResponse
        {
            Id = e.Id,
            Duration = e.Duration,
            Activity = e.Activity == null
                ? null
                : new ActivityResponse
                {
                    Id = e.Activity.Id,
                    Name = e.Activity.Name,
                    Text = e.Activity.Text,
                    IsUnavoidable = e.Activity.IsUnavoidable,
                    Role = new ActivityRoleResponse
                    {
                        Id = e.Activity.Role.Id,
                        Name = e.Activity.Role.Name,
                        Text = e.Activity.Role.Text,
                        Color = e.Activity.Role.Color,
                        Icon = e.Activity.Role.Icon
                    },
                    Category = e.Activity.Category == null
                        ? null
                        : new ActivityCategoryResponse
                        {
                            Id = e.Activity.Category.Id,
                            Name = e.Activity.Category.Name,
                            Text = e.Activity.Category.Text,
                            Color = e.Activity.Category.Color,
                            Icon = e.Activity.Category.Icon
                        }
                }
        });
    }
}