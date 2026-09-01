using AdhdTimeOrganizer.Core.application.dto.filter;
using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.role.query;

public class GridActivityRoleEndpoint(
    DbContext dbContext)
    : BaseGridEndpoint<ActivityRole, ActivityRoleResponse, RoleFilterRequest>(dbContext)
{
    /// <summary>
    /// Lifts the archival filter so <see cref="Projection"/>'s <c>Activities</c> subquery sees archived
    /// rows. Roles themselves are not archivable, so this touches nothing else on the grid — but an
    /// archived activity still blocks a role delete, so a count that hid one would put an enabled delete
    /// button in front of a 409.
    /// </summary>
    protected override IQueryable<ActivityRole> ApplyBaseFiltering(IQueryable<ActivityRole> query) =>
        query.IgnoreQueryFilters([AppQueryFilters.ActivityArchived]);

    /// <summary>
    /// Overridden so <c>usageCount</c> / <c>canDelete</c> are computed <b>in SQL</b>, for the same reason
    /// <c>GridActivityEndpoint</c> does it: the base sorts the projected queryable, so a field filled in by
    /// <c>PostProcessItemsAsync</c> would sort every row on its default and hand back an arbitrary page in
    /// the right-looking order. One correlated count per row of the page, bounded by the 200-row cap.
    /// The static <c>ActivityRoleResponse.Projection</c> stays cheap — it is what every nested role in an
    /// activity payload uses.
    /// </summary>
    protected override Func<IQueryable<ActivityRole>, IQueryable<ActivityRoleResponse>> Projection => query =>
        query.Select(e => new ActivityRoleResponse
        {
            Id = e.Id,
            Name = e.Name,
            Text = e.Text,
            Color = e.Color,
            Icon = e.Icon,
            SystemKey = e.SystemKey,
            UsageCount = e.Activities.Count(),
            // Both halves of DeleteActivityRoleEndpoint's refusal — a keyed role is undeletable however
            // empty it is.
            CanDelete = e.SystemKey == null && !e.Activities.Any()
        });

    protected override IQueryable<ActivityRole> ApplyCustomFiltering(IQueryable<ActivityRole> query, RoleFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Name))
            query = query.Where(r => r.Name.Contains(filter.Name));

        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(r => r.Text != null && r.Text.Contains(filter.Text));

        if (!string.IsNullOrWhiteSpace(filter.Color))
            query = query.Where(r => r.Color == filter.Color);

        if (!string.IsNullOrWhiteSpace(filter.Icon))
            query = query.Where(r => r.Icon != null && r.Icon.Contains(filter.Icon));

        return query;
    }
}