using AdhdTimeOrganizer.Tracking.application.dto.@enum;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.endpoint.extensions;
using AdhdTimeOrganizer.Tracking.application.endpointGroups;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper.mapping;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using FastEndpoints;
using Sydowwe.Framework.application.dto.request.@base.table;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.exception;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.android.query;

public class FetchTableDistinctAndroidEntry(DbContext dbContext)
    : Endpoint<BaseFilterSortPaginateRequest<AndroidDistinctEntriesFilter>, BaseGridResponse<AndroidDistinctEntriesResponse>>
{
    public override void Configure()
    {
        const string entityName = "Distinct android entries";
        Post("/grid");
        Summary(s =>
        {
            s.Summary = $"Get filtered and paginated {entityName} list";
            s.Description = $"Retrieves a filtered, paginated and sorted list of {entityName}";


            s.Response<BaseGridResponse<AndroidDistinctEntriesResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
        Validator<FetchTableDistinctAndroidEntryValidator>();
        Group<ActivityTrackingAndroidGroup>();
    }

    public override async Task HandleAsync(BaseFilterSortPaginateRequest<AndroidDistinctEntriesFilter> req, CancellationToken ct)
    {
        try
        {
            var userId = User.GetId();
            var query = dbContext.Set<AndroidSessionData>().AsNoTracking();

            query = query.FilteredByUser(userId);

            if (req is { UseFilter: true, Filter: not null })
                query = ApplyCustomFiltering(query, req.Filter);

            var distinctQuery = query
                .GroupBy(e => new { e.PackageName, e.AppLabel })
                .Select(g => new AndroidDistinctEntriesResponse
                {
                    Id = g.Min(e => e.Id),
                    PackageName = g.Key.PackageName,
                    AppLabel = g.Key.AppLabel
                });

            // B24: the android twin of the desktop grid's chip. ⚠ On this source the sync does not apply the
            // rules yet, so `coveredBy` reads "an active rule exists that would match" and nothing more —
            // see AndroidMappingRuleSet, which is where the sync should resolve from when it does.
            var rules = new AndroidMappingRuleSet(await AndroidMappingRuleSet.ActiveFor(dbContext, userId)
                .AsNoTracking()
                .Include(m => m.Activity)
                .Include(m => m.Role)
                .Include(m => m.Category)
                .ToListAsync(ct));

            var coverage = req is { UseFilter: true, Filter.Coverage: { } requested } ? requested : MappingCoverage.All;

            var grid = await distinctQuery.ToCoverageGridAsync(req.SortBy, req.ItemsPerPage, req.Page, coverage,
                row => rules.Resolve(row.PackageName, row.AppLabel) is { } rule
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