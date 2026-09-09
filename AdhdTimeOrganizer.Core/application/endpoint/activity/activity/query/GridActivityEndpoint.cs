using AdhdTimeOrganizer.Core.application.dto.filter;
using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.Core.infrastructure.persistence;
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

    private readonly DbContext _dbContext = dbContext;

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
    /// <para>
    /// ⚠ <b>Deriving <c>canDelete</c> from <c>usageCount</c> to "save a scan" makes it worse — measured,
    /// don't redo it.</b> The obvious rewrite (project the count into a transparent identifier, then
    /// <c>CanDelete = x.UsageCount == 0</c>) does not give EF one subquery to reuse: it inlines the
    /// identifier, and the comparison brings nullable semantics with it, so the page query comes back as
    /// <b>three</b> union subqueries — <c>count(*)</c> for the column, then <c>count(*) = 0 AND count(*)
    /// IS NOT NULL</c> for the boolean. The shape below is two, and the second is an <c>EXISTS</c>, which
    /// stops at the first matching row instead of counting to the end.
    /// </para>
    /// </remarks>
    protected override Func<IQueryable<Activity>, IQueryable<ActivityResponse>> Projection => query =>
    {
        var referencingIds = referenceService.ReferencingActivityIds(_dbContext);

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
        if (filter.IsArchived != false)
            query = query.IgnoreQueryFilters([AppQueryFilters.ActivityArchived]);

        if (filter.IsArchived.HasValue)
            query = query.Where(a => a.IsArchived == filter.IsArchived.Value);

        // ILIKE, not Contains: Contains is case-sensitive in Postgres and neither shape is indexable.
        // See FreeTextFilterExtensions.
        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            var pattern = filter.Name.ToContainsPattern();
            query = query.Where(a => EF.Functions.ILike(a.Name, pattern, FreeTextFilterExtensions.EscapeCharacter));
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var pattern = filter.Text.ToContainsPattern();
            query = query.Where(a => a.Text != null && EF.Functions.ILike(a.Text, pattern, FreeTextFilterExtensions.EscapeCharacter));
        }

        if (filter.IsUnavoidable.HasValue)
            query = query.Where(a => a.IsUnavoidable == filter.IsUnavoidable.Value);

        if (!string.IsNullOrWhiteSpace(filter.RoleName))
        {
            var pattern = filter.RoleName.ToContainsPattern();
            query = query.Where(a => EF.Functions.ILike(a.Role.Name, pattern, FreeTextFilterExtensions.EscapeCharacter));
        }

        if (!string.IsNullOrWhiteSpace(filter.CategoryName))
        {
            var pattern = filter.CategoryName.ToContainsPattern();
            query = query.Where(a => a.Category != null && EF.Functions.ILike(a.Category.Name, pattern, FreeTextFilterExtensions.EscapeCharacter));
        }

        if (filter.RoleId.HasValue)
            query = query.Where(a => a.RoleId == filter.RoleId.Value);

        if (filter.CategoryId.HasValue)
            query = query.Where(a => a.CategoryId == filter.CategoryId.Value);

        return query;
    }
}