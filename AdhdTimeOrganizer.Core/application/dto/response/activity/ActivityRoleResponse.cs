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

    /// <summary>
    /// How many activities are filed under this role, <b>archived ones included</b> — they cascade the
    /// same and are the ones a user has forgotten owning.
    /// </summary>
    /// <remarks>
    /// ⚠ Same "default means not asked for" contract as <c>ActivityResponse.UsageCount</c>: the static
    /// <see cref="Projection"/> cannot afford a correlated count on every nested role a grid row carries,
    /// so only <c>GridActivityRoleEndpoint</c> (which overrides the projection so the column stays
    /// sortable) and <c>GetByIdActivityRoleEndpoint</c> fill this in. Elsewhere — including the role
    /// nested inside every <c>ActivityResponse</c> — it is <c>0</c> and means nothing.
    /// </remarks>
    public int UsageCount { get; init; }

    /// <summary>
    /// <c>DELETE /activity-role/{id}</c> would succeed. Unlike the activity-level flag this is an exact
    /// prediction of the endpoint's answer, not just a UI guard: the role delete refuses with 409 both for
    /// a <see cref="SystemKey"/> and for a role that still has activities, because <c>Activity.RoleId</c>
    /// is required and the FK cascades. Default <c>false</c>, so a value nobody filled in never offers a
    /// destructive button.
    /// </summary>
    public bool CanDelete { get; init; }

    public static IQueryable<ActivityRoleResponse> Projection(IQueryable<ActivityRole> query)
    {
        return query.Select(e => new ActivityRoleResponse
        {
            Id = e.Id, Name = e.Name, Text = e.Text, Color = e.Color, Icon = e.Icon, SystemKey = e.SystemKey
        });
    }
}