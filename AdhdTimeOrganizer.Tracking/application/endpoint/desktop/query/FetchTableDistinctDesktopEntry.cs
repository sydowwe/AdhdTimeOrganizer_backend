using AdhdTimeOrganizer.Tracking.application.dto.@enum;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.application.endpoint.extensions;
using AdhdTimeOrganizer.Tracking.application.endpointGroups;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper.mapping;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FastEndpoints;
using Sydowwe.Framework.application.dto.request.@base.table;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.exception;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.desktop.query;

public class FetchTableDistinctDesktopEntry(DbContext dbContext)
    : Endpoint<BaseFilterSortPaginateRequest<TrackerDesktopDistinctEntriesFilter>, BaseGridResponse<TrackerDesktopDistinctEntriesResponse>>
{
    public override void Configure()
    {
        const string entityName = "Distinct desktop entries";
        Post("/grid");
        Summary(s =>
        {
            s.Summary = $"Get filtered and paginated {entityName} list";
            s.Description = $"Retrieves a filtered, paginated and sorted list of {entityName}";


            s.Response<BaseGridResponse<TrackerDesktopDistinctEntriesResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
        Validator<FetchTableDistinctDesktopEntryValidator>();
        Group<ActivityTrackingDesktopGroup>();
    }

    public override async Task HandleAsync(BaseFilterSortPaginateRequest<TrackerDesktopDistinctEntriesFilter> req, CancellationToken ct)
    {
        try
        {
            var userId = User.GetId();
            var query = dbContext.Set<DesktopActivityEntry>().AsNoTracking();

            query = query.FilteredByUser(userId);

            if (req is { UseFilter: true, Filter: not null })
                query = ApplyCustomFiltering(query, req.Filter);

            var distinctQuery = query
                .GroupBy(e => new { e.ProductName, e.ProcessName, e.WindowTitle })
                .Select(g => new TrackerDesktopDistinctEntriesResponse
                {
                    Id = g.Min(e => e.Id),
                    ProcessName = g.Key.ProcessName,
                    ProductName = g.Key.ProductName,
                    WindowTitle = g.Key.WindowTitle
                });

            // B24: the same rule set the heartbeat attributes with, so `coveredBy` is what the ingest would
            // do with the tuple today — not a client-side approximation over two paged lists. The target
            // navigations are loaded for the chip text; the heartbeat has no use for them.
            var rules = new DesktopMappingRuleSet(await DesktopMappingRuleSet.ActiveFor(dbContext, userId)
                .AsNoTracking()
                .Include(m => m.Activity)
                .Include(m => m.Role)
                .Include(m => m.Category)
                .ToListAsync(ct));

            var coverage = req is { UseFilter: true, Filter.Coverage: { } requested } ? requested : MappingCoverage.All;

            var grid = await distinctQuery.ToCoverageGridAsync(req.SortBy, req.ItemsPerPage, req.Page, coverage,
                row => rules.Resolve(row.ProcessName, row.ProductName, row.WindowTitle) is { } rule
                    ? MappingCoverageResponse.From(rule)
                    : null,
                ct);

            await Send.OkAsync(grid, ct);
        }
        // A sort key that resolves to nothing is the caller's mistake, not this endpoint's: let it out to
        // InvalidSortKeyMiddleware, which answers 400 naming the key. Swallowing it here reported a client
        // error as "An internal error occurred." with a 500 — see InvalidSortKeyException.
        catch (InvalidSortKeyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving distinct desktop entries");
            AddError("An internal error occurred.");
            await Send.ErrorsAsync(500, ct);
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