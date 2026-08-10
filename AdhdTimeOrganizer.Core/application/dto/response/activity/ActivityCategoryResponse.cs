using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.Core.application.dto.response.activity;

public record ActivityCategoryResponse : NameTextColorIconResponse, IProjectionResponse<ActivityCategoryResponse, ActivityCategory>
{
    public string? Role { get; init; }

    public static IQueryable<ActivityCategoryResponse> Projection(IQueryable<ActivityCategory> query)
    {
        return query.Select(e => new ActivityCategoryResponse { Id = e.Id, Name = e.Name, Text = e.Text, Color = e.Color, Icon = e.Icon });
    }
}