using AdhdTimeOrganizer.Tracking.application.dto.@enum;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.extensions;

/// <summary>
/// Pages a distinct-entries grid and fills in each row's <c>coveredBy</c> chip, in one copy for the desktop
/// and android grids (B24).
/// </summary>
/// <remarks>
/// <para>
/// Patterns are matched in memory, never in SQL (the <c>PatternMatchType</c> modes have no translation
/// that agrees with the .NET matcher the ingest uses), so <c>coverage</c> cannot be a <c>WHERE</c> clause.
/// Two shapes, chosen by the filter:
/// </para>
/// <list type="bullet">
/// <item><see cref="MappingCoverage.All"/> — the count and the page stay in SQL exactly as before, and
/// only the ≤ 200 rows of the page are resolved against the rule set.</item>
/// <item><see cref="MappingCoverage.Uncovered"/> / <see cref="MappingCoverage.Covered"/> — the whole
/// sorted distinct set is materialised, resolved, filtered and then paged in memory, so
/// <c>itemsCount</c> and <c>pageCount</c> are exact and "12 still to decide" can be a header. This is
/// the one place the grid reads more than a page; the set is one user's distinct tuples against their
/// own rule list, which is what one heartbeat already does per window.</item>
/// </list>
/// <para>
/// Sorting stays SQL-side in both shapes (<c>SortByMany</c> over the projection), so the order a page is
/// cut from is the same order the unfiltered grid shows.
/// </para>
/// </remarks>
public static class MappingCoverageGridExtensions
{
    public static async Task<BaseGridResponse<TRow>> ToCoverageGridAsync<TRow>(
        this IQueryable<TRow> distinctQuery,
        SortByRequest[] sortBy,
        int itemsPerPage,
        int page,
        MappingCoverage coverage,
        Func<TRow, MappingCoverageResponse?> resolve,
        CancellationToken ct)
        where TRow : class, IIdResponse, IMappingCoverageRow
    {
        List<TRow> items;
        int itemsCount;

        if (coverage == MappingCoverage.All)
        {
            itemsCount = await distinctQuery.CountAsync(ct);
            items = await distinctQuery.SortByManyAndPaginate(sortBy, itemsPerPage, page).ToListAsync(ct);

            foreach (var item in items)
                item.CoveredBy = resolve(item);
        }
        else
        {
            var wantCovered = coverage == MappingCoverage.Covered;
            var all = await distinctQuery.SortByMany(sortBy).ToListAsync(ct);

            foreach (var item in all)
                item.CoveredBy = resolve(item);

            var filtered = all.Where(item => (item.CoveredBy != null) == wantCovered).ToList();

            itemsCount = filtered.Count;
            items = filtered.Skip((page - 1) * itemsPerPage).Take(itemsPerPage).ToList();
        }

        return new BaseGridResponse<TRow>
        {
            Items = items,
            ItemsCount = itemsCount,
            PageCount = (int)Math.Ceiling((double)itemsCount / itemsPerPage)
        };
    }
}
