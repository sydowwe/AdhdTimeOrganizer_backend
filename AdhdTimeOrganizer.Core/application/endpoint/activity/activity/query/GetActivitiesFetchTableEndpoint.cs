using AdhdTimeOrganizer.Core.application.dto.filter;
using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.@base.table;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.activity.query;

public class GridActivityEndpoint(
    DbContext dbContext,
    IActivityReferenceService referenceService)
    : BaseGridEndpoint<Activity, ActivityResponse, ActivityFilterRequest>(dbContext)
{
    /// <summary>
    /// Whether the caller sent a filter object at all. Captured before the base runs because
    /// <see cref="ApplyBaseFiltering"/> — the only hook that runs on <em>every</em> request — is not
    /// handed the request, and the absent-filter default is the whole point (see below).
    /// </summary>
    private bool _filterSent;

    public override Task HandleAsync(BaseFilterSortPaginateRequest<ActivityFilterRequest> req, CancellationToken ct)
    {
        _filterSent = req is { UseFilter: true, Filter: not null };
        return base.HandleAsync(req, ct);
    }

    /// <summary>
    /// The unfiltered view means <b>active only</b>, not "everything".
    /// </summary>
    /// <remarks>
    /// This is the rule that lets the settings table's default view keep sending byte-for-byte the
    /// request it sent before A9 existed. Putting the default on <c>ActivityFilterRequest.IsArchived</c>
    /// instead would not work: <c>BaseGridEndpoint</c> skips <see cref="ApplyCustomFiltering"/> entirely
    /// when <c>useFilter</c> is false, so the property is never read and every archived row comes back
    /// into the one view most users never leave — with no error anywhere.
    /// </remarks>
    protected override IQueryable<Activity> ApplyBaseFiltering(IQueryable<Activity> query) =>
        _filterSent ? query : query.Where(a => !a.IsArchived);

    /// <summary>
    /// Overridden so <c>usageCount</c> and <c>canDelete</c> are computed <b>in SQL</b>, as part of the
    /// page's projection.
    /// </summary>
    /// <remarks>
    /// Not <c>PostProcessItemsAsync</c>, which would be cheaper and wrong: <c>BaseGridEndpoint</c> applies
    /// <c>SortByMany</c> to the projected queryable, so a field filled in afterwards sorts every row on
    /// <c>0</c> and <c>sortBy: [{ key: "usageCount" }]</c> would silently return an arbitrary page in the
    /// right-looking order. The settings table declares the column sortable, so this is the shape that
    /// has to work.
    /// <para>
    /// The cost is one correlated count over a <c>UNION ALL</c> of the twelve referencing tables, per row
    /// of the page. Bounded by the base's 200-row page cap and by the global user filter narrowing every
    /// table in the union. If it ever stops being affordable, the agreed fallback is <c>canDelete</c>
    /// everywhere and <c>usageCount</c> on <c>GET /activity/{id}</c> only — and the column loses its sort.
    /// </para>
    /// </remarks>
    protected override Func<IQueryable<Activity>, IQueryable<ActivityResponse>> Projection => query =>
    {
        var referencingIds = referenceService.ReferencingActivityIds(dbContext);

        return query.Select(e => new ActivityResponse
        {
            Id = e.Id,
            Name = e.Name,
            Text = e.Text,
            IsUnavoidable = e.IsUnavoidable,
            IsArchived = e.IsArchived,
            UsageCount = referencingIds.Count(id => id == e.Id),
            CanDelete = !referencingIds.Any(id => id == e.Id),
            Role = new ActivityRoleResponse { Id = e.Role.Id, Name = e.Role.Name, Text = e.Role.Text, Color = e.Role.Color, Icon = e.Role.Icon },
            Category = e.Category == null ? null : new ActivityCategoryResponse { Id = e.Category.Id, Name = e.Category.Name, Text = e.Category.Text, Color = e.Category.Color, Icon = e.Category.Icon }
        });
    };

    protected override IQueryable<Activity> ApplyCustomFiltering(IQueryable<Activity> query, ActivityFilterRequest filter)
    {
        // Tri-state on purpose: false = active only, true = archived only, null = both. Null is the only
        // way to see an archived row next to an active one, which the merge dialog's All view needs.
        if (filter.IsArchived.HasValue)
            query = query.Where(a => a.IsArchived == filter.IsArchived.Value);

        if (!string.IsNullOrWhiteSpace(filter.Name))
            query = query.Where(a => a.Name.Contains(filter.Name));

        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(a => a.Text != null && a.Text.Contains(filter.Text));

        if (filter.IsUnavoidable.HasValue)
            query = query.Where(a => a.IsUnavoidable == filter.IsUnavoidable.Value);

        if (!string.IsNullOrWhiteSpace(filter.RoleName))
            query = query.Where(a => a.Role.Name.Contains(filter.RoleName));

        if (!string.IsNullOrWhiteSpace(filter.CategoryName))
            query = query.Where(a => a.Category != null && a.Category.Name.Contains(filter.CategoryName));

        if (filter.RoleId.HasValue)
            query = query.Where(a => a.RoleId == filter.RoleId.Value);

        if (filter.CategoryId.HasValue)
            query = query.Where(a => a.CategoryId == filter.CategoryId.Value);

        return query;
    }
}
