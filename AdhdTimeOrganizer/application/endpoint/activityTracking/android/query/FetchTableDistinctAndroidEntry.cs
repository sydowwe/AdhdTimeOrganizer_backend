using AdhdTimeOrganizer.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.application.dto.request.@base.table;
using AdhdTimeOrganizer.application.dto.response.activityTracking.android;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.android.query;

public class FetchTableDistinctAndroidEntry(AppDbContext dbContext)
    : Endpoint<BaseFilterSortPaginateRequest<AndroidDistinctEntriesFilter>, BaseGridResponse<AndroidDistinctEntriesResponse>>
{
    public override void Configure()
    {
        const string entityName = "Distinct android entries";
        Post("/gird");
        Summary(s =>
        {
            s.Summary = $"Get filtered and paginated {entityName} list";
            s.Description = $"Retrieves a filtered, paginated and sorted list of {entityName}";
            

            s.Response<BaseGridResponse<AndroidDistinctEntriesResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
        Group<ActivityTrackingAndroidGroup>();
    }

    public override async Task HandleAsync(BaseFilterSortPaginateRequest<AndroidDistinctEntriesFilter> req, CancellationToken ct)
    {
        try
        {
            var query = dbContext.AndroidSessionDataEntries.AsNoTracking();

            query = query.FilteredByUser(User.GetId());

            if (req is { UseFilter: true, Filter: not null })
            {
                query = ApplyCustomFiltering(query, req.Filter);
            }

            var distinctQuery = query
                .GroupBy(e => new { e.PackageName, e.AppLabel })
                .Select(g => new AndroidDistinctEntriesResponse
                {
                    Id = g.Min(e => e.Id),
                    PackageName = g.Key.PackageName,
                    AppLabel = g.Key.AppLabel
                });

            var itemsCount = await distinctQuery.CountAsync(ct);
            var pageCount = (int)Math.Ceiling((double)itemsCount / req.ItemsPerPage);

            var items = await distinctQuery.SortByManyAndPaginate(req.SortBy, req.ItemsPerPage, req.Page).ToListAsync(ct);

            await Send.OkAsync(new BaseGridResponse<AndroidDistinctEntriesResponse>
            {
                Items = items,
                ItemsCount = itemsCount,
                PageCount = pageCount
            }, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving distinct android entries");
            AddError("An internal error occurred.");
            await Send.ErrorsAsync(500, ct);
        }
    }

    private static IQueryable<AndroidSessionData> ApplyCustomFiltering(IQueryable<AndroidSessionData> query, AndroidDistinctEntriesFilter filter)
    {
        if (filter is { PackageName: not null, PackageNameMatchType: not null })
            query = query.ApplyStringMatchFilter(e => e.PackageName, filter.PackageName, filter.PackageNameMatchType ?? PatternMatchType.Exact);

        if (filter is { AppLabel: not null, AppLabelMatchType: not null })
            query = query.ApplyStringMatchFilter(e => e.AppLabel, filter.AppLabel, filter.AppLabelMatchType ?? PatternMatchType.Exact);

        return query;
    }
}
