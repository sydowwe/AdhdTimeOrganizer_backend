using AdhdTimeOrganizer.Core.application.dto.filter;
using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.category.query;

public class GridActivityCategoryEndpoint(
    DbContext dbContext)
    : BaseGridEndpoint<ActivityCategory, ActivityCategoryGridResponse, CategoryFilterRequest>(dbContext)
{
    /// <summary>
    /// The grid shows <c>role</c>, but that column is a client-side <c>string.Join</c> and cannot appear in
    /// an <c>ORDER BY</c> — sorting on it throws an untranslatable-LINQ 500. Redirect the key onto the
    /// aggregate proxy, which orders by the same leading role name. See
    /// <see cref="ActivityCategoryGridResponse.RoleSortKey"/>.
    /// </summary>
    /// <summary>
    /// Lifts the archival filter so <c>ActivityCategoryGridResponse.Projection</c>'s <c>usageCount</c> sees
    /// every activity a delete would reclassify, archived included. Categories are not archivable
    /// themselves, and the projection's <c>role</c> column re-applies <c>!IsArchived</c> by hand, so this
    /// changes nothing else on the grid.
    /// </summary>
    protected override IQueryable<ActivityCategory> ApplyBaseFiltering(IQueryable<ActivityCategory> query) =>
        query.IgnoreQueryFilters([AppQueryFilters.ActivityArchived]);

    protected override SortByRequest[] PreprocessSortBy(SortByRequest[] sortBy) =>
        sortBy.Select(s => s.Key.Pascalize() == nameof(ActivityCategoryGridResponse.Role)
                ? s with { Key = nameof(ActivityCategoryGridResponse.RoleSortKey) }
                : s)
            .ToArray();

    protected override IQueryable<ActivityCategory> ApplyCustomFiltering(IQueryable<ActivityCategory> query, CategoryFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Name))
            query = query.Where(c => c.Name.Contains(filter.Name));

        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(c => c.Text != null && c.Text.Contains(filter.Text));

        if (!string.IsNullOrWhiteSpace(filter.Color))
            query = query.Where(c => c.Color == filter.Color);

        return query;
    }
}