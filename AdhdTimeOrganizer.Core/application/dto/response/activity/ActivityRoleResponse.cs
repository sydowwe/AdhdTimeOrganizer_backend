using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.Core.application.dto.response.activity;

public record ActivityRoleResponse : NameTextColorIconResponse, IProjectionResponse<ActivityRoleResponse, ActivityRole>
{
    /// <summary>
    /// <c>null</c> on user-created roles; one of the three keys on the roles the app looks up. Lets the
    /// settings grid mark those as system roles and warn before a delete the backend will refuse.
    /// </summary>
    public SystemActivityRole? SystemKey { get; init; }

    public static IQueryable<ActivityRoleResponse> Projection(IQueryable<ActivityRole> query)
    {
        return query.Select(e => new ActivityRoleResponse
        {
            Id = e.Id, Name = e.Name, Text = e.Text, Color = e.Color, Icon = e.Icon, SystemKey = e.SystemKey
        });
    }
}