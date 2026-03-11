using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.application.dto.request.@base.table;
using AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.query;

public class FetchTableDistinctDesktopEntry(AppDbContext dbContext)
    : Endpoint<BaseFilterSortPaginateRequest<TrackerDesktopDistinctEntriesFilter>, BaseTableResponse<TrackerDesktopDistinctEntriesResponse>>
{
    public override void Configure()
    {
        const string entityName = "Distinct desktop entries";
        Post("/filtered-table");
        Summary(s =>
        {
            s.Summary = $"Get filtered and paginated {entityName} list";
            s.Description = $"Retrieves a filtered, paginated and sorted list of {entityName}";
            Roles(EndpointHelper.GetUserOrHigherRoles());

            s.Response<BaseTableResponse<TrackerDesktopDistinctEntriesResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
        Group<ActivityTrackingDesktopGroup>();
    }

    public override async Task HandleAsync(BaseFilterSortPaginateRequest<TrackerDesktopDistinctEntriesFilter> req, CancellationToken ct)
    {
        try
        {
            var query = dbContext.DesktopActivityEntries.AsNoTracking();

            query = query.FilteredByUser(User.GetId());

            if (req is { UseFilter: true, Filter: not null })
            {
                query = ApplyCustomFiltering(query, req.Filter);
            }

            var distinctQuery = query
                .GroupBy(e => new { e.ProductName, e.ProcessName, e.WindowTitle })
                .Select(g => new TrackerDesktopDistinctEntriesResponse
                {
                    Id = g.Min(e => e.Id),
                    ProcessName = g.Key.ProcessName,
                    ProductName = g.Key.ProductName,
                    WindowTitle = g.Key.WindowTitle
                });

            var itemsCount = await distinctQuery.CountAsync(ct);
            var pageCount = (int)Math.Ceiling((double)itemsCount / req.ItemsPerPage);

            var items = await distinctQuery.SortByManyAndPaginate(req.SortBy, req.ItemsPerPage, req.Page).ToListAsync(ct);

            await SendOkAsync(new BaseTableResponse<TrackerDesktopDistinctEntriesResponse>
            {
                Items = items,
                ItemsCount = itemsCount,
                PageCount = pageCount
            }, ct);
        }
        catch (Exception ex)
        {
            AddError($"An error occurred while retrieving filtered data: {ex.Message}");
            await SendErrorsAsync(500, ct);
        }
    }

    private static IQueryable<DesktopActivityEntry> ApplyCustomFiltering(IQueryable<DesktopActivityEntry> query, TrackerDesktopDistinctEntriesFilter filter)
    {
        if (filter is { ProcessName: not null, ProcessNameMatchType: not null })
            query = query.ApplyStringMatchFilter(e => e.ProcessName, filter.ProcessName, filter.ProcessNameMatchType ?? PatternMatchType.Exact);

        if (filter is { ProductName: not null, ProductNameMatchType: not null })
            query = query.ApplyStringMatchFilter(e => e.ProductName, filter.ProductName, filter.ProductNameMatchType ?? PatternMatchType.Exact);

        if (filter is { WindowTitle: not null, WindowTitleMatchType: not null })
            query = query.ApplyStringMatchFilter(e => e.WindowTitle, filter.WindowTitle, filter.WindowTitleMatchType.Value);

        return query;
    }
}