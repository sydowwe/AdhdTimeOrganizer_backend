using AdhdTimeOrganizer.domain.model.entity.activity;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.activity;

public record ActivityRoleResponse : NameTextColorIconResponse, IProjectionResponse<ActivityRoleResponse, ActivityRole>
{
    public static IQueryable<ActivityRoleResponse> Projection(IQueryable<ActivityRole> query)
    {
        return query.Select(e => new ActivityRoleResponse { Id = e.Id, Name = e.Name, Text = e.Text, Color = e.Color, Icon = e.Icon });
    }
}